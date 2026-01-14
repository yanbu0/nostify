using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace nostify;

/// <summary>
/// Interface for executing LINQ queries against a data source.
/// This abstraction allows for mocking in unit tests while using the real
/// Cosmos DB implementation in production.
/// </summary>
public interface IQueryExecutor
{
    /// <summary>
    /// Executes a query and returns all results as a list.
    /// </summary>
    /// <typeparam name="T">The type of items in the query result</typeparam>
    /// <param name="query">The LINQ query to execute</param>
    /// <returns>A list of all matching items</returns>
    Task<List<T>> ReadAllAsync<T>(IQueryable<T> query);

    /// <summary>
    /// Executes a query and returns the first matching item or default.
    /// </summary>
    /// <typeparam name="T">The type of items in the query result</typeparam>
    /// <param name="query">The LINQ query to execute</param>
    /// <returns>The first matching item or default(T)</returns>
    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query);

    /// <summary>
    /// Executes a query and returns the first matching item or a new instance.
    /// </summary>
    /// <typeparam name="T">The type of items in the query result</typeparam>
    /// <param name="query">The LINQ query to execute</param>
    /// <returns>The first matching item or a new instance of T</returns>
    Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new();

    /// <summary>
    /// Executes a query and returns the count of matching items.
    /// </summary>
    /// <typeparam name="T">The type of items in the query result</typeparam>
    /// <param name="query">The LINQ query to execute</param>
    /// <returns>The count of matching items</returns>
    Task<int> CountAsync<T>(IQueryable<T> query);
}
