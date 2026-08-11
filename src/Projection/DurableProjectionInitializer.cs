
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

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

    private readonly int _batchSize;
    private readonly int _pageSize;

    private readonly TaskOptions _durableTaskOptions;
    private readonly RetryOptions _cosmosRetryOptions;

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
    {
        _httpClient = httpClient;
        _nostify = nostify;
        _instanceId = instanceId ?? $"{typeof(TProjection).FullName ?? typeof(TProjection).Name}_Init";

        _batchSize = batchSize;
        _pageSize = batchSize * concurrentBatchCount;

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
                // Resume a suspended orchestration so that it can be terminated.
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
    public async Task OrchestrateInitAsync(
        TaskOrchestrationContext context,
        string deleteActivityName,
        string getTenantIdsActivityName,
        string getIdsActivityName,
        string processBatchActivityName,
        ILogger? logger = null
        )
    {
        // delete Projections
        await context.CallActivityAsync(deleteActivityName, null, _durableTaskOptions);
        logger?.LogInformation($"{_instanceId}: delete all projections");

        // get tenant ids to query by tenant partition
        List<Guid> tenantIds = await context.CallActivityAsync<List<Guid>>(getTenantIdsActivityName, null, _durableTaskOptions);
        logger?.LogInformation($"{_instanceId}: processing {tenantIds.Count} tenants");

        int totalProcessed = 0;

        foreach (var tenantId in tenantIds)
        {
            await ProcessPartitionPagesAsync(
                context,
                getIdsActivityName,
                processBatchActivityName,
                pageNum => new DurableInitPageInfo(tenantId, pageNum),
                count =>
                {
                    totalProcessed += count;
                    logger?.LogInformation($"{_instanceId}: {totalProcessed} projections processed");
                });
        }

        logger?.LogInformation($"{_instanceId}: projection initialization complete");
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
        logger?.LogInformation($"{_instanceId}: delete all projections");

        // get partition key values to page through
        List<string> partitionKeys = await context.CallActivityAsync<List<string>>(getPartitionKeysActivityName, null, _durableTaskOptions);
        logger?.LogInformation($"{_instanceId}: processing {partitionKeys.Count} partitions");

        int totalProcessed = 0;

        foreach (var pk in partitionKeys)
        {
            await ProcessPartitionPagesAsync(
                context,
                getIdsActivityName,
                processBatchActivityName,
                pageNum => new DurablePartitionInitPageInfo(pk, pageNum),
                count =>
                {
                    totalProcessed += count;
                    logger?.LogInformation($"{_instanceId}: {totalProcessed} projections processed");
                });
        }

        logger?.LogInformation($"{_instanceId}: projection initialization complete");
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
    /// Shared by both <see cref="OrchestrateInitAsync"/> and <see cref="OrchestrateInitByPartitionAsync"/>.
    /// </summary>
    private async Task ProcessPartitionPagesAsync<TPageInfo>(
        TaskOrchestrationContext context,
        string getIdsActivityName,
        string processBatchActivityName,
        Func<int, TPageInfo> pageInfoFactory,
        Action<int> onPageProcessed)
    {
        int pageNum = 0;
        while (true)
        {
            var pageIds = await context.CallActivityAsync<List<Guid>>(getIdsActivityName, pageInfoFactory(pageNum), _durableTaskOptions);
            if (pageIds.Count == 0)
            {
                // no more ids to process
                break;
            }

            pageNum++;

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
    /// Gets a page of aggregate Ids for a tenant, ordered by Id for stable paging.
    /// </summary>
    /// <param name="request">Tenant Id and page number.</param>
    public Task<List<Guid>> GetIdsForTenant(DurableInitPageInfo request)
        => GetIdsForPartition(request.TenantId.ToPartitionKey(), request.PageNumber);

    /// <summary>
    /// Gets a page of aggregate Ids for the specified partition, ordered by Id for stable paging.
    /// Activity-friendly wrapper that accepts a serializable <see cref="DurablePartitionInitPageInfo"/>.
    /// </summary>
    /// <param name="request">Partition key value (as string) and page number.</param>
    public Task<List<Guid>> GetIdsForPartition(DurablePartitionInitPageInfo request)
        => GetIdsForPartition(new PartitionKey(request.PartitionKey), request.PageNumber);

    /// <summary>
    /// Gets a page of aggregate Ids for the specified partition, ordered by Id for stable paging.
    /// </summary>
    /// <param name="partitionKey">The Cosmos partition key to query within.</param>
    /// <param name="pageNumber">Zero-based page number.</param>
    public async Task<List<Guid>> GetIdsForPartition(PartitionKey partitionKey, int pageNumber)
    {
        var container = await _nostify.GetCurrentStateContainerAsync<TAggregate>();

        return await container.WithRetry(_cosmosRetryOptions)
            .FilteredQuery<TAggregate>(partitionKey)
            .Where(x => !x.isDeleted)
            .OrderBy(x => x.id)
            .Skip(pageNumber * _pageSize)
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

        // get events from event store
        var eventStore = await _nostify.GetEventStoreContainerAsync();
        var events = await eventStore.WithRetry(_cosmosRetryOptions)
            .GetItemLinqQueryable<Event>()
            .Where(x => ids.Contains(x.aggregateRootId))
            .ReadAllAsync();

        // get projections from events
        var projections = ids.Select(id =>
        {
            var p = new TProjection();
            events.Where(e => e.aggregateRootId == id).ToList().ForEach(e => p.Apply(e));
            return p;
        }).ToList();

        // check for cancellation again right before InitAsync - last chance to cancel before processing this batch
        if (client != null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        // initialize projections
        await _nostify.ProjectionInitializer.InitAsync(projections, _nostify, _httpClient, null, _cosmosRetryOptions);
    }
}

/// <summary>
/// Paging request for durable projection initialization: tenant Id and page number.
/// </summary>
public struct DurableInitPageInfo{
    public readonly Guid TenantId;
    public readonly int PageNumber;

    public DurableInitPageInfo(Guid tenantId, int pageNumber)
    {
        TenantId = tenantId;
        PageNumber = pageNumber;
    }
}

/// <summary>
/// Paging request for durable projection initialization by an arbitrary partition key:
/// the partition key value (as string for activity-boundary serialization) and page number.
/// </summary>
public struct DurablePartitionInitPageInfo
{
    /// <summary>The partition key value as a string. Reconstructed via <c>new PartitionKey(value)</c> on the receiving side.</summary>
    public readonly string PartitionKey;

    /// <summary>Zero-based page number.</summary>
    public readonly int PageNumber;

    /// <summary>
    /// Creates a new <see cref="DurablePartitionInitPageInfo"/> from a string partition key value.
    /// </summary>
    public DurablePartitionInitPageInfo(string partitionKey, int pageNumber)
    {
        PartitionKey = partitionKey;
        PageNumber = pageNumber;
    }
}
