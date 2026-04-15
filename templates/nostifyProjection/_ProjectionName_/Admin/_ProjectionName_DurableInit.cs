
using _ReplaceMe__Service;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using nostify;

namespace _ReplaceMe__Service;

public class _ProjectionName_DurableInit
{
    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    private readonly ILogger<_ProjectionName_DurableInit> _logger;

    private readonly DurableProjectionInitializer<_ProjectionName_, _ReplaceMe_> _initializer;

    public _ProjectionName_DurableInit(HttpClient httpClient, INostify nostify, ILogger<_ProjectionName_DurableInit> logger)
    {
        _httpClient = httpClient;
        _nostify = nostify;
        _logger = logger;

        _initializer = new DurableProjectionInitializer<_ProjectionName_, _ReplaceMe_>(
            _httpClient,
            _nostify,
            nameof(_ProjectionName_DurableInit),
            batchSize: 1000,
            concurrentBatchCount: 5);
    }

    [Function(nameof(_ProjectionName_DurableInit))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger("post", Route = "_ProjectionName_DurableInit")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        return await _initializer.StartOrchestration(req, client, nameof(Orchestrate_ProjectionName_DurableInit));
    }

    [Function(nameof(Cancel_ProjectionName_DurableInit))]
    public async Task<HttpResponseData> Cancel_ProjectionName_DurableInit(
        [HttpTrigger("delete", Route = "_ProjectionName_DurableInit")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        return await _initializer.CancelOrchestration(req, client);
    }

    [Function(nameof(Orchestrate_ProjectionName_DurableInit))]
    public async Task Orchestrate_ProjectionName_DurableInit(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        await _initializer.OrchestrateInitAsync(
            context,
            nameof(DeleteAll_ProjectionName_),
            nameof(GetDistinctTenantIds__ProjectionName_),
            nameof(Get_ReplaceMe_IdsForTenant__ProjectionName_),
            nameof(Process_ProjectionName_Batch),
            context.CreateReplaySafeLogger<_ProjectionName_DurableInit>());
    }

    [Function(nameof(DeleteAll_ProjectionName_))]
    public async Task DeleteAll_ProjectionName_(
        [ActivityTrigger] TaskActivityContext context)
    {
        await _initializer.DeleteAllProjections();
    }

    [Function(nameof(GetDistinctTenantIds__ProjectionName_))]
    public async Task<List<Guid>> GetDistinctTenantIds__ProjectionName_(
        [ActivityTrigger] TaskActivityContext context)
    {
        return await _initializer.GetDistinctTenantIds();
    }

    [Function(nameof(Get_ReplaceMe_IdsForTenant__ProjectionName_))]
    public async Task<List<Guid>> Get_ReplaceMe_IdsForTenant__ProjectionName_(
        [ActivityTrigger] DurableInitPageInfo request)
    {
        return await _initializer.GetIdsForTenant(request);
    }

    [Function(nameof(Process_ProjectionName_Batch))]
    public async Task Process_ProjectionName_Batch(
        [ActivityTrigger] List<Guid> ids)
    {
        await _initializer.ProcessBatch(ids);
    }
}
