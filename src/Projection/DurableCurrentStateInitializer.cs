using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
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
    private static readonly Action<ILogger, string, Exception?> LogCurrentStateDeleted =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(LogCurrentStateDeleted)),
            "{InstanceId}: deleted all current-state items");

    private static readonly Action<ILogger, string, int, Exception?> LogAggregateRebuildProgress =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(2, nameof(LogAggregateRebuildProgress)),
            "{InstanceId}: {TotalProcessed} aggregates rebuilt");

    private static readonly Action<ILogger, string, Exception?> LogCurrentStateRebuildComplete =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3, nameof(LogCurrentStateRebuildComplete)),
            "{InstanceId}: current-state rebuild complete");

    private readonly INostify _nostify;
    private readonly string _instanceId;
    private readonly string _partitionKeyPath;
    private readonly int _batchSize;
    private readonly int _pageSize;
    private readonly TaskOptions _durableTaskOptions;
    private readonly RetryOptions _cosmosRetryOptions;
    private readonly IQueryExecutor _queryExecutor;
    private readonly Func<Microsoft.Azure.Cosmos.Container, RetryOptions, IRetryableContainer> _retryableContainerFactory;

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
        : this(
            nostify,
            instanceId,
            partitionKeyPath,
            batchSize,
            concurrentBatchCount,
            durableTaskOptions,
            cosmosRetryOptions,
            CosmosQueryExecutor.Default,
            static (container, options) => container.WithRetry(options))
    {
    }

    /// <summary>Creates an initializer with deterministic infrastructure adapters for tests.</summary>
    internal DurableCurrentStateInitializer(
        INostify nostify,
        string instanceId,
        string partitionKeyPath,
        int batchSize,
        int concurrentBatchCount,
        TaskOptions? durableTaskOptions,
        RetryOptions? cosmosRetryOptions,
        IQueryExecutor queryExecutor,
        Func<Microsoft.Azure.Cosmos.Container, RetryOptions, IRetryableContainer> retryableContainerFactory)
    {
        ArgumentNullException.ThrowIfNull(nostify);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKeyPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrentBatchCount);
        ArgumentNullException.ThrowIfNull(queryExecutor);
        ArgumentNullException.ThrowIfNull(retryableContainerFactory);

        _nostify = nostify;
        _instanceId = instanceId;
        _partitionKeyPath = partitionKeyPath;
        _batchSize = batchSize;
        _pageSize = checked(batchSize * concurrentBatchCount);
        _durableTaskOptions = durableTaskOptions ?? CreateDefaultTaskOptions();
        _cosmosRetryOptions = cosmosRetryOptions ?? new RetryOptions();
        _queryExecutor = queryExecutor;
        _retryableContainerFactory = retryableContainerFactory;
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
        if (logger != null) LogCurrentStateDeleted(logger, _instanceId, null);

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
            if (logger != null) LogAggregateRebuildProgress(logger, _instanceId, totalProcessed, null);
            pageNumber++;

            if (ids.Count < _pageSize)
            {
                break;
            }
        }

        if (logger != null) LogCurrentStateRebuildComplete(logger, _instanceId, null);
    }

    /// <summary>Deletes every item from the aggregate current-state container.</summary>
    public async Task DeleteAllCurrentState(DurableTaskClient? client = null)
    {
        if (client is not null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        var container = await _nostify.GetBulkCurrentStateContainerAsync<TAggregate>(_partitionKeyPath);
        var aggregates = await _queryExecutor.ReadAllAsync(
            container.GetItemLinqQueryable<TAggregate>());
        await DeleteCurrentStateAsync(container, aggregates);
    }

    /// <summary>Gets a stable page of distinct aggregate IDs from the event store.</summary>
    public async Task<List<Guid>> GetAggregateIds(DurableCurrentStatePageInfo request)
    {
        var eventStore = await _nostify.GetEventStoreContainerAsync();
        var query = eventStore
            .GetItemLinqQueryable<Event>()
            .Select(@event => @event.aggregateRootId)
            .Distinct()
            .OrderBy(id => id)
            .Skip(request.PageNumber * _pageSize)
            .Take(_pageSize);
        return await _queryExecutor.ReadAllAsync(query);
    }

    /// <summary>Rehydrates and bulk-upserts one batch of aggregate current states.</summary>
    public async Task ProcessBatch(List<Guid> ids, DurableTaskClient? client = null)
    {
        if (client is not null && await IsCancellationRequestedAsync(client))
        {
            return;
        }

        var eventStore = await _nostify.GetEventStoreContainerAsync();
        var eventsQuery = eventStore
            .GetItemLinqQueryable<Event>()
            .Where(@event => ids.Contains(@event.aggregateRootId));
        var events = await _queryExecutor.ReadAllAsync(eventsQuery);

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
        await _retryableContainerFactory(currentState, _cosmosRetryOptions)
            .DoBulkUpsertAsync(aggregates);
    }

    /// <summary>Deletes the selected aggregate states from the current-state container.</summary>
    internal virtual Task<int> DeleteCurrentStateAsync(
        Microsoft.Azure.Cosmos.Container container,
        List<TAggregate> aggregates)
        => container.BulkDeleteAsync(aggregates, _cosmosRetryOptions);

    /// <summary>Returns true when the orchestration no longer permits activity work.</summary>
    public async Task<bool> IsCancellationRequestedAsync(
        DurableTaskClient client,
        CancellationToken cancellationToken = default)
    {
        var existing = await client.GetInstanceAsync(_instanceId, false, cancellationToken);
        return !IsInstanceActive(existing);
    }

    /// <summary>Builds the default retry policy for activity invocations.</summary>
    public static TaskOptions CreateDefaultTaskOptions()
        => new(TaskRetryOptions.FromRetryPolicy(new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5),
            backoffCoefficient: 2.0)));

    private static bool IsInstanceActive(OrchestrationMetadata? metadata)
        => metadata is not null
            && metadata.RuntimeStatus is OrchestrationRuntimeStatus.Running
                or OrchestrationRuntimeStatus.Pending
                or OrchestrationRuntimeStatus.Suspended;
}

/// <summary>Serializable zero-based page request for aggregate current-state rebuilding.</summary>
public readonly struct DurableCurrentStatePageInfo
{
    /// <summary>Creates a page request.</summary>
    /// <param name="pageNumber">The zero-based page number.</param>
    public DurableCurrentStatePageInfo(int pageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNumber);
        PageNumber = pageNumber;
    }

    /// <summary>Gets the zero-based page number.</summary>
    public int PageNumber { get; }
}
