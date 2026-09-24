
using System;
using System.Linq;
using Microsoft.Azure.Cosmos;

namespace nostify;

/// <summary>
/// Abstracts creation of Cosmos DB feed iterators from LINQ queries.
/// </summary>
public interface INostifyLinqQuery
{
    /// <summary>
    /// Creates a feed iterator for the specified query.
    /// </summary>
    /// <typeparam name="T">The type of item returned by the query.</typeparam>
    /// <param name="query">The Cosmos DB LINQ query.</param>
    /// <returns>A feed iterator for reading the query results.</returns>
    FeedIterator<T> GetFeedIterator<T>(IQueryable<T> query);
}