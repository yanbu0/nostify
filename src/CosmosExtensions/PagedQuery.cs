
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

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
    /// <returns>A paged result containing the items for the current page and the total count of matching items.</returns>
    public static async Task<IPagedResult<T>> PagedQueryAsync<T>(this Container container, ITableStateChange tableState, Guid tenantId) where T : class, ITenantFilterable
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }
        return await container.PagedQueryAsync<T>(tableState, "tenantId", tenantId);
    }

    /// <summary>
    /// Executes a paged query against a Cosmos DB container with filtering, sorting, and pagination.
    /// </summary>
    /// <typeparam name="T">The type of items to query.</typeparam>
    /// <param name="container">The Cosmos DB container to query.</param>
    /// <param name="tableState">The table state containing pagination, filtering, and sorting information.</param>
    /// <param name="partitionKeyName">The name of the partition key property to filter by.</param>
    /// <param name="partitionKeyValue">The partition key value to filter results by.</param>
    /// <returns>A tuple containing the items for the current page and the total count of matching items.</returns>
    public static async Task<IPagedResult<T>> PagedQueryAsync<T>(this Container container, ITableStateChange tableState, string partitionKeyName, Guid partitionKeyValue) where T : class
    {
        
        // Build count query with parameters
        var countQueryText = $"SELECT VALUE COUNT(1) FROM c WHERE c.{partitionKeyName} = @partitionKeyValue";
        var countQueryDefinition = new QueryDefinition(countQueryText)
            .WithParameter("@partitionKeyValue", partitionKeyValue);
        
        // Add filters to count query
        if (tableState.filters != null && tableState.filters.Count > 0)
        {
            for (int i = 0; i < tableState.filters.Count; i++)
            {
                var filter = tableState.filters[i];
                // Validate column name to prevent injection (only allow alphanumeric and underscore)
                if (!System.Text.RegularExpressions.Regex.IsMatch(filter.Key, @"^[a-zA-Z0-9_]+$"))
                {
                    throw new ArgumentException($"Invalid filter column name: {filter.Key}");
                }
                
                countQueryText += $" AND c.{filter.Key} = @filterValue{i}";
                countQueryDefinition = new QueryDefinition(countQueryText)
                    .WithParameter("@partitionKeyValue", partitionKeyValue);
                
                // Re-add all previous filter parameters
                for (int j = 0; j <= i; j++)
                {
                    countQueryDefinition = countQueryDefinition.WithParameter($"@filterValue{j}", tableState.filters[j].Value);
                }
            }
        }
        
        // Execute count query
        var countIterator = container.GetItemQueryIterator<int>(countQueryDefinition);
        int totalCount = 0;
        while (countIterator.HasMoreResults)
        {
            var response = await countIterator.ReadNextAsync();
            totalCount = response.FirstOrDefault();
            break; // COUNT query returns single result
        }
        
        // Build main query with parameters
        // Get properties of T and build SELECT clause in case of DTO
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p => $"c.{p.Name}")
            .ToList();
        
        var selectClause = properties.Any() 
            ? $"SELECT {string.Join(", ", properties)} FROM c" 
            : "SELECT * FROM c";
        
        var queryText = $"{selectClause} WHERE c.{partitionKeyName} = @partitionKeyValue";
        var queryDefinition = new QueryDefinition(queryText)
            .WithParameter("@partitionKeyValue", partitionKeyValue);
        
        // Add filters if present (Note: column name cannot be parameterized, only values)
        if (tableState.filters != null && tableState.filters.Count > 0)
        {
            for (int i = 0; i < tableState.filters.Count; i++)
            {
                var filter = tableState.filters[i];
                // Validate column name to prevent injection (only allow alphanumeric and underscore)
                if (!System.Text.RegularExpressions.Regex.IsMatch(filter.Key, @"^[a-zA-Z0-9_]+$"))
                {
                    throw new ArgumentException($"Invalid filter column name: {filter.Key}");
                }
                
                queryText += $" AND c.{filter.Key} = @filterValue{i}";
            }
            
            // Rebuild query definition with all parameters
            queryDefinition = new QueryDefinition(queryText)
                .WithParameter("@partitionKeyValue", partitionKeyValue);
            
            for (int i = 0; i < tableState.filters.Count; i++)
            {
                queryDefinition = queryDefinition.WithParameter($"@filterValue{i}", tableState.filters[i].Value);
            }
        }
        
        // Add sorting (column name validated to prevent injection)
        if (!string.IsNullOrEmpty(tableState.sortColumn))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(tableState.sortColumn, @"^[a-zA-Z0-9_]+$"))
            {
                throw new ArgumentException($"Invalid sort column name: {tableState.sortColumn}");
            }
            
            var sortDirection = tableState.sortDirection?.ToLower() == "desc" ? "DESC" : "ASC";
            queryText += $" ORDER BY c.{tableState.sortColumn} {sortDirection}";
            
            // Rebuild query definition with sorting
            queryDefinition = new QueryDefinition(queryText)
                .WithParameter("@partitionKeyValue", partitionKeyValue);
            
            if (tableState.filters != null && tableState.filters.Count > 0)
            {
                for (int i = 0; i < tableState.filters.Count; i++)
                {
                    queryDefinition = queryDefinition.WithParameter($"@filterValue{i}", tableState.filters[i].Value);
                }
            }
        }
        
        // Add pagination
        var offset = (tableState.page - 1) * tableState.pageSize;
        queryText += " OFFSET @offset LIMIT @limit";
        
        // Rebuild query definition with pagination
        queryDefinition = new QueryDefinition(queryText)
            .WithParameter("@partitionKeyValue", partitionKeyValue)
            .WithParameter("@offset", offset)
            .WithParameter("@limit", tableState.pageSize);
        
        if (tableState.filters != null && tableState.filters.Count > 0)
        {
            for (int i = 0; i < tableState.filters.Count; i++)
            {
                queryDefinition = queryDefinition.WithParameter($"@filterValue{i}", tableState.filters[i].Value);
            }
        }
        
        // Execute main query
        var items = new List<T>();
        var iterator = container.GetItemQueryIterator<T>(queryDefinition);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            items.AddRange(response);
        }

        return new PagedResult<T>() {
            items = items,
            totalCount = totalCount 
        };
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