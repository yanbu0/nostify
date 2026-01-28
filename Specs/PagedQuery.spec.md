# PagedQuery System Specification

## Overview

The PagedQuery system provides LINQ-based paged queries against Cosmos DB with filtering, sorting, and tenant isolation. It enables efficient data retrieval for grid displays and paginated APIs.

## Key Components

- `PagedQueryExtensions` - Extension methods for paged queries
- `IPagedResult<T>` - Result container with items and count
- `ITableStateChange` - Query parameters interface
- `IQueryExecutor` - Query execution abstraction

## IPagedResult<T> Interface

```csharp
public interface IPagedResult<T>
{
    List<T> Items { get; }
    int TotalCount { get; }
}
```

| Property | Type | Description |
|----------|------|-------------|
| `Items` | `List<T>` | Items for current page |
| `TotalCount` | `int` | Total matching items count |

## ITableStateChange Interface

```csharp
public interface ITableStateChange
{
    int page { get; }
    int pageSize { get; }
    List<Filter>? filters { get; }
    string? sortColumn { get; }
    string? sortDirection { get; }
}
```

| Property | Type | Description |
|----------|------|-------------|
| `page` | `int` | Current page number (1-based) |
| `pageSize` | `int` | Items per page |
| `filters` | `List<Filter>?` | Filter criteria |
| `sortColumn` | `string?` | Column to sort by |
| `sortDirection` | `string?` | "asc" or "desc" |

## Filter Class

```csharp
public class Filter
{
    public string column { get; set; }
    public string value { get; set; }
    public string? @operator { get; set; }
}
```

## PagedQueryExtensions Methods

### Tenant-Filtered Query

```csharp
public static async Task<IPagedResult<T>> GetPagedAsync<T>(
    this Container container,
    ITableStateChange tableState,
    Guid tenantId,
    IQueryExecutor? executor = null
) where T : ITenantFilterable
```

Executes a paged query filtered by tenant ID.

### Partition-Filtered Query

```csharp
public static async Task<IPagedResult<T>> GetPagedAsync<T>(
    this Container container,
    ITableStateChange tableState,
    string partitionKey,
    IQueryExecutor? executor = null
) where T : class
```

Executes a paged query within a specific partition.

### On IQueryable

```csharp
public static async Task<IPagedResult<T>> GetPagedAsync<T>(
    this IQueryable<T> query,
    ITableStateChange tableState,
    IQueryExecutor? executor = null
) where T : class
```

Applies paging to an existing LINQ query.

### Pre-Sorted Query

```csharp
public static async Task<IPagedResult<T>> GetPagedAsync<T>(
    this IOrderedQueryable<T> orderedQuery,
    int page,
    int pageSize,
    IQueryExecutor? executor = null
) where T : class
```

Applies paging to an already-sorted query.

## Usage Examples

### Basic Paged Query

```csharp
// Get paged orders for a tenant
var tableState = new TableState
{
    page = 1,
    pageSize = 20,
    sortColumn = "createdAt",
    sortDirection = "desc"
};

var container = nostify.Repository.GetContainer("orders");
var result = await container.GetPagedAsync<Order>(tableState, tenantId);

Console.WriteLine($"Page 1 of {Math.Ceiling(result.TotalCount / 20.0)}");
foreach (var order in result.Items)
{
    Console.WriteLine($"Order: {order.id}");
}
```

### With Filtering

```csharp
var tableState = new TableState
{
    page = 1,
    pageSize = 10,
    filters = new List<Filter>
    {
        new Filter { column = "status", value = "Pending" },
        new Filter { column = "total", value = "100", @operator = ">=" }
    },
    sortColumn = "total",
    sortDirection = "desc"
};

var result = await container.GetPagedAsync<Order>(tableState, tenantId);
```

### On Existing Query

```csharp
// Build custom query
var query = container.GetItemLinqQueryable<Order>()
    .Where(o => o.tenantId == tenantId)
    .Where(o => o.status != OrderStatus.Cancelled);

// Apply paging
var result = await query.GetPagedAsync(tableState);
```

### Pre-Sorted Query

```csharp
// Custom sorting
var orderedQuery = container.GetItemLinqQueryable<Order>()
    .Where(o => o.tenantId == tenantId)
    .OrderByDescending(o => o.total)
    .ThenBy(o => o.createdAt);

// Apply paging (no additional sorting)
var result = await orderedQuery.GetPagedAsync(page: 1, pageSize: 25);
```

## Filter Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `null` or `==` | Equals | `{ column: "status", value: "Active" }` |
| `!=` | Not equals | `{ column: "status", value: "Deleted", operator: "!=" }` |
| `>` | Greater than | `{ column: "total", value: "100", operator: ">" }` |
| `>=` | Greater or equal | `{ column: "total", value: "100", operator: ">=" }` |
| `<` | Less than | `{ column: "count", value: "10", operator: "<" }` |
| `<=` | Less or equal | `{ column: "count", value: "10", operator: "<=" }` |
| `contains` | String contains | `{ column: "name", value: "John", operator: "contains" }` |
| `startswith` | String starts with | `{ column: "name", value: "J", operator: "startswith" }` |

## Type Conversion

The filter system automatically converts values:

| Target Type | Conversion |
|-------------|------------|
| `Guid` | `Guid.Parse()` |
| `int` | `int.Parse()` |
| `long` | `long.Parse()` |
| `decimal` | `decimal.Parse()` |
| `double` | `double.Parse()` |
| `bool` | `bool.Parse()` |
| `DateTime` | `DateTime.Parse()` |
| `Nullable<T>` | Underlying type conversion |
| `Enum` | `Enum.Parse()` |

## Security

- **Column Validation**: Column names are validated with regex `^[a-zA-Z0-9_]+$`
- **SQL Injection Prevention**: Uses parameterized LINQ expressions
- **Tenant Isolation**: `ITenantFilterable` queries automatically filter by tenant

## Testing Support

```csharp
// Use custom executor for unit tests
public class MockQueryExecutor : IQueryExecutor
{
    private readonly List<object> _data;
    
    public Task<List<T>> ToListAsync<T>(IQueryable<T> query) 
        => Task.FromResult(query.ToList());
    
    public Task<int> CountAsync<T>(IQueryable<T> query) 
        => Task.FromResult(query.Count());
}

// In tests
var result = await container.GetPagedAsync<Order>(
    tableState, 
    tenantId, 
    new MockQueryExecutor()
);
```

## Best Practices

1. **Use Tenant Filtering** - Always filter by tenant in multi-tenant apps
2. **Reasonable Page Sizes** - Keep page sizes between 10-100
3. **Index Sort Columns** - Ensure frequently sorted columns are indexed
4. **Validate Input** - Validate page numbers and filter values
5. **Use Projections** - Select only needed fields for large datasets

## Related Types

- [IQueryExecutor](IQueryExecutor.spec.md) - Query execution interface
- [CosmosQueryExecutor](CosmosQueryExecutor.spec.md) - Cosmos DB implementation
- [ITenantFilterable](ITenantFilterable.spec.md) - Tenant filtering interface
