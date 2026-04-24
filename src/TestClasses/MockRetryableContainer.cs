using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace nostify;

/// <summary>
/// A test-friendly mock implementation of <see cref="IRetryableContainer"/> that does NOT perform actual retries.
/// Instead, it delegates directly to configurable callback functions, allowing tests to simulate success,
/// not-found, and exception scenarios without needing a real Cosmos DB container or retry delays.
/// </summary>
/// <remarks>
/// Use this class in unit tests to verify that handler code correctly interacts with <see cref="IRetryableContainer"/>
/// and responds appropriately to the various callbacks (onExhausted, onNotFound, onException).
/// </remarks>
/// <example>
/// <code>
/// var mock = new MockRetryableContainer&lt;MyProjection&gt;(
///     applyResult: new MyProjection { id = testId, name = "Updated" }
/// );
/// // Pass mock to handler code that expects IRetryableContainer
/// </code>
/// </example>
public class MockRetryableContainer<T> : IRetryableContainer where T : NostifyObject, new()
{
    private readonly T? _applyResult;
    private readonly Exception? _exceptionToThrow;
    private readonly bool _simulateNotFound;
    private readonly bool _simulateExhausted;
    private int _applyCallCount;
    private int _readCallCount;
    private readonly int _notFoundUntilAttempt;

    /// <summary>
    /// Gets the underlying Cosmos DB <see cref="Container"/>. Always null in mock.
    /// </summary>
    public Container Container { get; } = null!;

    /// <summary>
    /// Gets the <see cref="RetryOptions"/> governing retry behavior.
    /// </summary>
    public RetryOptions Options { get; }

    /// <summary>
    /// Gets the number of times <see cref="ApplyAndPersistAsync{T}(IEvent, Func{Task}?, Func{Task}?, Func{Exception, Task}?)"/>
    /// has been called.
    /// </summary>
    public int ApplyCallCount => _applyCallCount;

    /// <summary>
    /// Gets the number of times <see cref="ReadItemAsync{T}"/> has been called.
    /// </summary>
    public int ReadCallCount => _readCallCount;

    /// <summary>
    /// The events that were passed to ApplyAndPersistAsync, in order.
    /// </summary>
    public List<IEvent> AppliedEvents { get; } = new List<IEvent>();

    /// <summary>
    /// Creates a mock that returns the given result on ApplyAndPersistAsync calls.
    /// </summary>
    /// <param name="applyResult">The result to return. Pass null to simulate not-found.</param>
    /// <param name="options">Optional retry options. Defaults to <see cref="RetryOptions"/> with default values.</param>
    public MockRetryableContainer(T? applyResult = null, RetryOptions? options = null)
    {
        _applyResult = applyResult;
        Options = options ?? new RetryOptions();
    }

    /// <summary>
    /// Creates a mock that throws the specified exception on ApplyAndPersistAsync calls.
    /// </summary>
    /// <param name="exceptionToThrow">The exception to throw.</param>
    /// <param name="options">Optional retry options.</param>
    public MockRetryableContainer(Exception exceptionToThrow, RetryOptions? options = null)
    {
        _exceptionToThrow = exceptionToThrow;
        Options = options ?? new RetryOptions();
    }

    /// <summary>
    /// Creates a mock that simulates not-found behavior, invoking onNotFound or onExhausted callbacks.
    /// </summary>
    /// <param name="simulateNotFound">If true, invokes onNotFound callback (RetryWhenNotFound=false behavior).</param>
    /// <param name="simulateExhausted">If true, invokes onExhausted callback (retries exhausted behavior).</param>
    /// <param name="options">Optional retry options.</param>
    public MockRetryableContainer(bool simulateNotFound, bool simulateExhausted, RetryOptions? options = null)
    {
        _simulateNotFound = simulateNotFound;
        _simulateExhausted = simulateExhausted;
        Options = options ?? new RetryOptions();
    }

    /// <summary>
    /// Creates a mock that returns not-found for the first N calls, then returns the given result.
    /// This simulates eventual consistency where an item becomes available after some delay.
    /// </summary>
    /// <param name="notFoundUntilAttempt">Number of calls that return null before succeeding.</param>
    /// <param name="applyResult">The result to return after the not-found attempts.</param>
    /// <param name="options">Optional retry options.</param>
    public MockRetryableContainer(int notFoundUntilAttempt, T applyResult, RetryOptions? options = null)
    {
        _notFoundUntilAttempt = notFoundUntilAttempt;
        _applyResult = applyResult;
        Options = options ?? new RetryOptions();
    }

    /// <inheritdoc/>
    public async Task<TResult?> ApplyAndPersistAsync<TResult>(
        IEvent newEvent,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null) where TResult : NostifyObject, new()
    {
        Interlocked.Increment(ref _applyCallCount);
        lock (AppliedEvents) { AppliedEvents.Add(newEvent); }

        if (_exceptionToThrow != null)
        {
            if (onException != null) { await onException(_exceptionToThrow); return default; }
            throw _exceptionToThrow;
        }

        if (_simulateNotFound)
        {
            if (onNotFound != null) await onNotFound();
            return default;
        }

        if (_simulateExhausted)
        {
            if (onExhausted != null) await onExhausted();
            return default;
        }

        // Simulate eventual consistency: return null until notFoundUntilAttempt reached
        if (_notFoundUntilAttempt > 0 && _applyCallCount <= _notFoundUntilAttempt)
        {
            // The real RetryableContainer handles this internally; in mock we just return the result
            // since the mock doesn't actually retry. For testing handler logic that uses IRetryableContainer,
            // the callbacks should already be wired not to need mock-level retry.
            return default;
        }

        return _applyResult as TResult;
    }

    /// <inheritdoc/>
    public async Task<TResult?> ApplyAndPersistAsync<TResult>(
        IEvent newEvent,
        Guid projectionBaseAggregateId,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null) where TResult : NostifyObject, new()
    {
        // Delegate to the same logic as the non-projectionBaseAggregateId overload
        return await ApplyAndPersistAsync<TResult>(newEvent, onExhausted, onNotFound, onException);
    }

    /// <inheritdoc/>
    public async Task<ItemResponse<TResult>?> ReadItemAsync<TResult>(
        string id,
        PartitionKey partitionKey,
        Func<Task>? onExhausted = null,
        Func<Task>? onNotFound = null,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _readCallCount);

        if (_exceptionToThrow != null)
        {
            if (onException != null) { await onException(_exceptionToThrow); return default; }
            throw _exceptionToThrow;
        }

        if (_simulateNotFound)
        {
            if (onNotFound != null) await onNotFound();
            return default;
        }

        return default;
    }

    /// <inheritdoc/>
    public async Task<ItemResponse<TResult>?> CreateItemAsync<TResult>(
        TResult item,
        PartitionKey? partitionKey,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default)
    {
        if (_exceptionToThrow != null)
        {
            if (onException != null) { await onException(_exceptionToThrow); return default; }
            throw _exceptionToThrow;
        }

        return default;
    }

    /// <inheritdoc/>
    public async Task<ItemResponse<TResult>?> CreateItemAsync<TResult>(
        TResult item,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default)
    {
        if (_exceptionToThrow != null)
        {
            if (onException != null) { await onException(_exceptionToThrow); return default; }
            throw _exceptionToThrow;
        }

        return default;
    }

    /// <inheritdoc/>
    public async Task<ItemResponse<TResult>?> UpsertItemAsync<TResult>(
        TResult item,
        Func<Exception, Task>? onException = null,
        CancellationToken cancellationToken = default)
    {
        if (_exceptionToThrow != null)
        {
            if (onException != null) { await onException(_exceptionToThrow); return default; }
            throw _exceptionToThrow;
        }

        return default;
    }

    #region Query Methods

    /// <summary>
    /// Gets the in-memory queryable data for the mock's generic type.
    /// Returns an empty queryable if no data was provided or if the requested type doesn't match.
    /// </summary>
    private IQueryable<TQuery> GetMockQueryable<TQuery>(Expression<Func<TQuery, bool>>? filterExpression = null)
    {
        IQueryable<TQuery> data;
        if (typeof(TQuery) == typeof(T) && _applyResult != null)
        {
            // Single item as queryable data (for basic mock scenarios)
            data = new List<TQuery> { (TQuery)(object)_applyResult }.AsQueryable();
        }
        else
        {
            data = Enumerable.Empty<TQuery>().AsQueryable();
        }

        if (filterExpression != null)
        {
            data = data.Where(filterExpression);
        }
        return data;
    }

    /// <inheritdoc/>
    public RetryableQuery<TQuery> FilteredQuery<TQuery>(Guid tenantId, Expression<Func<TQuery, bool>>? filterExpression = null) where TQuery : NostifyObject, ITenantFilterable
    {
        return new RetryableQuery<TQuery>(GetMockQueryable(filterExpression), Options, InMemoryQueryExecutor.Default);
    }

    /// <inheritdoc/>
    public RetryableQuery<TQuery> FilteredQuery<TQuery>(string partitionKeyValue, Expression<Func<TQuery, bool>>? filterExpression = null) where TQuery : NostifyObject
    {
        return new RetryableQuery<TQuery>(GetMockQueryable(filterExpression), Options, InMemoryQueryExecutor.Default);
    }

    /// <inheritdoc/>
    public RetryableQuery<TQuery> FilteredQuery<TQuery>(PartitionKey partitionKey, Expression<Func<TQuery, bool>>? filterExpression = null) where TQuery : NostifyObject
    {
        return new RetryableQuery<TQuery>(GetMockQueryable(filterExpression), Options, InMemoryQueryExecutor.Default);
    }

    /// <inheritdoc/>
    public RetryableQuery<TQuery> GetItemLinqQueryable<TQuery>(QueryRequestOptions? requestOptions = null)
    {
        return new RetryableQuery<TQuery>(GetMockQueryable<TQuery>(), Options, InMemoryQueryExecutor.Default);
    }

    #endregion

    #region Bulk Methods

    /// <inheritdoc/>
    public Task DoBulkCreateAsync<TItem>(
        List<TItem> itemList,
        Func<TItem, Exception, Task>? onException = null)
    {
        if (_exceptionToThrow != null)
        {
            if (onException != null)
            {
                var item = itemList.Count > 0 ? itemList[0] : default!;
                return onException(item, _exceptionToThrow);
            }
            throw _exceptionToThrow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DoBulkUpsertAsync<TItem>(
        List<TItem> itemList,
        Func<TItem, Exception, Task>? onException = null)
    {
        if (_exceptionToThrow != null)
        {
            if (onException != null)
            {
                var item = itemList.Count > 0 ? itemList[0] : default!;
                return onException(item, _exceptionToThrow);
            }
            throw _exceptionToThrow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DoBulkCreateEventAsync(
        List<IEvent> eventList,
        Func<IEvent, Exception, Task>? onException = null)
    {
        if (_exceptionToThrow != null)
        {
            if (onException != null)
            {
                var evt = eventList.Count > 0 ? eventList[0] : default!;
                return onException(evt, _exceptionToThrow);
            }
            throw _exceptionToThrow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DoBulkUpsertEventAsync(
        List<IEvent> eventList,
        Func<IEvent, Exception, Task>? onException = null)
    {
        if (_exceptionToThrow != null)
        {
            if (onException != null)
            {
                var evt = eventList.Count > 0 ? eventList[0] : default!;
                return onException(evt, _exceptionToThrow);
            }
            throw _exceptionToThrow;
        }

        return Task.CompletedTask;
    }

    #endregion
}
