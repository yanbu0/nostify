
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace nostify;

/// <summary>
/// Provides extension methods for creating filtered queries on Cosmos DB containers.
/// </summary>
public static class PagedQueryExtensions
{
    /// <summary>
    /// Executes a paged query against a Cosmos DB container with tenant filtering.
    /// </summary>
    /// <typeparam name="T">The type of items to query that implements ITenantFilterable.</typeparam>
    /// <param name="container">The Cosmos DB container to query.</param>
    /// <param name="tableState">The table state containing pagination, filtering, and sorting information.</param>
    /// <param name="tenantId">The tenant ID to filter results by.</param>
    /// <param name="queryExecutor">Optional query executor for testability. Defaults to CosmosQueryExecutor.</param>
    /// <returns>A paged result containing the items for the current page and the total count of matching items.</returns>
    public static async Task<IPagedResult<T>> PagedQueryAsync<T>(this Container container, ITableStateChange tableState, Guid tenantId, IQueryExecutor? queryExecutor = null) where T : class, ITenantFilterable
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }
        return await container.PagedQueryAsync<T>(tableState, "tenantId", tenantId, queryExecutor);
    }

    /// <summary>
    /// Executes a paged query against a Cosmos DB container with filtering, sorting, and pagination.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="container">The Cosmos DB container to query.</param>
    /// <param name="tableState">The table state containing pagination, filtering, and sorting information.</param>
    /// <param name="partitionKeyName">The name of the partition key property to filter by.</param>
    /// <param name="partitionKeyValue">The partition key value to filter results by.</param>
    /// <param name="queryExecutor">Optional query executor for testability. Defaults to CosmosQueryExecutor.</param>
    /// <returns>A paged result containing the items for the current page and the total count of matching items.</returns>
    public static async Task<IPagedResult<T>> PagedQueryAsync<T>(this Container container, ITableStateChange tableState, string partitionKeyName, Guid partitionKeyValue, IQueryExecutor? queryExecutor = null) where T : class
    {
        var query = container.GetItemLinqQueryable<T>();
        return await query.PagedQueryAsync(tableState, partitionKeyName, partitionKeyValue, queryExecutor);
    }

    /// <summary>
    /// Executes a paged query against an IQueryable with tenant filtering, sorting, and pagination using LINQ.
    /// Use this overload when you have already constructed an IQueryable from a Cosmos DB container.
    /// </summary>
    /// <typeparam name="T">The type of items to query that implements ITenantFilterable.</typeparam>
    /// <param name="query">The IQueryable to page.</param>
    /// <param name="tableState">The table state containing pagination, filtering, and sorting information.</param>
    /// <param name="tenantId">The tenant ID to filter results by.</param>
    /// <param name="queryExecutor">Optional query executor for testability. Defaults to CosmosQueryExecutor.</param>
    /// <returns>A paged result containing the items for the current page and the total count of matching items.</returns>
    public static async Task<IPagedResult<T>> PagedQueryAsync<T>(this IQueryable<T> query, ITableStateChange tableState, Guid tenantId, IQueryExecutor? queryExecutor = null) where T : class, ITenantFilterable
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }
        return await query.PagedQueryAsync(tableState, "tenantId", tenantId, queryExecutor);
    }

    /// <summary>
    /// Executes a paged query against an IQueryable with partition key filtering, sorting, and pagination using LINQ.
    /// Use this overload when you have already constructed an IQueryable from a Cosmos DB container.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="query">The IQueryable to page.</param>
    /// <param name="tableState">The table state containing pagination, filtering, and sorting information.</param>
    /// <param name="partitionKeyName">The name of the partition key property to filter by.</param>
    /// <param name="partitionKeyValue">The partition key value to filter results by.</param>
    /// <param name="queryExecutor">Optional query executor for testability. Defaults to CosmosQueryExecutor.</param>
    /// <returns>A paged result containing the items for the current page and the total count of matching items.</returns>
    public static async Task<IPagedResult<T>> PagedQueryAsync<T>(this IQueryable<T> query, ITableStateChange tableState, string partitionKeyName, Guid partitionKeyValue, IQueryExecutor? queryExecutor = null) where T : class
    {
        var executor = queryExecutor ?? CosmosQueryExecutor.Default;

        // Validate pagination parameters
        ValidatePaginationParameters(tableState);

        // Validate partition key name to prevent injection
        if (!System.Text.RegularExpressions.Regex.IsMatch(partitionKeyName, @"^[a-zA-Z0-9_]+$"))
        {
            throw new ArgumentException($"Invalid partition key name: {partitionKeyName}");
        }

        // Build partition key filter expression: x => x.PartitionKeyName == partitionKeyValue
        var parameter = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(parameter, partitionKeyName);
        var constant = Expression.Constant(partitionKeyValue);
        var equality = Expression.Equal(property, constant);
        var lambda = Expression.Lambda<Func<T, bool>>(equality, parameter);

        // Apply partition key filter
        var partitionFilteredQuery = query.Where(lambda);

        // Apply additional filters using LINQ Where clauses
        var filteredQuery = ApplyFilters(partitionFilteredQuery, tableState.filters);

        // Get total count before pagination
        int totalCount = await executor.CountAsync(filteredQuery);

        // Apply sorting
        var sortedQuery = ApplySorting(filteredQuery, tableState.sortColumn, tableState.sortDirection);

        // Apply pagination
        var offset = (tableState.page - 1) * tableState.pageSize;
        var pagedQuery = sortedQuery.Skip(offset).Take(tableState.pageSize);

        // Execute the query
        var items = await executor.ReadAllAsync(pagedQuery);

        return new PagedResult<T>()
        {
            items = items,
            totalCount = totalCount
        };
    }

    /// <summary>
    /// Executes a paged query against an IOrderedQueryable with tenant filtering and pagination.
    /// Use this overload when you have already applied ordering to your query.
    /// </summary>
    /// <typeparam name="T">The type of items to query that implements ITenantFilterable.</typeparam>
    /// <param name="query">The IOrderedQueryable to page.</param>
    /// <param name="tableState">The table state containing pagination information. Sorting from tableState is ignored since the query is already ordered.</param>
    /// <param name="tenantId">The tenant ID to filter results by.</param>
    /// <param name="queryExecutor">Optional query executor for testability. Defaults to CosmosQueryExecutor.</param>
    /// <returns>A paged result containing the items for the current page and the total count of matching items.</returns>
    public static async Task<IPagedResult<T>> PagedQueryAsync<T>(this IOrderedQueryable<T> query, ITableStateChange tableState, Guid tenantId, IQueryExecutor? queryExecutor = null) where T : class, ITenantFilterable
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }
        return await query.PagedQueryAsync(tableState, "tenantId", tenantId, queryExecutor);
    }

    /// <summary>
    /// Executes a paged query against an IOrderedQueryable with partition key filtering and pagination.
    /// Use this overload when you have already applied ordering to your query.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="query">The IOrderedQueryable to page.</param>
    /// <param name="tableState">The table state containing pagination information. Sorting from tableState is ignored since the query is already ordered.</param>
    /// <param name="partitionKeyName">The name of the partition key property to filter by.</param>
    /// <param name="partitionKeyValue">The partition key value to filter results by.</param>
    /// <param name="queryExecutor">Optional query executor for testability. Defaults to CosmosQueryExecutor.</param>
    /// <returns>A paged result containing the items for the current page and the total count of matching items.</returns>
    public static async Task<IPagedResult<T>> PagedQueryAsync<T>(this IOrderedQueryable<T> query, ITableStateChange tableState, string partitionKeyName, Guid partitionKeyValue, IQueryExecutor? queryExecutor = null) where T : class
    {
        var executor = queryExecutor ?? CosmosQueryExecutor.Default;

        // Validate pagination parameters
        ValidatePaginationParameters(tableState);

        // Validate partition key name to prevent injection
        if (!System.Text.RegularExpressions.Regex.IsMatch(partitionKeyName, @"^[a-zA-Z0-9_]+$"))
        {
            throw new ArgumentException($"Invalid partition key name: {partitionKeyName}");
        }

        // Validate sort column name (even though we ignore it for IOrderedQueryable, validate for consistency)
        if (!string.IsNullOrEmpty(tableState.sortColumn) && !System.Text.RegularExpressions.Regex.IsMatch(tableState.sortColumn, @"^[a-zA-Z0-9_]+$"))
        {
            throw new ArgumentException($"Invalid sort column name: {tableState.sortColumn}");
        }

        // Build partition key filter expression: x => x.PartitionKeyName == partitionKeyValue
        var parameter = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(parameter, partitionKeyName);
        var constant = Expression.Constant(partitionKeyValue);
        var equality = Expression.Equal(property, constant);
        var lambda = Expression.Lambda<Func<T, bool>>(equality, parameter);

        // Apply partition key filter
        var partitionFilteredQuery = query.Where(lambda);

        // Apply additional filters using LINQ Where clauses
        var filteredQuery = ApplyFilters(partitionFilteredQuery, tableState.filters);

        // Get total count before pagination
        int totalCount = await executor.CountAsync(filteredQuery);

        // Apply pagination (sorting already applied via IOrderedQueryable)
        var offset = (tableState.page - 1) * tableState.pageSize;
        var pagedQuery = filteredQuery.Skip(offset).Take(tableState.pageSize);

        // Execute the query
        var items = await executor.ReadAllAsync(pagedQuery);

        return new PagedResult<T>()
        {
            items = items,
            totalCount = totalCount
        };
    }

    /// <summary>
    /// Validates pagination parameters to ensure they are within valid ranges.
    /// </summary>
    /// <param name="tableState">The table state containing pagination parameters.</param>
    /// <exception cref="ArgumentNullException">Thrown when tableState is null.</exception>
    /// <exception cref="ArgumentException">Thrown when page is less than 1, pageSize is less than 1, or the combination would cause an overflow.</exception>
    private static void ValidatePaginationParameters(ITableStateChange tableState)
    {
        if (tableState == null)
        {
            throw new ArgumentNullException(nameof(tableState), "Table state cannot be null.");
        }
        if (tableState.page < 1)
        {
            throw new ArgumentException($"Page must be greater than or equal to 1. Received: {tableState.page}", "tableState.page");
        }
        if (tableState.pageSize < 1)
        {
            throw new ArgumentException($"Page size must be greater than or equal to 1. Received: {tableState.pageSize}", "tableState.pageSize");
        }
        
        // Check for potential overflow in offset calculation: (page - 1) * pageSize
        try
        {
            _ = checked((tableState.page - 1) * tableState.pageSize);
        }
        catch (OverflowException)
        {
            throw new ArgumentException(
                $"The combination of page ({tableState.page}) and pageSize ({tableState.pageSize}) would cause an arithmetic overflow.",
                "tableState");
        }
    }

    /// <summary>
    /// Applies filters to an IQueryable using reflection-based property access.
    /// </summary>
    private static IQueryable<T> ApplyFilters<T>(IQueryable<T> query, List<KeyValuePair<string, string>>? filters) where T : class
    {
        if (filters == null || filters.Count == 0)
        {
            return query;
        }

        foreach (var filter in filters)
        {
            // Validate column name to prevent injection
            if (!System.Text.RegularExpressions.Regex.IsMatch(filter.Key, @"^[a-zA-Z0-9_]+$"))
            {
                throw new ArgumentException($"Invalid filter column name: {filter.Key}");
            }

            // Build expression: x => x.PropertyName == filterValue
            var parameter = Expression.Parameter(typeof(T), "x");
            var property = Expression.Property(parameter, filter.Key);
            
            // Convert filter value to the property type
            var propertyType = typeof(T).GetProperty(filter.Key)?.PropertyType ?? typeof(string);
            object? convertedValue = ConvertFilterValue(filter.Key, filter.Value, propertyType);
            
            var constant = Expression.Constant(convertedValue, propertyType);
            var equality = Expression.Equal(property, constant);
            var lambda = Expression.Lambda<Func<T, bool>>(equality, parameter);

            query = query.Where(lambda);
        }

        return query;
    }

    /// <summary>
    /// Converts a string filter value to the target property type.
    /// </summary>
    /// <param name="filterKey">The filter key name for error messages.</param>
    /// <param name="value">The string value to convert.</param>
    /// <param name="targetType">The target type to convert to.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown when the value cannot be converted to the target type.</exception>
    private static object? ConvertFilterValue(string filterKey, string value, Type targetType)
    {
        try
        {
            if (targetType == typeof(string))
            {
                return value;
            }
            if (targetType == typeof(Guid))
            {
                return Guid.Parse(value);
            }
            if (targetType == typeof(int))
            {
                return int.Parse(value);
            }
            if (targetType == typeof(long))
            {
                return long.Parse(value);
            }
            if (targetType == typeof(decimal))
            {
                return decimal.Parse(value);
            }
            if (targetType == typeof(double))
            {
                return double.Parse(value);
            }
            if (targetType == typeof(bool))
            {
                return bool.Parse(value);
            }
            if (targetType == typeof(DateTime))
            {
                return DateTime.Parse(value);
            }
            if (Nullable.GetUnderlyingType(targetType) != null)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return null;
                }
                return ConvertFilterValue(filterKey, value, Nullable.GetUnderlyingType(targetType)!);
            }

            return Convert.ChangeType(value, targetType);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                $"Filter '{filterKey}' has invalid value '{value}'. Expected type: {targetType.Name}.",
                filterKey,
                ex);
        }
        catch (InvalidCastException ex)
        {
            throw new ArgumentException(
                $"Filter '{filterKey}' value '{value}' cannot be converted to type {targetType.Name}.",
                filterKey,
                ex);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException(
                $"Filter '{filterKey}' value '{value}' is out of range for type {targetType.Name}.",
                filterKey,
                ex);
        }
    }

    /// <summary>
    /// Applies sorting to an IQueryable using reflection-based property access.
    /// </summary>
    private static IQueryable<T> ApplySorting<T>(IQueryable<T> query, string? sortColumn, string? sortDirection) where T : class
    {
        if (string.IsNullOrEmpty(sortColumn))
        {
            return query;
        }

        // Validate column name to prevent injection
        if (!System.Text.RegularExpressions.Regex.IsMatch(sortColumn, @"^[a-zA-Z0-9_]+$"))
        {
            throw new ArgumentException($"Invalid sort column name: {sortColumn}");
        }

        // Build expression: x => x.PropertyName
        var parameter = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(parameter, sortColumn);
        var lambda = Expression.Lambda(property, parameter);

        // Determine sort method name
        var methodName = sortDirection?.ToLower() == "desc" ? "OrderByDescending" : "OrderBy";

        // Create the OrderBy/OrderByDescending call
        var resultExpression = Expression.Call(
            typeof(Queryable),
            methodName,
            new Type[] { typeof(T), property.Type },
            query.Expression,
            Expression.Quote(lambda));

        return query.Provider.CreateQuery<T>(resultExpression);
    }
}

/// <summary>
/// Represents the results of a paged query operation.
/// </summary>
public interface IPagedResult<T>
{
    /// <summary>
    /// Gets or sets the list of items for the current page.
    /// </summary>
    public List<T> items { get; set; }
    
    /// <summary>
    /// Gets or sets the total count of items matching the query criteria.
    /// </summary>
    public int totalCount { get; set; }
}

/// <summary>
/// Concrete implementation of IPagedResult that contains paged query results.
/// </summary>
/// <typeparam name="T">The type of items in the paged result.</typeparam>
public class PagedResult<T> : IPagedResult<T>
{
    /// <inheritdoc/>
    public List<T> items { get; set; }
    /// <inheritdoc/>
    public int totalCount { get; set; }
}

/// <summary>
/// Represents the state of a table including pagination, filtering, and sorting information.
/// </summary>
public interface ITableStateChange
{
    /// <summary>
    /// Gets or sets the current page number.
    /// </summary>
    public int page { get; set; }
    
    /// <summary>
    /// Gets or sets the number of items per page.
    /// </summary>
    public int pageSize { get; set; }
    
    /// <summary>
    /// Gets or sets the filter criteria as a list of key-value pairs.
    /// </summary>
    public List<KeyValuePair<string, string>>? filters { get; set; }
    
    /// <summary>
    /// Gets or sets the column to sort by.
    /// </summary>
    public string? sortColumn { get; set; }
    
    /// <summary>
    /// Gets or sets the sort direction (ascending or descending).
    /// </summary>
    public string? sortDirection { get; set; }
}

/// <summary>
/// Represents the state of a table including pagination, filtering, and sorting information.
/// </summary>
public class TableStateChange : ITableStateChange
{
    /// <inheritdoc/>
    public int page { get; set; }
    /// <inheritdoc/>
    public int pageSize { get; set; }
    /// <inheritdoc/>
    public List<KeyValuePair<string, string>>? filters { get; set; }
    /// <inheritdoc/>
    public string? sortColumn { get; set; }
    /// <inheritdoc/>
    public string? sortDirection { get; set; }
}