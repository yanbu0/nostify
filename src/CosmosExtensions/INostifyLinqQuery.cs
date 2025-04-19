
using System;
using System.Linq;
using Microsoft.Azure.Cosmos;

namespace nostify;

public interface INostifyLinqQuery
{
    FeedIterator<T> GetFeedIterator<T>(IQueryable<T> query);
}