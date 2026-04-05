using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace nostify;

/// <summary>
/// Wraps a Cosmos DB <see cref="Container"/> with configurable retry logic.
/// Automatically retries on 429 TooManyRequests (always on, uses server RetryAfter header)
/// and optionally retries on 404 NotFound based on <see cref="RetryOptions.RetryWhenNotFound"/>.
/// Supports exponential backoff via <see cref="RetryOptions.DelayMultiplier"/> and optional logging.
/// </summary>
/// <remarks>
/// Create instances via <see cref="RetryableContainerExtensions.WithRetry"/>.
/// Callbacks (onExhausted, onNotFound, onException) allow callers to handle failures
/// without coupling the retry wrapper to INostify or any specific error handling strategy.
/// </remarks>
public class RetryableContainer : IRetryableContainer
{
    /// <inheritdoc/>
    public Container Container { get; }

    /// <inheritdoc/>
    public RetryOptions Options { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryableContainer"/> class.
    /// </summary>
    /// <param name="container">The underlying Cosmos DB container.</param>
    /// <param name="options">The retry options governing retry behavior.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="container"/> or <paramref name="options"/> is null.</exception>
    public RetryableContainer(Container container, RetryOptions options)
    {
        Container = container ?? throw new ArgumentNullException(nameof(container));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public async Task<T?> ApplyAndPersistAsync<T>(
        IEvent newEvent,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null) where T : NostifyObject, new()
    {
        return await ExecuteWithRetryAsync<T>(
            async () => await Container.ApplyAndPersistAsync<T>(newEvent),
            onExhausted,
            onNotFound,
            onException,
            $"ApplyAndPersist<{typeof(T).Name}> for aggregateRootId {newEvent.aggregateRootId}"
        );
    }

    /// <inheritdoc/>
    public async Task<T?> ApplyAndPersistAsync<T>(
        IEvent newEvent,
        Guid projectionBaseAggregateId,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null) where T : NostifyObject, new()
    {
        return await ExecuteWithRetryAsync<T>(
            async () => await Container.ApplyAndPersistAsync<T>(newEvent, projectionBaseAggregateId),
            onExhausted,
            onNotFound,
            onException,
            $"ApplyAndPersist<{typeof(T).Name}> for projectionBaseAggregateId {projectionBaseAggregateId}"
        );
    }

    /// <inheritdoc/>
    public async Task<ItemResponse<T>?> ReadItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt <= Options.MaxRetries; attempt++)
        {
            try
            {
                return await Container.ReadItemAsync<T>(id, partitionKey, cancellationToken: cancellationToken);
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await HandleTooManyRequestsAsync(ce, attempt, $"ReadItem<{typeof(T).Name}> for {id}");
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.NotFound)
            {
                if (!Options.RetryWhenNotFound)
                {
                    Options.LogRetry($"ReadItem<{typeof(T).Name}> for {id}: NotFound, RetryWhenNotFound=false");
                    if (onNotFound != null) await onNotFound();
                    return default;
                }

                if (attempt < Options.MaxRetries)
                {
                    var delay = Options.GetDelayForAttempt(attempt);
                    Options.LogRetry($"ReadItem<{typeof(T).Name}> for {id}: NotFound, retrying (attempt {attempt + 1}/{Options.MaxRetries}) after {delay.TotalMilliseconds}ms");
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    Options.LogRetry($"ReadItem<{typeof(T).Name}> for {id}: NotFound, exhausted {Options.MaxRetries} retries");
                    if (onExhausted != null) await onExhausted();
                    return default;
                }
            }
            catch (Exception ex)
            {
                if (onException != null) await onException(ex);
                else throw;
                return default;
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public async Task<ItemResponse<T>?> CreateItemAsync<T>(
        T item,
        PartitionKey? partitionKey,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt <= Options.MaxRetries; attempt++)
        {
            try
            {
                return await (partitionKey.HasValue
                    ? Container.CreateItemAsync(item, partitionKey.Value, cancellationToken: cancellationToken)
                    : Container.CreateItemAsync(item, cancellationToken: cancellationToken));
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await HandleTooManyRequestsAsync(ce, attempt, $"CreateItem<{typeof(T).Name}>");
            }
            catch (Exception ex)
            {
                if (onException != null) await onException(ex);
                else throw;
                return default;
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public async Task<ItemResponse<T>?> CreateItemAsync<T>(
        T item,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateItemAsync(item, null, onException, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ItemResponse<T>?> UpsertItemAsync<T>(
        T item,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt <= Options.MaxRetries; attempt++)
        {
            try
            {
                return await Container.UpsertItemAsync(item, cancellationToken: cancellationToken);
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await HandleTooManyRequestsAsync(ce, attempt, $"UpsertItem<{typeof(T).Name}>");
            }
            catch (Exception ex)
            {
                if (onException != null) await onException(ex);
                else throw;
                return default;
            }
        }

        return default;
    }

    /// <summary>
    /// Core retry logic for ApplyAndPersistAsync operations.
    /// ApplyAndPersistAsync returns null on NotFound (it catches CosmosException internally),
    /// so we interpret null as "not found" for retry purposes.
    /// 429 TooManyRequests is thrown and caught separately.
    /// </summary>
    private async Task<T?> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        Func<Task>? onExhausted,
        Func<Task>? onNotFound,
        Func<Exception, Task>? onException,
        string operationDescription) where T : NostifyObject, new()
    {
        for (int attempt = 0; attempt <= Options.MaxRetries; attempt++)
        {
            try
            {
                T? result = await operation();

                // ApplyAndPersistAsync returns null when the item is not found
                if (result != null)
                {
                    return result;
                }

                // Result is null - the item was not found
                if (!Options.RetryWhenNotFound)
                {
                    Options.LogRetry($"{operationDescription}: NotFound, RetryWhenNotFound=false");
                    if (onNotFound != null) await onNotFound();
                    return default;
                }

                if (attempt < Options.MaxRetries)
                {
                    var delay = Options.GetDelayForAttempt(attempt);
                    Options.LogRetry($"{operationDescription}: NotFound, retrying (attempt {attempt + 1}/{Options.MaxRetries}) after {delay.TotalMilliseconds}ms");
                    await Task.Delay(delay);
                }
                else
                {
                    Options.LogRetry($"{operationDescription}: NotFound, exhausted {Options.MaxRetries} retries");
                    if (onExhausted != null) await onExhausted();
                    return default;
                }
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await HandleTooManyRequestsAsync(ce, attempt, operationDescription);
            }
            catch (Exception ex)
            {
                Options.LogRetry($"{operationDescription}: Exception {ex.Message}");
                if (onException != null) await onException(ex);
                else throw;
                return default;
            }
        }

        return default;
    }

    #region Query Methods

    /// <inheritdoc/>
    public RetryableQuery<T> FilteredQuery<T>(Guid tenantId, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject, ITenantFilterable
    {
        return new RetryableQuery<T>(Container.FilteredQuery<T>(tenantId, filterExpression), Options);
    }

    /// <inheritdoc/>
    public RetryableQuery<T> FilteredQuery<T>(string partitionKeyValue, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject
    {
        return new RetryableQuery<T>(Container.FilteredQuery<T>(partitionKeyValue, filterExpression), Options);
    }

    /// <inheritdoc/>
    public RetryableQuery<T> FilteredQuery<T>(PartitionKey partitionKey, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject
    {
        return new RetryableQuery<T>(Container.FilteredQuery<T>(partitionKey, filterExpression), Options);
    }

    /// <inheritdoc/>
    public RetryableQuery<T> GetItemLinqQueryable<T>(QueryRequestOptions? requestOptions = null)
    {
        var queryable = requestOptions != null
            ? Container.GetItemLinqQueryable<T>(requestOptions: requestOptions)
            : Container.GetItemLinqQueryable<T>();
        return new RetryableQuery<T>(queryable, Options);
    }

    #endregion

    #region Bulk Methods

    /// <inheritdoc/>
    public async Task DoBulkCreateAsync<T>(
        List<T> itemList,
        Func<Exception, Task>? onException = null) where T : IApplyable
    {
        Container.ValidateBulkEnabled(true);

        List<Task> taskList = new List<Task>();
        itemList.ForEach(i => taskList.Add(CreateItemAsync(
            i,
            onException: onException ?? ((ex) => Task.FromException(new NostifyException($"Bulk Create Error {ex.Message}")))
        )));
        await Task.WhenAll(taskList);
    }

    /// <inheritdoc/>
    public async Task DoBulkUpsertAsync<T>(
        List<T> itemList,
        Func<Exception, Task>? onException = null) where T : IApplyable
    {
        Container.ValidateBulkEnabled(true);

        List<Task> taskList = new List<Task>();
        itemList.ForEach(i => taskList.Add(UpsertItemAsync(
            i,
            onException: onException ?? ((ex) => Task.FromException(new NostifyException($"Bulk Upsert Error {ex.Message}")))
        )));
        await Task.WhenAll(taskList);
    }

    #endregion

    /// <summary>
    /// Handles 429 TooManyRequests by waiting the server-specified RetryAfter duration.
    /// Always retries regardless of RetryOptions settings because 429 is always transient.
    /// </summary>
    private async Task HandleTooManyRequestsAsync(CosmosException ce, int attempt, string operationDescription)
    {
        if (attempt >= Options.MaxRetries)
        {
            Options.LogRetry($"{operationDescription}: 429 TooManyRequests, exhausted {Options.MaxRetries} retries");
            throw ce;
        }

        int waitTime = ce.RetryAfter.HasValue ? (int)ce.RetryAfter.Value.TotalMilliseconds : 1000;
        Options.LogRetry($"{operationDescription}: 429 TooManyRequests, retrying (attempt {attempt + 1}/{Options.MaxRetries}) after {waitTime}ms");
        await Task.Delay(waitTime);
    }
}
