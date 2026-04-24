using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace nostify;

/// <summary>
/// Defines a retry-capable wrapper around a Cosmos DB <see cref="Container"/>.
/// Provides methods that automatically retry on transient failures (404 NotFound) based on <see cref="RetryOptions"/>,
/// and always retry on 429 TooManyRequests using the server-provided RetryAfter header.
/// </summary>
/// <remarks>
/// Use <see cref="RetryableContainerExtensions.WithRetry"/> to create an instance from a <see cref="Container"/>.
/// Callbacks are used to handle exhausted retries, not-found results, and exceptions without coupling
/// the retry logic to the <see cref="INostify"/> interface.
/// </remarks>
public interface IRetryableContainer
{
    /// <summary>
    /// Gets the underlying Cosmos DB <see cref="Container"/>.
    /// </summary>
    Container Container { get; }

    /// <summary>
    /// Gets the <see cref="RetryOptions"/> governing retry behavior.
    /// </summary>
    RetryOptions Options { get; }

    /// <summary>
    /// Applies an event and persists the result with retry logic.
    /// On success, returns the updated object. On not-found, retries according to <see cref="RetryOptions"/>.
    /// On 429 TooManyRequests, always retries using the server's RetryAfter header.
    /// </summary>
    /// <typeparam name="T">The type of NostifyObject to apply and persist.</typeparam>
    /// <param name="newEvent">The event to apply.</param>
    /// <param name="onExhausted">Callback invoked when all retries are exhausted for a not-found result.</param>
    /// <param name="onNotFound">Callback invoked when the item is not found and RetryWhenNotFound is false.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs.</param>
    /// <returns>The updated object, or null if all retries were exhausted or the item was not found.</returns>
    Task<T?> ApplyAndPersistAsync<T>(
        IEvent newEvent,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null) where T : NostifyObject, new();

    /// <summary>
    /// Applies an event to a specific projection (by projectionBaseAggregateId) and persists with retry logic.
    /// </summary>
    /// <typeparam name="T">The type of NostifyObject to apply and persist.</typeparam>
    /// <param name="newEvent">The event to apply.</param>
    /// <param name="projectionBaseAggregateId">The ID of the projection base aggregate to apply to.</param>
    /// <param name="onExhausted">Callback invoked when all retries are exhausted for a not-found result.</param>
    /// <param name="onNotFound">Callback invoked when the item is not found and RetryWhenNotFound is false.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs.</param>
    /// <returns>The updated object, or null if all retries were exhausted or the item was not found.</returns>
    Task<T?> ApplyAndPersistAsync<T>(
        IEvent newEvent,
        Guid projectionBaseAggregateId,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null) where T : NostifyObject, new();

    /// <summary>
    /// Reads an item with retry logic for transient failures.
    /// </summary>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    /// <param name="id">The item ID.</param>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="onExhausted">Callback invoked when all retries are exhausted.</param>
    /// <param name="onNotFound">Callback invoked when the item is not found and RetryWhenNotFound is false.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item response, or default if not found.</returns>
    Task<ItemResponse<T>?> ReadItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an item with automatic 429 retry. Does NOT retry on 404.
    /// </summary>
    /// <typeparam name="T">The type of item to create.</typeparam>
    /// <param name="item">The item to create.</param>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item response.</returns>
    Task<ItemResponse<T>?> CreateItemAsync<T>(
        T item,
        PartitionKey? partitionKey,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an item with automatic 429 retry. The partition key is inferred from the item
    /// by the Cosmos DB SDK based on the container's partition key path. Does NOT retry on 404.
    /// </summary>
    /// <typeparam name="T">The type of item to create.</typeparam>
    /// <param name="item">The item to create.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item response.</returns>
    Task<ItemResponse<T>?> CreateItemAsync<T>(
        T item,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts an item with automatic 429 retry.
    /// </summary>
    /// <typeparam name="T">The type of item to upsert.</typeparam>
    /// <param name="item">The item to upsert.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item response.</returns>
    Task<ItemResponse<T>?> UpsertItemAsync<T>(
        T item,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a filtered, retry-aware queryable for items within a specific tenant partition.
    /// Chain LINQ-style methods on the returned <see cref="RetryableQuery{T}"/> and call
    /// terminal methods (ReadAllAsync, FirstOrDefaultAsync, CountAsync) for automatic 429 retry.
    /// </summary>
    /// <typeparam name="T">The type of items to query that implements ITenantFilterable.</typeparam>
    /// <param name="tenantId">The tenant ID to use as the partition key.</param>
    /// <param name="filterExpression">Optional filter expression to apply to the query.</param>
    /// <returns>A <see cref="RetryableQuery{T}"/> with retry-aware terminal operations.</returns>
    RetryableQuery<T> FilteredQuery<T>(Guid tenantId, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject, ITenantFilterable;

    /// <summary>
    /// Creates a filtered, retry-aware queryable for items within a specific partition.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="partitionKeyValue">The partition key value to filter by.</param>
    /// <param name="filterExpression">Optional filter expression to apply to the query.</param>
    /// <returns>A <see cref="RetryableQuery{T}"/> with retry-aware terminal operations.</returns>
    RetryableQuery<T> FilteredQuery<T>(string partitionKeyValue, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject;

    /// <summary>
    /// Creates a filtered, retry-aware queryable for items within a specific partition.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="partitionKey">The partition key to filter by.</param>
    /// <param name="filterExpression">Optional filter expression to apply to the query.</param>
    /// <returns>A <see cref="RetryableQuery{T}"/> with retry-aware terminal operations.</returns>
    RetryableQuery<T> FilteredQuery<T>(PartitionKey partitionKey, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject;

    /// <summary>
    /// Creates a retry-aware LINQ queryable for all items in the container.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="requestOptions">Optional query request options.</param>
    /// <returns>A <see cref="RetryableQuery{T}"/> with retry-aware terminal operations.</returns>
    RetryableQuery<T> GetItemLinqQueryable<T>(QueryRequestOptions? requestOptions = null);

    /// <summary>
    /// Bulk creates a list of items with per-item retry on 429 TooManyRequests.
    /// The underlying container must have bulk operations enabled.
    /// </summary>
    /// <typeparam name="T">The type of items to create.</typeparam>
    /// <param name="itemList">List of items to create.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs for an individual item. Receives the item being created and the exception.</param>
    /// <returns>A task representing the asynchronous bulk create operation.</returns>
    Task DoBulkCreateAsync<T>(
        List<T> itemList,
        Func<T, Exception, Task>? onException = null);

    /// <summary>
    /// Bulk upserts a list of items with per-item retry on 429 TooManyRequests.
    /// The underlying container must have bulk operations enabled.
    /// </summary>
    /// <typeparam name="T">The type of items to upsert.</typeparam>
    /// <param name="itemList">List of items to upsert.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs for an individual item.</param>
    /// <returns>A task representing the asynchronous bulk upsert operation.</returns>
    Task DoBulkUpsertAsync<T>(
        List<T> itemList,
        Func<T, Exception, Task>? onException = null);

    /// <summary>
    /// Bulk creates events with per-item retry on 429 TooManyRequests using each event's aggregateRootId partition key.
    /// The underlying container must have bulk operations enabled.
    /// </summary>
    /// <param name="eventList">List of events to create.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs for an individual event. Receives the event and the exception.</param>
    /// <returns>A task representing the asynchronous bulk create operation.</returns>
    Task DoBulkCreateEventAsync(
        List<IEvent> eventList,
        Func<IEvent, Exception, Task>? onException = null);

    /// <summary>
    /// Bulk upserts events with per-item retry on 429 TooManyRequests using each event's aggregateRootId partition key.
    /// The underlying container must have bulk operations enabled.
    /// </summary>
    /// <param name="eventList">List of events to upsert.</param>
    /// <param name="onException">Callback invoked when a non-transient exception occurs for an individual event. Receives the event and the exception.</param>
    /// <returns>A task representing the asynchronous bulk upsert operation.</returns>
    Task DoBulkUpsertEventAsync(
        List<IEvent> eventList,
        Func<IEvent, Exception, Task>? onException = null);
}
