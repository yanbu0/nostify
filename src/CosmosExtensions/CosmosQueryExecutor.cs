using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace nostify;

/// <summary>
/// Default implementation of IQueryExecutor that uses Cosmos DB's FeedIterator
/// to execute LINQ queries. This is the production implementation.
/// </summary>
public class CosmosQueryExecutor : IQueryExecutor
{
    /// <summary>
    /// Singleton instance for convenience when DI is not available.
    /// </summary>
    public static readonly CosmosQueryExecutor Default = new CosmosQueryExecutor();

    /// <inheritdoc />
    public async Task<List<T>> ReadAllAsync<T>(IQueryable<T> query)
    {
        FeedIterator<T> fi = query.ToFeedIterator<T>();
        return await fi.ReadFeedIteratorAsync<T>();
    }

    /// <inheritdoc />
    public async Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query)
    {
        FeedIterator<T> fi = query.ToFeedIterator<T>();
        List<T> list = await fi.ReadFeedIteratorAsync<T>();
        return list.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new()
    {
        FeedIterator<T> fi = query.ToFeedIterator<T>();
        List<T> list = await fi.ReadFeedIteratorAsync<T>();
        return list.FirstOrDefault() ?? new T();
    }
}
