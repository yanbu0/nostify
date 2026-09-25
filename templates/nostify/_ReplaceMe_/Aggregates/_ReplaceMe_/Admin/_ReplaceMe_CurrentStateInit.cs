using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using nostify;

namespace _ReplaceMe__Service;

/// <summary>Durably rebuilds the aggregate current-state container from its event stream.</summary>
public class _ReplaceMe_CurrentStateInit
{
    private readonly DurableCurrentStateInitializer<_ReplaceMe_> _initializer;

    public _ReplaceMe_CurrentStateInit(INostify nostify)
    {
        _initializer = new DurableCurrentStateInitializer<_ReplaceMe_>(
            nostify,
            nameof(_ReplaceMe_CurrentStateInit),
            batchSize: 1000,
            concurrentBatchCount: 5);
    }

    /// <summary>Starts the rebuild unless this initializer is already running.</summary>
    [Function(nameof(_ReplaceMe_CurrentStateInit))]
    public Task<HttpResponseData> Run(
        [HttpTrigger("post", Route = "_ReplaceMe_CurrentStateInit")] HttpRequestData request,
        [DurableClient] DurableTaskClient client)
        => _initializer.StartOrchestration(request, client, nameof(Orchestrate_ReplaceMe_CurrentStateInit));

    /// <summary>Cancels and purges the active rebuild, when one exists.</summary>
    [Function(nameof(Cancel_ReplaceMe_CurrentStateInit))]
    public Task<HttpResponseData> Cancel_ReplaceMe_CurrentStateInit(
        [HttpTrigger("delete", Route = "_ReplaceMe_CurrentStateInit")] HttpRequestData request,
        [DurableClient] DurableTaskClient client)
        => _initializer.CancelOrchestration(request, client);

    /// <summary>Coordinates deletion, event-store paging, and aggregate rehydration.</summary>
    [Function(nameof(Orchestrate_ReplaceMe_CurrentStateInit))]
    public Task Orchestrate_ReplaceMe_CurrentStateInit(
        [OrchestrationTrigger] TaskOrchestrationContext context)
        => _initializer.OrchestrateInitAsync(
            context,
            nameof(DeleteAll_ReplaceMe_CurrentState),
            nameof(Get_ReplaceMe_AggregateIds),
            nameof(Process_ReplaceMe_CurrentStateBatch),
            context.CreateReplaySafeLogger<_ReplaceMe_CurrentStateInit>());

    /// <summary>Deletes existing aggregate current-state documents.</summary>
    [Function(nameof(DeleteAll_ReplaceMe_CurrentState))]
    public Task DeleteAll_ReplaceMe_CurrentState(
        [ActivityTrigger] TaskActivityContext context,
        [DurableClient] DurableTaskClient client)
        => _initializer.DeleteAllCurrentState(client);

    /// <summary>Gets one stable page of aggregate identifiers from the event store.</summary>
    [Function(nameof(Get_ReplaceMe_AggregateIds))]
    public Task<List<Guid>> Get_ReplaceMe_AggregateIds(
        [ActivityTrigger] DurableCurrentStatePageInfo request)
        => _initializer.GetAggregateIds(request);

    /// <summary>Rehydrates and persists one aggregate batch.</summary>
    [Function(nameof(Process_ReplaceMe_CurrentStateBatch))]
    public Task Process_ReplaceMe_CurrentStateBatch(
        [ActivityTrigger] List<Guid> ids,
        [DurableClient] DurableTaskClient client)
        => _initializer.ProcessBatch(ids, client);
}
