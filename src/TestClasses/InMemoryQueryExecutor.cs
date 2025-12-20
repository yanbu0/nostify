using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace nostify;

/// <summary>
/// In-memory implementation of IQueryExecutor for unit testing.
/// Executes LINQ queries against in-memory collections without requiring Cosmos DB.
/// </summary>
public class InMemoryQueryExecutor : IQueryExecutor
{
    /// <summary>
    /// Singleton instance for convenience.
    /// </summary>
    public static readonly InMemoryQueryExecutor Default = new InMemoryQueryExecutor();

    /// <inheritdoc />
    public Task<List<T>> ReadAllAsync<T>(IQueryable<T> query)
    {
        // Execute the query synchronously in memory
        return Task.FromResult(query.ToList());
    }

    /// <inheritdoc />
    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query)
    {
        return Task.FromResult(query.FirstOrDefault());
    }

    /// <inheritdoc />
    public Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new()
    {
        return Task.FromResult(query.FirstOrDefault() ?? new T());
    }
}
