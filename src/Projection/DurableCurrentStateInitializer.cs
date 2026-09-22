using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace nostify;

/// <summary>
/// Coordinates a durable, paged rebuild of an aggregate current-state container from
/// its event stream. The fixed orchestration instance ID prevents concurrent rebuilds.
/// </summary>
/// <typeparam name="TAggregate">The aggregate type to rehydrate and persist.</typeparam>
public class DurableCurrentStateInitializer<TAggregate>
    where TAggregate : NostifyObject, IAggregate, new()
{
    private readonly INostify _nostify;
    private readonly string _instanceId;
    private readonly string _partitionKeyPath;
    private readonly int _batchSize;
    private readonly int _pageSize;
    private readonly TaskOptions _durableTaskOptions;
    private readonly RetryOptions _cosmosRetryOptions;

    /// <summary>Creates a durable aggregate current-state initializer.</summary>
    /// <param name="nostify">The Nostify instance used to access Cosmos containers.</param>
    /// <param name="instanceId">The fixed orchestration instance ID.</param>
    /// <param name="partitionKeyPath">The current-state container partition key path.</param>
    /// <param name="batchSize">The maximum aggregate count processed by one activity.</param>
    /// <param name="concurrentBatchCount">The maximum batches fanned out for one page.</param>
    /// <param name="durableTaskOptions">Optional Durable Functions activity retry options.</param>
    /// <param name="cosmosRetryOptions">Optional Cosmos operation retry options.</param>
    public DurableCurrentStateInitializer(
        INostify nostify,
        string instanceId,
        string partitionKeyPath = "/tenantId",
        int batchSize = 1000,
        int concurrentBatchCount = 5,
        TaskOptions? durableTaskOptions = null,
        RetryOptions? cosmosRetryOptions = null)
    {
        ArgumentNullException.ThrowIfNull(nostify);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKeyPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrentBatchCount);

        _nostify = nostify;
        _instanceId = instanceId;
        _partitionKeyPath = partitionKeyPath;
        _batchSize = batchSize;
        _pageSize = checked(batchSize * concurrentBatchCount);
        _durableTaskOptions = durableTaskOptions ?? DurableProjectionInitializer<TAggregateProjectionAdapter, TAggregate>.CreateDefaultTaskOptions();
        _cosmosRetryOptions = cosmosRetryOptions ?? new RetryOptions();
    }

    /// <summary>Starts a rebuild, returning conflict when the fixed instance is active.</summary>
    public async Task<HttpResponseData> StartOrchestration(
        HttpRequestData request,
        DurableTaskClient client,
        string orchestratorName)
    {
        var existing = await client.GetInstanceAsync(_instanceId);
        if (IsInstanceActive(existing))
        {
            var conflict = request.CreateResponse(HttpStatusCode.Conflict);
            conflict.WriteString($"{_instanceId} is already running");
            return conflict;
        }

        await client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName,
            new StartOrchestrationOptions(_instanceId));
        return await client.CreateCheckStatusResponseAsync(request, _instanceId);
    }

    /// <summary>Cancels an active rebuild and purges its completed instance.</summary>
    public async Task<HttpResponseData> CancelOrchestration(
        HttpRequestData request,
        DurableTaskClient client)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);
        var existing = await client.GetInstanceAsync(_instanceId);
        if (existing is null)
        {
            return response;
        }

        if (IsInstanceActive(existing))
        {
            if (existing.RuntimeStatus == OrchestrationRuntimeStatus.Suspended)
            {
                await client.ResumeInstanceAsync(_instanceId, $"{_instanceId} resumed to cancel");
            }

            await client.TerminateInstanceAsync(_instanceId, $"{_instanceId} cancelled");
            await client.WaitForInstanceCompletionAsync(_instanceId);
            response.WriteString($"{_instanceId} cancelled");
        }

        existing = await client.GetInstanceAsync(_instanceId);
        if (existing is not null && !existing.IsRunning)
        {
            await client.PurgeInstanceAsync(_instanceId);
        }

        return response;
    }

    /// <summary>Runs delete, page, and fan-out batch activities until all aggregates are rebuilt.</summary>
    public async Task OrchestrateInitAsync(
        TaskOrchestrationContext context,
        string deleteActivityName,
        string getIdsActivityName,
        string processBatchActivityName,
        ILogger? logger = null)
    {
        await context.CallActivityAsync(deleteActivityName, null, _durableTaskOptions);
        logger?.LogInformation("{InstanceId}: deleted all current-state items", _instanceId);

        var pageNumber = 0;
        var totalProcessed = 0;
        while (true)
        {
            var ids = await context.CallActivityAsync<List<Guid>>(
                getIdsActivityName,
                new DurableCurrentStatePageInfo(pageNumber),
                _durableTaskOptions);

            if (ids.Count == 0)
            {
                break;
            }

            var tasks = ids
                .Chunk(_batchSize)
                .Select(batch => context.CallActivityAsync(
                    processBatchActivityName,
                    batch.ToList(),
                    _durableTaskOptions));
            await Task.WhenAll(tasks);

            totalProcessed += ids.Count;
            logger?.LogInformation("{InstanceId}: {TotalProcessed} aggregates rebuilt", _instanceId, totalProcessed);
            pageNumber++;

            if (ids.Count < _pageSize)
            {
                break;
            }
        }

        logger?.LogInformation("{InstanceId}: current-state rebuild complete", _instanceId);
    }

    /// <summary>Deletes every item from the aggregate current-state container.</summary>
    public async Task DeleteAllCurrentState(DurableTaskClient? client = null)
    {
        if (client is not null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        var container = await _nostify.GetBulkCurrentStateContainerAsync<TAggregate>(_partitionKeyPath);
        var aggregates = await container.WithRetry(_cosmosRetryOptions)
            .GetItemLinqQueryable<TAggregate>()
            .ReadAllAsync();
        await container.BulkDeleteAsync(aggregates, _cosmosRetryOptions);
    }

    /// <summary>Gets a stable page of distinct aggregate IDs from the event store.</summary>
    public async Task<List<Guid>> GetAggregateIds(DurableCurrentStatePageInfo request)
    {
        var eventStore = await _nostify.GetEventStoreContainerAsync();
        return await eventStore.WithRetry(_cosmosRetryOptions)
            .GetItemLinqQueryable<Event>()
            .Select(@event => @event.aggregateRootId)
            .Distinct()
            .OrderBy(id => id)
            .Skip(request.PageNumber * _pageSize)
            .Take(_pageSize)
            .ReadAllAsync();
    }

    /// <summary>Rehydrates and bulk-upserts one batch of aggregate current states.</summary>
    public async Task ProcessBatch(List<Guid> ids, DurableTaskClient? client = null)
    {
        if (client is not null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        var eventStore = await _nostify.GetEventStoreContainerAsync();
        var events = await eventStore.WithRetry(_cosmosRetryOptions)
            .GetItemLinqQueryable<Event>()
            .Where(@event => ids.Contains(@event.aggregateRootId))
            .ReadAllAsync();

        var aggregates = ids.Select(id =>
        {
            var aggregate = new TAggregate();
            foreach (var @event in events
                .Where(item => item.aggregateRootId == id)
                .OrderBy(item => item.timestamp))
            {
                aggregate.Apply(@event);
            }

            return aggregate;
        }).ToList();

        if (client is not null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        var currentState = await _nostify.GetBulkCurrentStateContainerAsync<TAggregate>(_partitionKeyPath);
        await currentState.WithRetry(_cosmosRetryOptions).DoBulkUpsertAsync(aggregates);
    }

    /// <summary>Returns true when the orchestration no longer permits activity work.</summary>
    public async Task<bool> IsCancellationRequestedAsync(
        DurableTaskClient client,
        CancellationToken cancellationToken = default)
    {
        var existing = await client.GetInstanceAsync(_instanceId, false, cancellationToken);
        return !IsInstanceActive(existing);
    }

    private static bool IsInstanceActive(OrchestrationMetadata? metadata)
        => metadata is not null
            && metadata.RuntimeStatus is OrchestrationRuntimeStatus.Running
                or OrchestrationRuntimeStatus.Pending
                or OrchestrationRuntimeStatus.Suspended;

    /// <summary>
    /// Supplies the projection constraints needed only to reuse the library's established
    /// default Durable Functions retry policy; it is never instantiated by rebuild logic.
    /// </summary>
    private sealed class TAggregateProjectionAdapter : NostifyObject, IProjection, IHasExternalData<TAggregateProjectionAdapter>
    {
        public static string containerName => string.Empty;
        public bool initialized { get; set; }

        public Task<TAggregateProjectionAdapter> InitAsync(INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
            => Task.FromResult(this);
    }
}

/// <summary>Serializable zero-based page request for aggregate current-state rebuilding.</summary>
public readonly struct DurableCurrentStatePageInfo
{
    public DurableCurrentStatePageInfo(int pageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNumber);
        PageNumber = pageNumber;
    }

    public int PageNumber { get; }
}
