using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace nostify;

/// <summary>
/// Wraps an <see cref="IQueryable{T}"/> with <see cref="RetryOptions"/> to provide retry-aware
/// LINQ query execution against Cosmos DB. Chain LINQ-style methods (Where, Select, OrderBy, etc.)
/// and call terminal methods (ReadAllAsync, FirstOrDefaultAsync, CountAsync) that automatically
/// retry on 429 TooManyRequests using the server's RetryAfter header.
/// </summary>
/// <remarks>
/// Create instances via <see cref="IRetryableContainer.FilteredQuery{T}(PartitionKey, Expression{Func{T, bool}}?)"/> or
/// <see cref="IRetryableContainer.GetItemLinqQueryable{T}(QueryRequestOptions?)"/>.
/// Standard LINQ operators return new <see cref="RetryableQuery{T}"/> instances,
/// preserving retry options through the chain.
/// </remarks>
/// <typeparam name="T">The type of items in the query.</typeparam>
/// <example>
/// <code>
/// var retryable = container.WithRetry(new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), retryWhenNotFound: true));
/// var results = await retryable
///     .FilteredQuery&lt;MyProjection&gt;(tenantId)
///     .Where(x => x.name.Contains("test"))
///     .OrderBy(x => x.name)
///     .ReadAllAsync();
/// </code>
/// </example>
public class RetryableQuery<T>
{
    private readonly IQueryable<T> _query;
    private readonly RetryOptions _options;
    private readonly IQueryExecutor _queryExecutor;

    /// <summary>
    /// Gets the underlying <see cref="IQueryable{T}"/> for interoperability with existing extension methods.
    /// </summary>
    public IQueryable<T> Query => _query;

    /// <summary>
    /// Gets the <see cref="RetryOptions"/> governing retry behavior for terminal operations.
    /// </summary>
    public RetryOptions Options => _options;

    /// <summary>
    /// Initializes a new instance of <see cref="RetryableQuery{T}"/>.
    /// </summary>
    /// <param name="query">The underlying LINQ queryable.</param>
    /// <param name="options">Retry options for terminal operations.</param>
    /// <param name="queryExecutor">Optional query executor. Defaults to <see cref="CosmosQueryExecutor.Default"/>.</param>
    public RetryableQuery(IQueryable<T> query, RetryOptions options, IQueryExecutor? queryExecutor = null)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _queryExecutor = queryExecutor ?? CosmosQueryExecutor.Default;
    }

    #region LINQ Chain Methods

    /// <summary>Filters the query by the specified predicate.</summary>
    /// <param name="predicate">The filter expression.</param>
    /// <returns>A new <see cref="RetryableQuery{T}"/> with the filter applied.</returns>
    public RetryableQuery<T> Where(Expression<Func<T, bool>> predicate)
        => new RetryableQuery<T>(_query.Where(predicate), _options, _queryExecutor);

    /// <summary>Projects each element into a new form.</summary>
    /// <typeparam name="TResult">The type of the projected elements.</typeparam>
    /// <param name="selector">The projection expression.</param>
    /// <returns>A new <see cref="RetryableQuery{TResult}"/> with the projection applied.</returns>
    public RetryableQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
        => new RetryableQuery<TResult>(_query.Select(selector), _options, _queryExecutor);

    /// <summary>Sorts the elements in ascending order by the specified key.</summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>A new <see cref="RetryableQuery{T}"/> with the sort applied.</returns>
    public RetryableQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
        => new RetryableQuery<T>(_query.OrderBy(keySelector), _options, _queryExecutor);

    /// <summary>Sorts the elements in descending order by the specified key.</summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>A new <see cref="RetryableQuery{T}"/> with the sort applied.</returns>
    public RetryableQuery<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        => new RetryableQuery<T>(_query.OrderByDescending(keySelector), _options, _queryExecutor);

    /// <summary>Performs a subsequent ascending ordering by the specified key (after OrderBy/OrderByDescending).</summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>A new <see cref="RetryableQuery{T}"/> with the secondary sort applied.</returns>
    public RetryableQuery<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
        => new RetryableQuery<T>(((IOrderedQueryable<T>)_query).ThenBy(keySelector), _options, _queryExecutor);

    /// <summary>Performs a subsequent descending ordering by the specified key (after OrderBy/OrderByDescending).</summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="keySelector">The key selector expression.</param>
    /// <returns>A new <see cref="RetryableQuery{T}"/> with the secondary sort applied.</returns>
    public RetryableQuery<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        => new RetryableQuery<T>(((IOrderedQueryable<T>)_query).ThenByDescending(keySelector), _options, _queryExecutor);

    /// <summary>Returns a specified number of elements from the start of the query.</summary>
    /// <param name="count">The number of elements to return.</param>
    /// <returns>A new <see cref="RetryableQuery{T}"/> with the take applied.</returns>
    public RetryableQuery<T> Take(int count)
        => new RetryableQuery<T>(_query.Take(count), _options, _queryExecutor);

    /// <summary>Bypasses a specified number of elements from the start of the query.</summary>
    /// <param name="count">The number of elements to skip.</param>
    /// <returns>A new <see cref="RetryableQuery{T}"/> with the skip applied.</returns>
    public RetryableQuery<T> Skip(int count)
        => new RetryableQuery<T>(_query.Skip(count), _options, _queryExecutor);

    /// <summary>Returns distinct elements from the query.</summary>
    /// <returns>A new <see cref="RetryableQuery{T}"/> with distinct applied.</returns>
    public RetryableQuery<T> Distinct()
        => new RetryableQuery<T>(_query.Distinct(), _options, _queryExecutor);

    #endregion

    #region Terminal Methods with Retry

    /// <summary>
    /// Executes the query and returns all results as a list, retrying on 429 TooManyRequests.
    /// </summary>
    /// <returns>A list of all matching items.</returns>
    public async Task<List<T>> ReadAllAsync()
    {
        return await ExecuteWithRetryAsync(
            () => _queryExecutor.ReadAllAsync(_query),
            "ReadAllAsync"
        );
    }

    /// <summary>
    /// Executes the query and returns the first matching item or default, retrying on 429 TooManyRequests.
    /// </summary>
    /// <returns>The first matching item, or default if no match.</returns>
    public async Task<T?> FirstOrDefaultAsync()
    {
        return await ExecuteWithRetryAsync(
            () => _queryExecutor.FirstOrDefaultAsync(_query),
            "FirstOrDefaultAsync"
        );
    }

    /// <summary>
    /// Executes the query and returns the count of matching items, retrying on 429 TooManyRequests.
    /// </summary>
    /// <returns>The number of matching items.</returns>
    public async Task<int> CountAsync()
    {
        return await ExecuteWithRetryAsync(
            () => _queryExecutor.CountAsync(_query),
            "CountAsync"
        );
    }

    /// <summary>
    /// Returns the underlying <see cref="IQueryable{T}"/> for use with extension methods
    /// that are not directly supported by <see cref="RetryableQuery{T}"/>, such as PagedQueryAsync.
    /// </summary>
    /// <returns>The underlying IQueryable.</returns>
    public IQueryable<T> AsQueryable() => _query;

    #endregion

    #region Private Retry Logic

    /// <summary>
    /// Core retry logic for query terminal operations.
    /// Retries on 429 TooManyRequests using the server's RetryAfter header.
    /// Throws after exhausting all retry attempts.
    /// </summary>
    private async Task<TResult> ExecuteWithRetryAsync<TResult>(
        Func<Task<TResult>> operation,
        string operationDescription)
    {
        for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (CosmosException ce) when (ce.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt >= _options.MaxRetries)
                {
                    _options.LogRetry($"{operationDescription}: 429 TooManyRequests, exhausted {_options.MaxRetries} retries");
                    throw;
                }

                int waitTime = ce.RetryAfter.HasValue ? (int)ce.RetryAfter.Value.TotalMilliseconds : 1000;
                _options.LogRetry($"{operationDescription}: 429 TooManyRequests, retrying (attempt {attempt + 1}/{_options.MaxRetries}) after {waitTime}ms");
                await Task.Delay(waitTime);
            }
        }

        // Should not reach here — the loop either returns or throws
        throw new InvalidOperationException("Retry loop completed without returning or throwing.");
    }

    #endregion
}

/// <summary>
/// Extension methods for <see cref="RetryableQuery{T}"/> that require additional type constraints.
/// </summary>
public static class RetryableQueryExtensions
{
    /// <summary>
    /// Executes the query and returns the first matching item or a new instance of <typeparamref name="T"/>,
    /// retrying on 429 TooManyRequests.
    /// </summary>
    /// <typeparam name="T">The type of items in the query. Must have a parameterless constructor.</typeparam>
    /// <param name="retryableQuery">The retryable query to execute.</param>
    /// <returns>The first matching item, or a new instance of <typeparamref name="T"/> if no match.</returns>
    public static async Task<T> FirstOrNewAsync<T>(this RetryableQuery<T> retryableQuery) where T : new()
    {
        return await retryableQuery.FirstOrDefaultAsync() ?? new T();
    }
}
