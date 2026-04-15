
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableProjectionInitializer{TProjection, TAggregate}"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to make external data requests.</param>
    /// <param name="nostify">The Nostify instance.</param>
    /// <param name="instanceId">The instance Id; only one orchestration can run at a time with this Id.</param>
    /// <param name="batchSize">The number of projections to initialize in a batch.</param>
    /// <param name="concurrentBatchCount">The number of concurrent batches to process.</param>
    public DurableProjectionInitializer(HttpClient httpClient, INostify nostify, string instanceId, int batchSize = 1000, int concurrentBatchCount = 5)
    {
        _httpClient = httpClient;
        _nostify = nostify;
        _instanceId = instanceId ?? $"{nameof(TProjection)}_Init";

        _batchSize = batchSize;
        _pageSize = batchSize * concurrentBatchCount;
    }

    /// <summary>
    /// Starts the durable orchestration to initialize projections. If an orchestration with the same instance Id is already running, returns a 409 Conflict response.
    /// </summary>
    /// <param name="req">The HTTP request data from the Azure Function.</param>
    /// <param name="client">The durable task client injected by the Azure host.</param>
    /// <param name="orchestratorName">The name of the orchestrator.</param>
    /// <returns>The HTTP response with 409 if running, otherwise 202 response with a Location header and a payload containing instance control URLs.</returns>
    public async Task<HttpResponseData> StartOrchestration(
        HttpRequestData req,
        DurableTaskClient client,
        string orchestratorName)
    {
        var existing = await client.GetInstanceAsync(_instanceId);

        // only allow one orchestration to run at a time
        if (existing != null && existing.RuntimeStatus == OrchestrationRuntimeStatus.Running)
        {
            // already running, return 409 Conflict
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            conflict.WriteString($"{_instanceId} is already running");
            return conflict;
        }

        await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, new StartOrchestrationOptions(_instanceId));
        return await client.CreateCheckStatusResponseAsync(req, _instanceId);
    }

    /// <summary>
    /// Cancels the durable orchestration if it is running, and purges the instance after cancellation.
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
            if (existing.RuntimeStatus == OrchestrationRuntimeStatus.Running)
            {
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
    /// The orchestrator that runs the projection initialization logic.
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
        await context.CallActivityAsync(deleteActivityName);
        logger?.LogInformation($"{_instanceId}: delete all projections");

        var retryOptions = new TaskOptions(TaskRetryOptions.FromRetryPolicy(new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5),
            backoffCoefficient: 2.0)));

        // get tenant ids to query by tenant partition
        List<Guid> tenantIds = await context.CallActivityAsync<List<Guid>>(getTenantIdsActivityName);
        logger?.LogInformation($"{_instanceId}: processing {tenantIds.Count} tenants");

        int totalProcessed = 0;

        foreach (var tenantId in tenantIds)
        {
            // process each tenant separately
            List<Guid> pageIds = [];
            int pageNum = 0;
            while (true)
            {
                pageIds = await context.CallActivityAsync<List<Guid>>(getIdsActivityName, new DurableInitPageInfo(tenantId, pageNum));
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
                    .Select(batch => context.CallActivityAsync(processBatchActivityName, batch.ToList(), retryOptions));

                await Task.WhenAll(tasks);
                totalProcessed += pageIds.Count;
                logger?.LogInformation($"{_instanceId}: {totalProcessed} projections processed");

                if (pageIds.Count < _pageSize)
                {
                    // at the last page
                    break;
                }
            }
        }

        logger?.LogInformation($"{_instanceId}: projection initialization complete");
    }

    /// <summary>
    /// Deletes all projections of <typeparamref name="TProjection"/> from the bulk container.
    /// </summary>
    public async Task DeleteAllProjections()
    {
        var container = await _nostify.GetBulkProjectionContainerAsync<TProjection>();
        await container.DeleteAllBulkAsync<TProjection>();
    }

    /// <summary>
    /// Gets distinct tenant Ids from the <typeparamref name="TAggregate"/> current state container.
    /// </summary>
    public async Task<List<Guid>> GetDistinctTenantIds()
    {
        var container = await _nostify.GetCurrentStateContainerAsync<TAggregate>();
        return await container.GetItemLinqQueryable<TAggregate>()
            .Select(x => x.tenantId)
            .Distinct()
            .ReadAllAsync();
    }

    /// <summary>
    /// Gets a page of aggregate Ids for a tenant, ordered by Id for stable paging.
    /// </summary>
    /// <param name="request">Tenant Id and page number.</param>
    public async Task<List<Guid>> GetIdsForTenant(DurableInitPageInfo request)
    {
        var container = await _nostify.GetCurrentStateContainerAsync<TAggregate>();
        
        return await container.FilteredQuery<TAggregate>(request.TenantId)
            .OrderBy(x => x.id)
            .Skip(request.PageNumber * _pageSize)
            .Take(_pageSize)
            .Select(x => x.id)
            .ReadAllAsync();
    }

    /// <summary>
    /// Replays events for a batch of aggregate Ids and initializes their projections.
    /// </summary>
    /// <param name="ids">Aggregate Ids to process.</param>
    public async Task ProcessBatch(List<Guid> ids)
    {
        // get events from event store
        var eventStore = await _nostify.GetEventStoreContainerAsync();
        var events = await eventStore.GetItemLinqQueryable<Event>()
            .Where(x => ids.Contains(x.aggregateRootId))
            .ReadAllAsync();

        // get projections from events
        var projections = ids.Select(id =>
        {
            var p = new TProjection();
            events.Where(e => e.aggregateRootId == id).ToList().ForEach(e => p.Apply(e));
            return p;
        }).ToList();

        // initialize projections
        await _nostify.ProjectionInitializer.InitAsync(projections, _nostify, _httpClient);
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
