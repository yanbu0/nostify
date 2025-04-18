
using System;
using System.Linq;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace nostify;

public class NostifyLinqQuery : INostifyLinqQuery
{
    public FeedIterator<T> GetFeedIterator<T>(IQueryable<T> query)
    {

        return query.ToFeedIterator();
    }
}