

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace nostify;

/// <summary>
/// Extension methods for creating filtered queries on Cosmos DB containers.
/// </summary>
public static class FilteredQueryExtensions
{
    /// <summary>
    /// Creates a filtered queryable for items in a Cosmos DB container within a specific tenant partition.
    /// </summary>
    /// <typeparam name="T">The type of items to query that implements ITenantFilterable.</typeparam>
    /// <param name="container">The Cosmos DB container to query.</param>
    /// <param name="tenantId">The tenant ID to use as the partition key.</param>
    /// <param name="filterExpression">Optional filter expression to apply to the query.</param>
    /// <returns>An IQueryable of type T filtered by the specified criteria within the tenant partition.</returns>
    public static IQueryable<T> FilteredQuery<T>(this Container container, Guid tenantId, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject, ITenantFilterable
    {
        return FilteredQuery(container, tenantId.ToPartitionKey(), filterExpression);
    }

    /// <summary>
    /// Creates a filtered queryable for items in a Cosmos DB container within a specific partition.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="container">The Cosmos DB container to query.</param>
    /// <param name="partitionKeyValue">The partition key value to filter by.</param>
    /// <param name="filterExpression">Optional filter expression to apply to the query.</param>
    /// <returns>An IQueryable of type T filtered by the specified criteria within the partition.</returns>
    public static IQueryable<T> FilteredQuery<T>(this Container container, string partitionKeyValue, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject
    {
        return FilteredQuery(container, new PartitionKey(partitionKeyValue), filterExpression);
    }

    /// <summary>
    /// Creates a filtered queryable for items in a Cosmos DB container within a specific partition.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="container">The Cosmos DB container to query.</param>
    /// <param name="partitionKey">The partition key to filter by.</param>
    /// <param name="filterExpression">Optional filter expression to apply to the query.</param>
    /// <returns>An IQueryable of type T filtered by the specified criteria.</returns>
    public static IQueryable<T> FilteredQuery<T>(this Container container, PartitionKey partitionKey, Expression<Func<T, bool>>? filterExpression = null) where T : NostifyObject
    {
        var query = container.GetItemLinqQueryable<T>(requestOptions: new QueryRequestOptions { 
                PartitionKey = partitionKey
            })
            .AsQueryable();

        if (filterExpression != null)
        {
            query = query.Where(filterExpression);
        }
        return query;
    }
}