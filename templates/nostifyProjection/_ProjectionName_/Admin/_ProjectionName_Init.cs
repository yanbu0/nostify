using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using nostify;

namespace _ReplaceMe__Service;

/// <summary>
/// Rebuilds the projection through a durable, paged orchestration so initialization can
/// process large data sets without exceeding a single Azure Function execution timeout.
/// </summary>
public class _ProjectionName_Init
{
    private readonly DurableProjectionInitializer<_ProjectionName_, _ReplaceMe_> _initializer;

    public _ProjectionName_Init(HttpClient httpClient, INostify nostify)
    {
        _initializer = new DurableProjectionInitializer<_ProjectionName_, _ReplaceMe_>(
            httpClient,
            nostify,
            nameof(_ProjectionName_Init),
            batchSize: 1000,
            concurrentBatchCount: 5);
    }

    /// <summary>Starts the projection rebuild unless this initializer is already running.</summary>
    [Function(nameof(_ProjectionName_Init))]
    public Task<HttpResponseData> Run(
        [HttpTrigger("post", Route = "_ProjectionName_Init")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
        => _initializer.StartOrchestration(req, client, nameof(Orchestrate_ProjectionName_Init));

    /// <summary>
    /// Starts a rolling rebuild without deleting existing projection documents.
    /// The JSON body supplies the required <see cref="DurableRollingProjectionInput"/>.
    /// </summary>
    [Function(nameof(Rolling_ProjectionName_Init))]
    public Task<HttpResponseData> Rolling_ProjectionName_Init(
        [HttpTrigger("post", Route = "_ProjectionName_Init/rolling")] HttpRequestData req,
        [FromBody] DurableRollingProjectionInput input,
        [DurableClient] DurableTaskClient client)
        => _initializer.StartRollingOrchestration(
            req,
            client,
            nameof(OrchestrateRolling_ProjectionName_Init),
            input);

    /// <summary>Cancels and purges the active projection rebuild, when one exists.</summary>
    [Function(nameof(Cancel_ProjectionName_Init))]
    public Task<HttpResponseData> Cancel_ProjectionName_Init(
        [HttpTrigger("delete", Route = "_ProjectionName_Init")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
        => _initializer.CancelOrchestration(req, client);

    /// <summary>Coordinates projection deletion and tenant-partitioned batch processing.</summary>
    [Function(nameof(Orchestrate_ProjectionName_Init))]
    public Task Orchestrate_ProjectionName_Init(
        [OrchestrationTrigger] TaskOrchestrationContext context)
        => _initializer.OrchestrateInitAsync(
            context,
            new DurableTenantInitActivityNames(
                nameof(DeleteAll_ProjectionName_),
                nameof(GetDistinctTenantIds__ProjectionName_),
                nameof(Get_ReplaceMe_IdsForTenant__ProjectionName_),
                nameof(Process_ProjectionName_Batch)),
            context.CreateReplaySafeLogger<_ProjectionName_Init>());

    /// <summary>Coordinates a non-destructive tenant-partitioned rolling rebuild.</summary>
    [Function(nameof(OrchestrateRolling_ProjectionName_Init))]
    public Task OrchestrateRolling_ProjectionName_Init(
        [OrchestrationTrigger] TaskOrchestrationContext context)
        => _initializer.OrchestrateRollingInitAsync(
            context,
            new DurableRollingTenantInitActivityNames(
                nameof(GetDistinctTenantIds__ProjectionName_),
                nameof(Get_ReplaceMe_IdsForTenant__ProjectionName_),
                nameof(ProcessRolling_ProjectionName_Batch)),
            context.GetInput<DurableRollingProjectionInput>()
                ?? new DurableRollingProjectionInput(),
            context.CreateReplaySafeLogger<_ProjectionName_Init>());

    /// <summary>Deletes existing projection documents before rebuilding them.</summary>
    [Function(nameof(DeleteAll_ProjectionName_))]
    public Task DeleteAll_ProjectionName_(
        [ActivityTrigger] TaskActivityContext context,
        [DurableClient] DurableTaskClient client)
        => _initializer.DeleteAllProjections(client);

    /// <summary>Gets the aggregate tenant partitions that must be rebuilt.</summary>
    [Function(nameof(GetDistinctTenantIds__ProjectionName_))]
    public Task<List<Guid>> GetDistinctTenantIds__ProjectionName_(
        [ActivityTrigger] TaskActivityContext context)
        => _initializer.GetDistinctTenantIds();

    /// <summary>Gets one stable page of aggregate identifiers for a tenant.</summary>
    [Function(nameof(Get_ReplaceMe_IdsForTenant__ProjectionName_))]
    public Task<List<Guid>> Get_ReplaceMe_IdsForTenant__ProjectionName_(
        [ActivityTrigger] DurableInitPageInfo request)
        => _initializer.GetIdsForTenant(request);

    /// <summary>Replays and initializes one projection batch.</summary>
    [Function(nameof(Process_ProjectionName_Batch))]
    public Task Process_ProjectionName_Batch(
        [ActivityTrigger] List<Guid> ids,
        [DurableClient] DurableTaskClient client)
        => _initializer.ProcessBatch(ids, client);

    /// <summary>Replays and conditionally persists one rolling projection batch.</summary>
    [Function(nameof(ProcessRolling_ProjectionName_Batch))]
    public Task ProcessRolling_ProjectionName_Batch(
        [ActivityTrigger] DurableRollingProjectionBatch batch,
        [DurableClient] DurableTaskClient client)
        => _initializer.ProcessRollingBatch(batch, client);
}
