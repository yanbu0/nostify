# PagedQuery System Specification

## Overview

The PagedQuery system provides LINQ-based paged queries against Cosmos DB with equality filtering, sorting, tenant isolation, and custom `Guid` partition-key filtering. It is intended for paginated APIs and data grids that need both the current page and the total number of matching records.

All public query overloads require either a tenant ID or an explicit partition-key property and value. Query execution is abstracted through `IQueryExecutor`, allowing production code to use Cosmos DB and tests to execute queries in memory.

## Key Components

- `PagedQueryExtensions` — extension methods that filter, count, sort, and page queries.
- `IPagedResult<T>` — result contract containing the current page and total matching count.
- `PagedResult<T>` — concrete result implementation.
- `ITableStateChange` — pagination, equality-filter, and sorting contract.
- `TableStateChange` — concrete table-state implementation.
- `IQueryExecutor` — query execution abstraction.
- `CosmosQueryExecutor` — default Cosmos DB query executor.
- `InMemoryQueryExecutor` — executor intended for unit tests.

## Result Contract

```csharp
public interface IPagedResult<T>
{
    List<T> items { get; set; }
    int totalCount { get; set; }
}

public class PagedResult<T> : IPagedResult<T>
{
    public List<T> items { get; set; }
    public int totalCount { get; set; }
}
```

| Property | Type | Description |
|---|---|---|
| `items` | `List<T>` | Items returned for the requested page. |
| `totalCount` | `int` | Number of records matching the partition and additional filters before pagination. |

## Table-State Contract

```csharp
public interface ITableStateChange
{
    int page { get; set; }
    int pageSize { get; set; }
    List<KeyValuePair<string, string>>? filters { get; set; }
    string? sortColumn { get; set; }
    string? sortDirection { get; set; }
}

public class TableStateChange : ITableStateChange
{
    public int page { get; set; }
    public int pageSize { get; set; }
    public List<KeyValuePair<string, string>>? filters { get; set; }
    public string? sortColumn { get; set; }
    public string? sortDirection { get; set; }
}
```

| Property | Type | Description |
|---|---|---|
| `page` | `int` | One-based page number. Must be at least 1. |
| `pageSize` | `int` | Number of items per page. Must be at least 1. |
| `filters` | `List<KeyValuePair<string, string>>?` | Optional equality filters. The key is a property name and the value is converted to that property's type. Multiple filters use AND semantics. |
| `sortColumn` | `string?` | Optional property name used for sorting. |
| `sortDirection` | `string?` | `"desc"`, case-insensitively, selects descending order. Every other value, including `null`, selects ascending order. |

## Public Extension Methods

### Container Query by Tenant

```csharp
public static Task<IPagedResult<T>> PagedQueryAsync<T>(
    this Container container,
    ITableStateChange tableState,
    Guid tenantId,
    IQueryExecutor? queryExecutor = null)
    where T : class, ITenantFilterable;
```

Filters the container query by the `tenantId` property. An empty tenant ID throws `ArgumentException`.

### Container Query by Custom Partition Key

```csharp
public static Task<IPagedResult<T>> PagedQueryAsync<T>(
    this Container container,
    ITableStateChange tableState,
    string partitionKeyName,
    Guid partitionKeyValue,
    IQueryExecutor? queryExecutor = null)
    where T : class;
```

Filters the container query by the named `Guid` property before applying additional filters, sorting, and pagination.

### Existing IQueryable by Tenant

```csharp
public static Task<IPagedResult<T>> PagedQueryAsync<T>(
    this IQueryable<T> query,
    ITableStateChange tableState,
    Guid tenantId,
    IQueryExecutor? queryExecutor = null)
    where T : class, ITenantFilterable;
```

Applies tenant filtering and pagination to an existing query.

### Existing IQueryable by Custom Partition Key

```csharp
public static Task<IPagedResult<T>> PagedQueryAsync<T>(
    this IQueryable<T> query,
    ITableStateChange tableState,
    string partitionKeyName,
    Guid partitionKeyValue,
    IQueryExecutor? queryExecutor = null)
    where T : class;
```

Applies custom partition filtering and pagination to an existing query.

### Pre-Sorted IOrderedQueryable by Tenant

```csharp
public static Task<IPagedResult<T>> PagedQueryAsync<T>(
    this IOrderedQueryable<T> query,
    ITableStateChange tableState,
    Guid tenantId,
    IQueryExecutor? queryExecutor = null)
    where T : class, ITenantFilterable;
```

Preserves the ordering already applied to the query. `tableState.sortColumn` is validated when present but is not used to replace the existing ordering.

### Pre-Sorted IOrderedQueryable by Custom Partition Key

```csharp
public static Task<IPagedResult<T>> PagedQueryAsync<T>(
    this IOrderedQueryable<T> query,
    ITableStateChange tableState,
    string partitionKeyName,
    Guid partitionKeyValue,
    IQueryExecutor? queryExecutor = null)
    where T : class;
```

Preserves existing ordering while applying custom partition filtering, additional equality filters, counting, and pagination.

## Processing Order

For a normal `IQueryable<T>`, processing occurs in this order:

1. Validate `tableState`, `page`, and `pageSize`.
2. Validate the partition-key property name.
3. Filter by tenant or custom partition-key value.
4. Apply all equality filters in `tableState.filters`.
5. Count all matching records.
6. Apply optional sorting.
7. Apply `Skip((page - 1) * pageSize)` and `Take(pageSize)`.
8. Execute the paged query and return `PagedResult<T>`.

For an `IOrderedQueryable<T>`, the same process applies except that the existing ordering is preserved instead of applying table-state sorting.

## Usage Examples

### Tenant-Scoped Query

```csharp
var tableState = new TableStateChange
{
    page = 1,
    pageSize = 20,
    sortColumn = "createdAt",
    sortDirection = "desc",
    filters = new List<KeyValuePair<string, string>>
    {
        new("status", "Pending")
    }
};

IPagedResult<Order> result = await container.PagedQueryAsync<Order>(
    tableState,
    tenantId);

Console.WriteLine($"Matching orders: {result.totalCount}");
foreach (var order in result.items)
{
    Console.WriteLine($"Order: {order.id}");
}
```

The queried type must implement `ITenantFilterable` for this overload.

### Custom Partition-Key Query

```csharp
var tableState = new TableStateChange
{
    page = 2,
    pageSize = 50,
    sortColumn = "name",
    sortDirection = null // Ascending.
};

IPagedResult<Account> result = await container.PagedQueryAsync<Account>(
    tableState,
    "userId",
    userId);
```

The named partition-key property must exist on the queried type and must be a `Guid`.

### Existing LINQ Query

```csharp
IQueryable<Order> query = container
    .GetItemLinqQueryable<Order>()
    .Where(order => order.status != OrderStatus.Cancelled);

IPagedResult<Order> result = await query.PagedQueryAsync(
    tableState,
    tenantId);
```

The partition or tenant argument remains required even when the existing query already contains other predicates.

### Pre-Sorted Query

```csharp
IOrderedQueryable<Order> orderedQuery = container
    .GetItemLinqQueryable<Order>()
    .Where(order => order.status != OrderStatus.Cancelled)
    .OrderByDescending(order => order.total)
    .ThenBy(order => order.createdAt);

IPagedResult<Order> result = await orderedQuery.PagedQueryAsync(
    tableState,
    tenantId);
```

The pre-applied order is retained.

## Filtering Behavior

Each entry in `filters` is an equality comparison:

```csharp
filters = new List<KeyValuePair<string, string>>
{
    new("status", "Active"),
    new("priority", "2")
};
```

This produces behavior equivalent to:

```csharp
query.Where(item => item.status == "Active" && item.priority == 2);
```

Comparison operators such as `!=`, `>`, `<`, `contains`, and `startswith` are not supported by `TableStateChange.filters`. Apply those predicates to an existing LINQ query before calling `PagedQueryAsync`.

## Filter Value Conversion

Filter values are supplied as strings and converted to the target property type.

| Target type | Conversion |
|---|---|
| `string` | Used unchanged. |
| `Guid` | `Guid.Parse`. |
| `int` | `int.Parse`. |
| `long` | `long.Parse`. |
| `decimal` | `decimal.Parse`. |
| `double` | `double.Parse`. |
| `bool` | `bool.Parse`. |
| `DateTime` | `DateTime.Parse`. |
| `Nullable<T>` | Empty string becomes `null`; otherwise conversion uses the underlying type. |
| Other convertible types | `Convert.ChangeType`. |

Invalid formats, invalid casts, and numeric overflow are wrapped in an `ArgumentException` that identifies the filter.

## Validation and Errors

- `tableState == null` throws `ArgumentNullException`.
- `page < 1` throws `ArgumentException`.
- `pageSize < 1` throws `ArgumentException`.
- An offset calculation that would overflow throws `ArgumentException`.
- `tenantId == Guid.Empty` throws `ArgumentException`.
- Partition-key, filter, and sort property names must match `^[a-zA-Z0-9_]+$`.
- A named property that does not exist, or whose type is incompatible with the generated expression, causes expression construction to fail.

## Security and Isolation

- Property names are restricted to alphanumeric characters and underscores before expression construction.
- Values are represented as expression constants rather than interpolated query text.
- Tenant overloads always add an equality predicate against `tenantId`.
- Custom partition overloads always add an equality predicate against the supplied partition-key property.

Property-name validation reduces injection risk but does not confirm that a property exists. Callers should expose an allowlist of sortable and filterable properties rather than passing arbitrary client input directly.

## Testing Support

Every overload accepts an optional `IQueryExecutor`. Production calls default to `CosmosQueryExecutor.Default`. Unit tests can pass `InMemoryQueryExecutor.Default`.

```csharp
var tableState = new TableStateChange
{
    page = 1,
    pageSize = 2,
    sortColumn = "name",
    sortDirection = "asc"
};

IPagedResult<TestProjection> result = await query.PagedQueryAsync(
    tableState,
    tenantId,
    InMemoryQueryExecutor.Default);

Assert.Equal(2, result.items.Count);
Assert.Equal(3, result.totalCount);
```

## Best Practices

1. Use tenant or partition filtering to keep queries isolated and efficient.
2. Keep page sizes bounded at the API boundary.
3. Index frequently filtered and sorted properties in Cosmos DB.
4. Use a stable sort column when deterministic pagination is required.
5. Apply non-equality predicates with LINQ before calling `PagedQueryAsync`.
6. Map user-facing sort and filter names through an allowlist of known model properties.
7. Inject `InMemoryQueryExecutor.Default` in unit tests rather than requiring a Cosmos DB emulator.

## Related Types

- [IQueryExecutor](IQueryExecutor.spec.md) — query execution interface.
- [CosmosQueryExecutor](CosmosQueryExecutor.spec.md) — production Cosmos DB implementation.
- [ITenantFilterable](ITenantFilterable.spec.md) — tenant filtering contract.
- `FilteredQueryExtensions` — helpers in `src/CosmosExtensions/FilteredQuery.cs` for constructing partition-scoped LINQ queries.
