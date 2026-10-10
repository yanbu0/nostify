
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace nostify;

/// <summary>
/// Initialize projections using durable orchestration, for use with large datasets that may exceed execution time of a single azure function.
/// </summary>
/// <typeparam name="TProjection">The type of the projection.</typeparam>
/// <typeparam name="TAggregate">The type of the aggregate.</typeparam>
public class DurableProjectionInitializer<TProjection, TAggregate>
    where TProjection : NostifyObject, IProjection, IHasExternalData<TProjection>, new()
    where TAggregate : NostifyObject, IAggregate, new()
{
    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    private readonly string _instanceId;
    private readonly IQueryExecutor _queryExecutor;

    private readonly int _batchSize;
    private readonly int _pageSize;

    private readonly TaskOptions _durableTaskOptions;
    private readonly RetryOptions _cosmosRetryOptions;

    private static readonly Action<ILogger, string, Exception?> LogDeletingProjections =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(LogDeletingProjections)),
            "{InstanceId}: delete all projections");

    private static readonly Action<ILogger, string, int, Exception?> LogTenantCount =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(2, nameof(LogTenantCount)),
            "{InstanceId}: processing {TenantCount} tenants");

    private static readonly Action<ILogger, string, int, Exception?> LogPartitionCount =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(3, nameof(LogPartitionCount)),
            "{InstanceId}: processing {PartitionCount} partitions");

    private static readonly Action<ILogger, string, int, Exception?> LogProcessedCount =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(4, nameof(LogProcessedCount)),
            "{InstanceId}: {ProjectionCount} projections processed");

    private static readonly Action<ILogger, string, Exception?> LogInitializationComplete =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(5, nameof(LogInitializationComplete)),
            "{InstanceId}: projection initialization complete");

    private static readonly Action<ILogger, string, Guid, int, Exception?> LogRollingFallback =
        LoggerMessage.Define<string, Guid, int>(
            LogLevel.Warning,
            new EventId(6, nameof(LogRollingFallback)),
            "{InstanceId}: unconditional rolling write for hot projection {ProjectionId} after {ConflictCount} concurrency conflicts");

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableProjectionInitializer{TProjection, TAggregate}"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to make external data requests.</param>
    /// <param name="nostify">The Nostify instance.</param>
    /// <param name="instanceId">The instance Id; only one orchestration can run at a time with this Id.</param>
    /// <param name="batchSize">The number of projections to initialize in a batch.</param>
    /// <param name="concurrentBatchCount">The number of concurrent batches to process.</param>
    /// <param name="durableTaskOptions">Retry options for the orchestrator - if null, default TaskOptions are used (see <see cref="CreateDefaultTaskOptions"/>)</param>
    /// <param name="cosmosRetryOptions">Retry options for Cosmos DB operations - if null, default RetryOptions are used</param>
    public DurableProjectionInitializer(
        HttpClient httpClient,
        INostify nostify,
        string instanceId,
        int batchSize = 1000,
        int concurrentBatchCount = 5,
        TaskOptions? durableTaskOptions = null,
        RetryOptions? cosmosRetryOptions = null)
        : this(
            httpClient,
            nostify,
            instanceId,
            batchSize,
            concurrentBatchCount,
            durableTaskOptions,
            cosmosRetryOptions,
            CosmosQueryExecutor.Default)
    {
    }

    /// <summary>
    /// Creates an initializer with an explicit query executor, allowing the event replay path to be
    /// tested against already-deserialized Cosmos documents without requiring a live Cosmos account.
    /// </summary>
    internal DurableProjectionInitializer(
        HttpClient httpClient,
        INostify nostify,
        string instanceId,
        int batchSize,
        int concurrentBatchCount,
        TaskOptions? durableTaskOptions,
        RetryOptions? cosmosRetryOptions,
        IQueryExecutor queryExecutor)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(nostify);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrentBatchCount);
        ArgumentNullException.ThrowIfNull(queryExecutor);

        _httpClient = httpClient;
        _nostify = nostify;
        _instanceId = instanceId ?? $"{typeof(TProjection).FullName ?? typeof(TProjection).Name}_Init";
        _queryExecutor = queryExecutor;

        _batchSize = batchSize;
        _pageSize = checked(batchSize * concurrentBatchCount);

        _durableTaskOptions = durableTaskOptions ?? CreateDefaultTaskOptions();
        _cosmosRetryOptions = cosmosRetryOptions ?? new RetryOptions();
    }

    /// <summary>
    /// Starts the durable orchestration to initialize projections. If an orchestration with the same instance Id is already active (Running or Pending), returns a 409 Conflict response.
    /// </summary>
    /// <param name="req">The HTTP request data from the Azure Function.</param>
    /// <param name="client">The durable task client injected by the Azure host.</param>
    /// <param name="orchestratorName">The name of the orchestrator.</param>
    /// <returns>The HTTP response with 409 if active, otherwise 202 response with a Location header and a payload containing instance control URLs.</returns>
    public async Task<HttpResponseData> StartOrchestration(
        HttpRequestData req,
        DurableTaskClient client,
        string orchestratorName)
    {
        var existing = await client.GetInstanceAsync(_instanceId);

        // only allow one orchestration to run at a time — block if the instance is still active
        if (IsInstanceActive(existing))
        {
            // already active, return 409 Conflict
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            conflict.WriteString($"{_instanceId} is already running");
            return conflict;
        }

        await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, new StartOrchestrationOptions(_instanceId));
        return await client.CreateCheckStatusResponseAsync(req, _instanceId);
    }

    /// <summary>
    /// Starts a non-destructive rolling projection orchestration with immutable input.
    /// </summary>
    /// <param name="req">The triggering HTTP request.</param>
    /// <param name="client">The Durable task client.</param>
    /// <param name="orchestratorName">The rolling orchestrator name.</param>
    /// <param name="input">Rolling rebuild input.</param>
    /// <returns>A conflict response when this initializer is active; otherwise, a status response.</returns>
    public async Task<HttpResponseData> StartRollingOrchestration(
        HttpRequestData req,
        DurableTaskClient client,
        string orchestratorName,
        DurableRollingProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await client.GetInstanceAsync(_instanceId);
        if (IsInstanceActive(existing))
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            conflict.WriteString($"{_instanceId} is already running");
            return conflict;
        }

        ValidateSelectedProperties(input.Options);
        await client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName,
            input,
            new StartOrchestrationOptions(_instanceId));
        return await client.CreateCheckStatusResponseAsync(req, _instanceId);
    }

    /// <summary>
    /// Cancels the durable orchestration if it is active, and purges the instance after cancellation.
    /// </summary>
    /// <param name="req">The HTTP request data from the Azure Function.</param>
    /// <param name="client">The durable task client injected by the Azure host.</param>
    /// <returns>The HTTP response data.</returns>
    public async Task<HttpResponseData> CancelOrchestration(
        HttpRequestData req,
        DurableTaskClient client)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        var existing = await client.GetInstanceAsync(_instanceId);
        if (existing != null)
        {
            if (IsInstanceActive(existing))
            {
                // Suspended orchestrations must be resumed before termination can complete.
                if (existing.RuntimeStatus == OrchestrationRuntimeStatus.Suspended)
                {
                    await client.ResumeInstanceAsync(_instanceId, $"{_instanceId} resumed to cancel");
                }

                await client.TerminateInstanceAsync(_instanceId, $"{_instanceId} cancelled");
                await client.WaitForInstanceCompletionAsync(_instanceId);

                resp.WriteString($"{_instanceId} cancelled");
            }

            existing = await client.GetInstanceAsync(_instanceId);
            if (existing != null && !existing.IsRunning)
            {
                await client.PurgeInstanceAsync(_instanceId);
            }
        }

        return resp;
    }

    /// <summary>
    /// Checks if an activity should be cancelled
    /// </summary>
    public async Task<bool> IsCancellationRequestedAsync(DurableTaskClient client, CancellationToken cancellationToken = default)
    {
        var existing = await client.GetInstanceAsync(_instanceId, false, cancellationToken);
        return !IsInstanceActive(existing);
    }

    /// <summary>
    /// Checks if an orchestration is active
    /// </summary>
    private static bool IsInstanceActive(OrchestrationMetadata? metadata)
        => metadata != null
            && (metadata.RuntimeStatus == OrchestrationRuntimeStatus.Running
                || metadata.RuntimeStatus == OrchestrationRuntimeStatus.Pending
                || metadata.RuntimeStatus == OrchestrationRuntimeStatus.Suspended);

    /// <summary>
    /// The orchestrator that runs the projection initialization logic, paging through aggregates by tenant partition.
    /// </summary>
    /// <param name="context">The orchestration context provided by the durable task framework.</param>
    /// <param name="deleteActivityName">The name of the activity function that deletes projections.</param>
    /// <param name="getTenantIdsActivityName">The name of the activity function that retrieves tenant IDs.</param>
    /// <param name="getIdsActivityName">The name of the activity function that retrieves aggregate IDs for a tenant.</param>
    /// <param name="processBatchActivityName">The name of the activity function that processes a batch of aggregates to initialize projections.</param>
    /// <param name="logger">Use `context.CreateReplaySafeLogger` for deployments to avoid logging duplicate messages during orchestration replay.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OrchestrateInitAsync(
        TaskOrchestrationContext context,
        string deleteActivityName,
        string getTenantIdsActivityName,
        string getIdsActivityName,
        string processBatchActivityName,
        ILogger? logger = null)
        => OrchestrateInitAsync(
            context,
            new DurableTenantInitActivityNames(
                deleteActivityName,
                getTenantIdsActivityName,
                getIdsActivityName,
                processBatchActivityName),
            logger);

    /// <summary>
    /// Runs tenant-partitioned projection initialization using a strongly typed activity-name group.
    /// Aggregate IDs remain the complete process-activity payload because event streams are keyed by aggregate ID.
    /// </summary>
    /// <param name="context">The orchestration context provided by the durable task framework.</param>
    /// <param name="activities">The activity function names used by the orchestration.</param>
    /// <param name="logger">Use a replay-safe logger to avoid duplicate messages during orchestration replay.</param>
    public async Task OrchestrateInitAsync(
        TaskOrchestrationContext context,
        DurableTenantInitActivityNames activities,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(activities);

        // Delete projections before rebuilding them from the event store.
        await context.CallActivityAsync(activities.Delete, null, _durableTaskOptions);
        if (logger != null)
        {
            LogDeletingProjections(logger, _instanceId, null);
        }

        // Discover aggregate IDs within each current-state tenant partition.
        List<Guid> tenantIds = await context.CallActivityAsync<List<Guid>>(
            activities.GetTenantIds,
            null,
            _durableTaskOptions);
        if (logger != null)
        {
            LogTenantCount(logger, _instanceId, tenantIds.Count, null);
        }

        int totalProcessed = 0;

        foreach (var tenantId in tenantIds)
        {
            await ProcessPartitionPagesAsync(
                context,
                activities.GetIds,
                activities.ProcessBatch,
                lastSeenId => new DurableInitPageInfo(tenantId, lastSeenId),
                count =>
                {
                    totalProcessed += count;
                    if (logger != null)
                    {
                        LogProcessedCount(logger, _instanceId, totalProcessed, null);
                    }
                });
        }

        if (logger != null)
        {
            LogInitializationComplete(logger, _instanceId, null);
        }
    }

    /// <summary>
    /// Runs a non-destructive, tenant-partitioned rolling projection rebuild.
    /// </summary>
    /// <param name="context">Durable orchestration context.</param>
    /// <param name="activities">Rolling activity names.</param>
    /// <param name="input">Immutable rolling options.</param>
    /// <param name="logger">Replay-safe logger.</param>
    public async Task OrchestrateRollingInitAsync(
        TaskOrchestrationContext context,
        DurableRollingTenantInitActivityNames activities,
        DurableRollingProjectionInput input,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(input);

        List<Guid> tenantIds = await context.CallActivityAsync<List<Guid>>(
            activities.GetTenantIds,
            null,
            _durableTaskOptions);
        if (logger != null)
        {
            LogTenantCount(logger, _instanceId, tenantIds.Count, null);
        }

        int totalProcessed = 0;
        foreach (Guid tenantId in tenantIds)
        {
            await ProcessRollingPartitionPagesAsync(
                context,
                activities.GetIds,
                activities.ProcessBatch,
                lastSeenId => new DurableInitPageInfo(tenantId, lastSeenId),
                id => new DurableRollingProjectionWorkItem(id, tenantId.ToString(), true),
                input.Options,
                count =>
                {
                    totalProcessed += count;
                    if (logger != null)
                    {
                        LogProcessedCount(logger, _instanceId, totalProcessed, null);
                    }
                });
        }

        if (logger != null)
        {
            LogInitializationComplete(logger, _instanceId, null);
        }
    }

    /// <summary>
    /// The orchestrator that runs the projection initialization logic, paging through aggregates by an arbitrary partition key.
    /// Use this overload when the aggregate's container is partitioned by something other than tenantId.
    /// </summary>
    /// <param name="context">The orchestration context provided by the durable task framework.</param>
    /// <param name="deleteActivityName">The name of the activity function that deletes projections.</param>
    /// <param name="getPartitionKeysActivityName">The name of the activity function that retrieves the distinct partition key values (as strings) from the aggregate container.</param>
    /// <param name="getIdsActivityName">The name of the activity function that retrieves aggregate IDs for a partition. Must accept a <see cref="DurablePartitionInitPageInfo"/> input.</param>
    /// <param name="processBatchActivityName">The name of the activity function that processes a batch of aggregates to initialize projections.</param>
    /// <param name="logger">Use `context.CreateReplaySafeLogger` for deployments to avoid logging duplicate messages during orchestration replay.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task OrchestrateInitByPartitionAsync(
        TaskOrchestrationContext context,
        string deleteActivityName,
        string getPartitionKeysActivityName,
        string getIdsActivityName,
        string processBatchActivityName,
        ILogger? logger = null
        )
    {
        // delete Projections
        await context.CallActivityAsync(deleteActivityName, null, _durableTaskOptions);
        if (logger != null)
        {
            LogDeletingProjections(logger, _instanceId, null);
        }

        // get partition key values to page through
        List<string> partitionKeys = await context.CallActivityAsync<List<string>>(getPartitionKeysActivityName, null, _durableTaskOptions);
        if (logger != null)
        {
            LogPartitionCount(logger, _instanceId, partitionKeys.Count, null);
        }

        int totalProcessed = 0;

        foreach (var pk in partitionKeys)
        {
            await ProcessPartitionPagesAsync(
                context,
                getIdsActivityName,
                processBatchActivityName,
                lastSeenId => new DurablePartitionInitPageInfo(pk, lastSeenId),
                count =>
                {
                    totalProcessed += count;
                    if (logger != null)
                    {
                        LogProcessedCount(logger, _instanceId, totalProcessed, null);
                    }
                });
        }

        if (logger != null)
        {
            LogInitializationComplete(logger, _instanceId, null);
        }
    }

    /// <summary>
    /// Runs a non-destructive rolling rebuild using an arbitrary string partition key.
    /// </summary>
    /// <param name="context">Durable orchestration context.</param>
    /// <param name="getPartitionKeysActivityName">Distinct partition-key activity name.</param>
    /// <param name="getIdsActivityName">Paged aggregate-ID activity name.</param>
    /// <param name="processBatchActivityName">Rolling batch activity name.</param>
    /// <param name="input">Immutable rolling options.</param>
    /// <param name="logger">Replay-safe logger.</param>
    public async Task OrchestrateRollingInitByPartitionAsync(
        TaskOrchestrationContext context,
        string getPartitionKeysActivityName,
        string getIdsActivityName,
        string processBatchActivityName,
        DurableRollingProjectionInput input,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<string> partitionKeys = await context.CallActivityAsync<List<string>>(
            getPartitionKeysActivityName,
            null,
            _durableTaskOptions);
        if (logger != null)
        {
            LogPartitionCount(logger, _instanceId, partitionKeys.Count, null);
        }

        int totalProcessed = 0;
        foreach (string partitionKey in partitionKeys)
        {
            await ProcessRollingPartitionPagesAsync(
                context,
                getIdsActivityName,
                processBatchActivityName,
                lastSeenId => new DurablePartitionInitPageInfo(partitionKey, lastSeenId),
                id => new DurableRollingProjectionWorkItem(id, partitionKey, false),
                input.Options,
                count =>
                {
                    totalProcessed += count;
                    if (logger != null)
                    {
                        LogProcessedCount(logger, _instanceId, totalProcessed, null);
                    }
                });
        }

        if (logger != null)
        {
            LogInitializationComplete(logger, _instanceId, null);
        }
    }

    /// <summary>
    /// Builds the default orchestrator retry policy used for all activity invocations when none is supplied to the constructor:
    /// 3 attempts, 5-second initial delay, 2x exponential backoff.
    /// </summary>
    public static TaskOptions CreateDefaultTaskOptions()
    {
        return new TaskOptions(TaskRetryOptions.FromRetryPolicy(new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5),
            backoffCoefficient: 2.0)));
    }

    /// <summary>
    /// Pages through aggregate IDs for a single partition, fanning out concurrent process-batch
    /// activity calls (chunked by <c>_batchSize</c>) for each page until an empty or partial page is returned.
    /// Pages are requested with the last-seen aggregate Id as a cursor rather than an offset.
    /// Shared by the tenant and arbitrary-partition orchestrators.
    /// </summary>
    private async Task ProcessPartitionPagesAsync<TPageInfo>(
        TaskOrchestrationContext context,
        string getIdsActivityName,
        string processBatchActivityName,
        Func<Guid?, TPageInfo> pageInfoFactory,
        Action<int> onPageProcessed)
    {
        Guid? lastSeenId = null;
        while (true)
        {
            var pageIds = await context.CallActivityAsync<List<Guid>>(getIdsActivityName, pageInfoFactory(lastSeenId), _durableTaskOptions);
            if (pageIds.Count == 0)
            {
                // no more ids to process
                break;
            }

            // ids are returned ordered by id, so the last one is the cursor for the next page
            lastSeenId = pageIds[^1];

            // run `concurrentBatchCount` batches concurrently
            // this splits the pageIds into `concurrentBatchCount` batches with at most `_batchSize` ids
            var tasks = pageIds
                .Chunk(_batchSize)
                .Select(batch => context.CallActivityAsync(processBatchActivityName, batch.ToList(), _durableTaskOptions))
                .ToList();

            await Task.WhenAll(tasks);
            onPageProcessed(pageIds.Count);

            if (pageIds.Count < _pageSize)
            {
                // at the last page
                break;
            }
        }
    }

    private async Task ProcessRollingPartitionPagesAsync<TPageInfo>(
        TaskOrchestrationContext context,
        string getIdsActivityName,
        string processBatchActivityName,
        Func<Guid?, TPageInfo> pageInfoFactory,
        Func<Guid, DurableRollingProjectionWorkItem> workItemFactory,
        DurableRollingProjectionOptions options,
        Action<int> onPageProcessed)
    {
        Guid? lastSeenId = null;
        while (true)
        {
            List<Guid> ids = await context.CallActivityAsync<List<Guid>>(
                getIdsActivityName,
                pageInfoFactory(lastSeenId),
                _durableTaskOptions);
            if (ids.Count == 0)
            {
                break;
            }

            // Cursor-based paging: resume strictly after the last Id seen so live inserts/deletes cannot skip or duplicate work.
            lastSeenId = ids[^1];
            var tasks = ids
                .Chunk(_batchSize)
                .Select(chunk => context.CallActivityAsync(
                    processBatchActivityName,
                    new DurableRollingProjectionBatch(chunk.Select(workItemFactory).ToArray(), options),
                    _durableTaskOptions));
            await Task.WhenAll(tasks);
            onPageProcessed(ids.Count);

            if (ids.Count < _pageSize)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Deletes all projections of <typeparamref name="TProjection"/> from the bulk container.
    /// </summary>
    /// <param name="client">
    /// Optional durable task client, used to check for cancellation.
    /// </param>
    public async Task DeleteAllProjections(DurableTaskClient? client = null)
    {
        if (client != null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        var container = await _nostify.GetBulkProjectionContainerAsync<TProjection>();
        List<TProjection> allProjections = await container.WithRetry(_cosmosRetryOptions)
            .GetItemLinqQueryable<TProjection>()
            .ReadAllAsync();
        await container.BulkDeleteAsync(allProjections, _cosmosRetryOptions);
    }

    /// <summary>
    /// Gets distinct tenant Ids from the <typeparamref name="TAggregate"/> current state container.
    /// </summary>
    public async Task<List<Guid>> GetDistinctTenantIds()
    {
        var container = await _nostify.GetCurrentStateContainerAsync<TAggregate>();
        return await container.WithRetry(_cosmosRetryOptions)
            .GetItemLinqQueryable<TAggregate>()
            .Select(x => x.tenantId)
            .Distinct()
            .ReadAllAsync();
    }

    /// <summary>
    /// Gets the distinct partition key values from the <typeparamref name="TAggregate"/> current state container,
    /// projected to strings for serialization across activity boundaries.
    /// Use when the aggregate's container is partitioned by a key other than <c>tenantId</c>.
    /// </summary>
    /// <typeparam name="TKey">The CLR type of the partition key property on the aggregate.</typeparam>
    /// <param name="partitionKeySelector">An expression that selects the partition key property from the aggregate (e.g. <c>x =&gt; x.organizationId</c>).</param>
    /// <returns>A list of distinct partition key values converted to strings (null values are converted to empty strings).</returns>
    public async Task<List<string>> GetDistinctPartitionKeys<TKey>(Expression<Func<TAggregate, TKey>> partitionKeySelector)
    {
        var container = await _nostify.GetCurrentStateContainerAsync<TAggregate>();
        var values = await container.WithRetry(_cosmosRetryOptions)
            .GetItemLinqQueryable<TAggregate>()
            .Select(partitionKeySelector)
            .Distinct()
            .ReadAllAsync();
        return values.Select(v => v?.ToString() ?? string.Empty).ToList();
    }

    /// <summary>
    /// Gets a page of aggregate Ids for a tenant, ordered by Id, starting after the request's last-seen Id cursor.
    /// </summary>
    /// <param name="request">Tenant Id and last-seen aggregate Id cursor.</param>
    public Task<List<Guid>> GetIdsForTenant(DurableInitPageInfo request)
        => GetIdsForPartition(request.TenantId.ToPartitionKey(), request.LastSeenId);

    /// <summary>
    /// Gets a page of aggregate Ids for the specified partition, ordered by Id, starting after the request's last-seen Id cursor.
    /// Activity-friendly wrapper that accepts a serializable <see cref="DurablePartitionInitPageInfo"/>.
    /// </summary>
    /// <param name="request">Partition key value (as string) and last-seen aggregate Id cursor.</param>
    public Task<List<Guid>> GetIdsForPartition(DurablePartitionInitPageInfo request)
        => GetIdsForPartition(new PartitionKey(request.PartitionKey), request.LastSeenId);

    /// <summary>
    /// Gets a page of aggregate Ids for the specified partition, ordered by Id.
    /// Uses keyset (cursor) pagination: only Ids strictly greater than <paramref name="lastSeenId"/> are returned,
    /// so aggregates inserted or deleted between pages cannot cause skipped or duplicated Ids.
    /// </summary>
    /// <param name="partitionKey">The Cosmos partition key to query within.</param>
    /// <param name="lastSeenId">The last Id returned by the previous page, or <c>null</c> for the first page.</param>
    public async Task<List<Guid>> GetIdsForPartition(PartitionKey partitionKey, Guid? lastSeenId = null)
    {
        var container = await _nostify.GetCurrentStateContainerAsync<TAggregate>();

        RetryableQuery<TAggregate> query = container.WithRetry(_cosmosRetryOptions)
            .FilteredQuery<TAggregate>(partitionKey)
            .Where(x => !x.isDeleted);

        if (lastSeenId.HasValue)
        {
            // Cosmos stores and orders ids as strings; this translates to `root["id"] > "<lastSeenId>"`,
            // which matches the ORDER BY id comparison used for paging.
            string cursor = lastSeenId.Value.ToString();
            query = query.Where(x => x.id.ToString().CompareTo(cursor) > 0);
        }

        return await query
            .OrderBy(x => x.id)
            .Take(_pageSize)
            .Select(x => x.id)
            .ReadAllAsync();
    }

    /// <summary>
    /// Replays events for a batch of aggregate Ids and initializes their projections.
    /// </summary>
    /// <param name="ids">Aggregate Ids to process.</param>
    /// <param name="client">
    /// Optional durable task client, used to check for cancellation.
    /// </param>
    public async Task ProcessBatch(List<Guid> ids, DurableTaskClient? client = null)
    {
        if (client != null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        // Query persisted event documents through the configured executor. Production uses the
        // Cosmos executor; tests can use the same replay path with in-memory, deserialized documents.
        var eventStore = await _nostify.GetEventStoreContainerAsync();
        var eventsQuery = eventStore
            .GetItemLinqQueryable<Event>()
            .Where(x => ids.Contains(x.aggregateRootId));
        var events = await new RetryableQuery<Event>(
                eventsQuery,
                _cosmosRetryOptions,
                _queryExecutor)
            .ReadAllAsync();

        // Use the event ID as a stable tie-breaker when separate events have equal timestamps.
        var projections = ids.Select(id =>
        {
            var projection = new TProjection();
            foreach (var @event in events
                .Where(item => item.aggregateRootId == id)
                .OrderBy(item => item.timestamp)
                .ThenBy(item => item.id))
            {
                projection.Apply(@event);
            }

            return projection;
        }).ToList();

        // check for cancellation again right before InitAsync - last chance to cancel before processing this batch
        if (client != null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        // initialize projections
        await _nostify.ProjectionInitializer.InitAsync(projections, _nostify, _httpClient, null, _cosmosRetryOptions);
    }

    /// <summary>
    /// Replays and persists a non-destructive rolling projection batch.
    /// </summary>
    /// <param name="batch">Partition-aware work items and immutable rolling options.</param>
    /// <param name="client">Optional Durable client used to observe cancellation.</param>
    public async Task ProcessRollingBatch(
        DurableRollingProjectionBatch batch,
        DurableTaskClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ValidateSelectedProperties(batch.Options);

        if (client != null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        foreach (DurableRollingProjectionWorkItem item in batch.Items)
        {
            if (client != null && await IsCancellationRequestedAsync(client))
            {
                return;
            }

            await ProcessRollingItemAsync(item, batch.Options);
        }
    }

    private async Task ProcessRollingItemAsync(
        DurableRollingProjectionWorkItem item,
        DurableRollingProjectionOptions options)
    {
        Container projectionContainer = await _nostify.GetProjectionContainerAsync<TProjection>(options.PartitionKeyPath);
        PartitionKey partitionKey = item.ToPartitionKey();

        for (int conflictAttempt = 0; ; conflictAttempt++)
        {
            ItemResponse<TProjection>? existing = await ReadProjectionAsync(
                projectionContainer,
                item.Id,
                partitionKey);

            // A missing selective target must be complete, not a sparse selected-property document.
            // Choose the full property set before rebuilding so the event-store query and
            // external-data lookup run only once per attempt.
            IReadOnlyList<string> rebuildProperties = existing == null ? [] : options.SelectedProperties;
            TProjection rebuilt = await RebuildProjectionAsync(item.Id, rebuildProperties);

            try
            {
                if (existing == null)
                {
                    rebuilt.initialized = true;
                    await ExecuteCosmosWithThrottleRetryAsync(() => projectionContainer.CreateItemAsync(
                        rebuilt,
                        partitionKey));
                }
                else if (options.IsSelective)
                {
                    TProjection merged = MergeSelectedProperties(existing.Resource, rebuilt, options.SelectedProperties);
                    await ExecuteCosmosWithThrottleRetryAsync(() => projectionContainer.ReplaceItemAsync(
                        merged,
                        item.Id.ToString(),
                        partitionKey,
                        new ItemRequestOptions { IfMatchEtag = existing.ETag }));
                }
                else
                {
                    rebuilt.initialized = true;
                    await ExecuteCosmosWithThrottleRetryAsync(() => projectionContainer.ReplaceItemAsync(
                        rebuilt,
                        item.Id.ToString(),
                        partitionKey,
                        new ItemRequestOptions { IfMatchEtag = existing.ETag }));
                }

                return;
            }
            catch (CosmosException exception) when (
                exception.StatusCode == HttpStatusCode.PreconditionFailed
                || exception.StatusCode == HttpStatusCode.Conflict)
            {
                if (conflictAttempt < options.MaxEtagRetries)
                {
                    await Task.Delay(options.GetBackoff(conflictAttempt));
                    continue;
                }

                if (_nostify.Logger != null)
                {
                    LogRollingFallback(
                        _nostify.Logger,
                        _instanceId,
                        item.Id,
                        conflictAttempt + 1,
                        exception);
                }

                // The explicitly chosen hot-document policy favors completion and eventual consistency.
                if (options.IsSelective)
                {
                    IReadOnlyList<PatchOperation> operations = CreateSelectedPatchOperations(
                        rebuilt,
                        options.SelectedProperties);
                    await ExecuteCosmosWithThrottleRetryAsync(() => projectionContainer.PatchItemAsync<TProjection>(
                        item.Id.ToString(),
                        partitionKey,
                        operations));
                }
                else
                {
                    rebuilt.initialized = true;
                    await ExecuteCosmosWithThrottleRetryAsync(() => projectionContainer.UpsertItemAsync(
                        rebuilt,
                        partitionKey));
                }

                return;
            }
        }
    }

    private async Task<TProjection> RebuildProjectionAsync(
        Guid id,
        IReadOnlyList<string> selectedProperties)
    {
        Container eventStore = await _nostify.GetEventStoreContainerAsync();
        // Nostify stores an aggregate's event stream in the aggregate-root partition. Scope the
        // replay query accordingly to avoid a cross-partition scan and to support emulator-backed
        // rolling initialization with the same partition contract used by production writes.
        IQueryable<Event> query = eventStore
            .GetItemLinqQueryable<Event>(
                requestOptions: new QueryRequestOptions { PartitionKey = id.ToPartitionKey() })
            .Where(item => item.aggregateRootId == id);
        List<Event> baseEvents;
        try
        {
            baseEvents = await new RetryableQuery<Event>(query, _cosmosRetryOptions, _queryExecutor)
                .ReadAllAsync();
        }
        catch (ArgumentOutOfRangeException exception) when (
            exception.Message.Contains("Unknown JsonNodeType: Unknown", StringComparison.Ordinal))
        {
            // Linux vNext can return event objects that the SDK's query materializer cannot traverse.
            // Query only scalar IDs in the aggregate partition, then use normal point reads to retain
            // real Cosmos semantics without asking the emulator to materialize complex query values.
            baseEvents = await ReadEventsByIdAsync(eventStore, id);
        }
        List<Event> orderedBaseEvents = baseEvents
            .OrderBy(item => item.timestamp)
            .ThenBy(item => item.id)
            .ToList();

        // Always build complete state first because external-data selectors may depend on any base property.
        var shadow = new TProjection();
        foreach (Event @event in orderedBaseEvents)
        {
            shadow.Apply(@event);
        }

        List<Event> externalEvents = (await TProjection.GetExternalDataEventsAsync(
                [shadow],
                _nostify,
                _httpClient,
                null))
            .Where(group => group.aggregateRootId == shadow.id)
            .SelectMany(group => group.events)
            .OrderBy(item => item.timestamp)
            .ThenBy(item => item.id)
            .ToList();

        if (selectedProperties.Count == 0)
        {
            foreach (Event @event in externalEvents)
            {
                shadow.Apply(@event);
            }

            return shadow;
        }

        var selectedProjection = new TProjection();

        // Preserve normal initialization semantics: replay all base events first, followed by
        // external events. Each group is already ordered deterministically above.
        foreach (Event @event in orderedBaseEvents)
        {
            Event? reduced = CreateReducedEvent(@event, selectedProperties);
            if (reduced != null)
            {
                selectedProjection.Apply(reduced);
            }
        }

        foreach (Event @event in externalEvents)
        {
            Event? reduced = CreateReducedEvent(@event, selectedProperties);
            if (reduced != null)
            {
                selectedProjection.Apply(reduced);
            }
        }

        return selectedProjection;
    }

    private async Task<List<Event>> ReadEventsByIdAsync(Container eventStore, Guid aggregateRootId)
    {
        var definition = new QueryDefinition(
            "SELECT VALUE eventItem.id FROM eventItem WHERE eventItem.aggregateRootId = @aggregateRootId")
            .WithParameter("@aggregateRootId", aggregateRootId);
        PartitionKey partitionKey = aggregateRootId.ToPartitionKey();
        var requestOptions = new QueryRequestOptions { PartitionKey = partitionKey };
        using FeedIterator<string> iterator = eventStore.GetItemQueryIterator<string>(
            definition,
            requestOptions: requestOptions);
        var eventIds = new List<string>();

        while (iterator.HasMoreResults)
        {
            FeedResponse<string> page = await ExecuteCosmosWithThrottleRetryAsync(
                () => iterator.ReadNextAsync());
            eventIds.AddRange(page);
        }

        var events = new List<Event>(eventIds.Count);
        foreach (string eventId in eventIds)
        {
            ItemResponse<Event> response = await ExecuteCosmosWithThrottleRetryAsync(
                () => eventStore.ReadItemAsync<Event>(eventId, partitionKey));
            events.Add(response.Resource);
        }

        // Point reads follow scalar query order, so explicitly restore the replay contract here.
        return events
            .OrderBy(item => item.timestamp)
            .ThenBy(item => item.id)
            .ToList();
    }

    private static Event? CreateReducedEvent(Event source, IReadOnlyList<string> selectedProperties)
    {
        if (source.payload == null)
        {
            return null;
        }

        JObject sourcePayload = JObject.FromObject(source.payload);
        var reducedPayload = new JObject();
        JToken? idToken = sourcePayload[nameof(NostifyObject.id)];
        if (idToken != null)
        {
            reducedPayload[nameof(NostifyObject.id)] = idToken.DeepClone();
        }
        else
        {
            reducedPayload[nameof(NostifyObject.id)] = source.aggregateRootId;
        }

        bool containsSelectedProperty = false;
        foreach (string property in selectedProperties)
        {
            JToken? value = sourcePayload[property];
            if (value != null)
            {
                reducedPayload[property] = value.DeepClone();
                containsSelectedProperty = true;
            }
        }

        if (!containsSelectedProperty)
        {
            return null;
        }

        return new Event
        {
            id = source.id,
            aggregateRootId = source.aggregateRootId,
            partitionKey = source.partitionKey,
            userId = source.userId,
            timestamp = source.timestamp,
            eventType = source.eventType,
            schemaVersion = source.schemaVersion,
            payload = reducedPayload
        };
    }

    private static TProjection MergeSelectedProperties(
        TProjection existing,
        TProjection rebuilt,
        IReadOnlyList<string> selectedProperties)
    {
        foreach (string propertyName in selectedProperties)
        {
            PropertyInfo property = typeof(TProjection).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!;
            property.SetValue(existing, property.GetValue(rebuilt));
        }

        return existing;
    }

    private static PatchOperation[] CreateSelectedPatchOperations(
        TProjection rebuilt,
        IReadOnlyList<string> selectedProperties)
        => selectedProperties
            .Select(propertyName =>
            {
                PropertyInfo property = typeof(TProjection).GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance)!;
                string escapedPath = propertyName.Replace("~", "~0", StringComparison.Ordinal)
                    .Replace("/", "~1", StringComparison.Ordinal);
                return PatchOperation.Set($"/{escapedPath}", property.GetValue(rebuilt));
            })
            .ToArray();

    private static void ValidateSelectedProperties(DurableRollingProjectionOptions options)
    {
        string partitionProperty = options.PartitionKeyPath.TrimStart('/');
        var protectedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(NostifyObject.id),
            nameof(NostifyObject.tenantId),
            nameof(NostifyObject.ttl),
            nameof(IProjection.initialized),
            partitionProperty,
            "_etag",
            "_rid",
            "_self",
            "_attachments",
            "_ts"
        };

        foreach (string propertyName in options.SelectedProperties)
        {
            PropertyInfo? property = typeof(TProjection).GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.SetMethod == null)
            {
                throw new ArgumentException(
                    $"Selected property '{propertyName}' is not a writable public property on {typeof(TProjection).Name}.",
                    nameof(options));
            }

            if (protectedProperties.Contains(propertyName))
            {
                throw new ArgumentException(
                    $"Selected property '{propertyName}' is managed by Nostify or Cosmos and cannot be selectively rebuilt.",
                    nameof(options));
            }
        }
    }

    private async Task<ItemResponse<TProjection>?> ReadProjectionAsync(
        Container container,
        Guid id,
        PartitionKey partitionKey)
    {
        try
        {
            return await ExecuteCosmosWithThrottleRetryAsync(() => container.ReadItemAsync<TProjection>(
                id.ToString(),
                partitionKey));
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T> ExecuteCosmosWithThrottleRetryAsync<T>(Func<Task<T>> operation)
    {
        int throttleAttempt = 0;
        while (true)
        {
            try
            {
                return await operation();
            }
            catch (CosmosException exception) when (
                exception.StatusCode == HttpStatusCode.TooManyRequests
                && throttleAttempt < _cosmosRetryOptions.MaxRetries)
            {
                TimeSpan delay = exception.RetryAfter is TimeSpan retryAfter && retryAfter > TimeSpan.Zero
                    ? retryAfter
                    : _cosmosRetryOptions.GetDelayForAttempt(throttleAttempt);
                throttleAttempt++;
                await Task.Delay(delay);
            }
        }
    }
}

/// <summary>
/// Names of activities used by tenant-partitioned projection initialization.
/// Grouping the names prevents positional string mistakes while retaining the existing activity payloads.
/// </summary>
public sealed class DurableTenantInitActivityNames
{
    /// <summary>Creates the activity-name group.</summary>
    public DurableTenantInitActivityNames(
        string delete,
        string getTenantIds,
        string getIds,
        string processBatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(delete);
        ArgumentException.ThrowIfNullOrWhiteSpace(getTenantIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(getIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(processBatch);

        Delete = delete;
        GetTenantIds = getTenantIds;
        GetIds = getIds;
        ProcessBatch = processBatch;
    }

    /// <summary>Gets the projection deletion activity name.</summary>
    public string Delete { get; }

    /// <summary>Gets the distinct-tenant retrieval activity name.</summary>
    public string GetTenantIds { get; }

    /// <summary>Gets the tenant-specific aggregate-ID retrieval activity name.</summary>
    public string GetIds { get; }

    /// <summary>Gets the aggregate-ID process-batch activity name.</summary>
    public string ProcessBatch { get; }
}

/// <summary>
/// Paging request for durable projection initialization: tenant Id and last-seen aggregate Id cursor.
/// </summary>
public struct DurableInitPageInfo
{
    /// <summary>Gets the tenant identifier whose projections are being initialized.</summary>
    public readonly Guid TenantId;

    /// <summary>
    /// Gets the last aggregate Id returned by the previous page, or <c>null</c> for the first page.
    /// The next page starts strictly after this Id, so concurrent inserts and deletes cannot shift page boundaries.
    /// </summary>
    public readonly Guid? LastSeenId;

    /// <summary>
    /// Initializes a durable projection page request.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="lastSeenId">The last aggregate Id from the previous page, or <c>null</c> for the first page.</param>
    public DurableInitPageInfo(Guid tenantId, Guid? lastSeenId = null)
    {
        TenantId = tenantId;
        LastSeenId = lastSeenId;
    }
}

/// <summary>
/// Paging request for durable projection initialization by an arbitrary partition key:
/// the partition key value (as string for activity-boundary serialization) and last-seen aggregate Id cursor.
/// </summary>
public struct DurablePartitionInitPageInfo
{
    /// <summary>The partition key value as a string. Reconstructed via <c>new PartitionKey(value)</c> on the receiving side.</summary>
    public readonly string PartitionKey;

    /// <summary>
    /// The last aggregate Id returned by the previous page, or <c>null</c> for the first page.
    /// The next page starts strictly after this Id, so concurrent inserts and deletes cannot shift page boundaries.
    /// </summary>
    public readonly Guid? LastSeenId;

    /// <summary>
    /// Creates a new <see cref="DurablePartitionInitPageInfo"/> from a string partition key value.
    /// </summary>
    /// <param name="partitionKey">The partition key value as a string.</param>
    /// <param name="lastSeenId">The last aggregate Id from the previous page, or <c>null</c> for the first page.</param>
    public DurablePartitionInitPageInfo(string partitionKey, Guid? lastSeenId = null)
    {
        PartitionKey = partitionKey;
        LastSeenId = lastSeenId;
    }
}
