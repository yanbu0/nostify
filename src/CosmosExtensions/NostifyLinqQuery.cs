
using System;
using System.Linq;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace nostify;

/// <summary>
/// Creates Cosmos DB feed iterators from LINQ queries.
/// </summary>
public class NostifyLinqQuery : INostifyLinqQuery
{
    /// <inheritdoc />
    public FeedIterator<T> GetFeedIterator<T>(IQueryable<T> query)
    {

        return query.ToFeedIterator();
    }
}