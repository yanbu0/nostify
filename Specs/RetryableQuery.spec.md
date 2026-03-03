# RetryableQuery Specification

## Overview

`RetryableQuery<T>` wraps an `IQueryable<T>` with `RetryOptions` and an `IQueryExecutor` to provide retry-aware LINQ query execution against Cosmos DB. LINQ-style chain methods (Where, Select, OrderBy, etc.) return new `RetryableQuery<T>` instances preserving retry context. Terminal methods (ReadAllAsync, FirstOrDefaultAsync, CountAsync) automatically retry on 429 TooManyRequests.

## Class

```csharp
public class RetryableQuery<T>
```

### Constructor

```csharp
public RetryableQuery(IQueryable<T> query, RetryOptions options, IQueryExecutor? queryExecutor = null)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `query` | `IQueryable<T>` | required | The underlying LINQ queryable |
| `options` | `RetryOptions` | required | Retry configuration for terminal operations |
| `queryExecutor` | `IQueryExecutor?` | `CosmosQueryExecutor.Default` | Query executor for terminal operations |

Throws `ArgumentNullException` if `query` or `options` is null.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Query` | `IQueryable<T>` | The underlying queryable (for interop) |
| `Options` | `RetryOptions` | The retry options |

### LINQ Chain Methods

All return a new `RetryableQuery<T>` (or `RetryableQuery<TResult>` for Select) preserving options and executor.

| Method | Signature | Description |
|--------|-----------|-------------|
| `Where` | `Where(Expression<Func<T, bool>>)` → `RetryableQuery<T>` | Filter |
| `Select` | `Select<TResult>(Expression<Func<T, TResult>>)` → `RetryableQuery<TResult>` | Project |
| `OrderBy` | `OrderBy<TKey>(Expression<Func<T, TKey>>)` → `RetryableQuery<T>` | Sort ascending |
| `OrderByDescending` | `OrderByDescending<TKey>(...)` → `RetryableQuery<T>` | Sort descending |
| `ThenBy` | `ThenBy<TKey>(...)` → `RetryableQuery<T>` | Secondary ascending sort |
| `ThenByDescending` | `ThenByDescending<TKey>(...)` → `RetryableQuery<T>` | Secondary descending sort |
| `Take` | `Take(int)` → `RetryableQuery<T>` | Limit count |
| `Skip` | `Skip(int)` → `RetryableQuery<T>` | Skip count |
| `Distinct` | `Distinct()` → `RetryableQuery<T>` | Deduplicate |

**Note:** `ThenBy`/`ThenByDescending` cast the internal queryable to `IOrderedQueryable<T>`. They must be called after `OrderBy`/`OrderByDescending`.

### Terminal Methods

Execute the query and retry on 429 TooManyRequests using the server's `RetryAfter` header.

| Method | Returns | Description |
|--------|---------|-------------|
| `ReadAllAsync()` | `Task<List<T>>` | All matching items |
| `FirstOrDefaultAsync()` | `Task<T?>` | First match or null |
| `CountAsync()` | `Task<int>` | Count of matches |
| `AsQueryable()` | `IQueryable<T>` | Underlying queryable (no retry) |

### Retry Behavior

1. **429 TooManyRequests**: Retries entire query from scratch. Uses `RetryAfter` header (fallback 1000ms). Throws after `MaxRetries` exhausted.
2. **Other exceptions**: Not caught — propagate immediately.
3. **Logging**: Uses `RetryOptions.LogRetry()`.

## Extension Methods

```csharp
public static class RetryableQueryExtensions
{
    public static async Task<T> FirstOrNewAsync<T>(this RetryableQuery<T> query) where T : new()
}
```

Returns first match or `new T()`. Separate from the class because `RetryableQuery<T>` cannot have a `new()` constraint (would break `Select<TResult>`).

## Usage Examples

### Fluent Query with Retry

```csharp
var results = await container.WithRetry()
    .FilteredQuery<MyProjection>(tenantId)
    .Where(x => x.name.Contains("test"))
    .OrderBy(x => x.name)
    .Take(10)
    .ReadAllAsync();
```

### First or New with Retry

```csharp
var item = await container
    .WithRetry(new RetryOptions { MaxRetries = 5 })
    .FilteredQuery<MyProjection>(partitionKey)
    .Where(x => x.id == targetId)
    .FirstOrNewAsync();
```

### Interop with PagedQuery

```csharp
var retryable = container.WithRetry();
var query = retryable
    .FilteredQuery<MyProjection>(tenantId)
    .Where(x => x.isActive)
    .AsQueryable(); // back to IQueryable<T>

var pagedResult = await query.PagedQueryAsync(tableState, "tenantId", tenantId);
```

### Testing with InMemoryQueryExecutor

```csharp
var data = new List<MyProjection> { ... };
var query = new RetryableQuery<MyProjection>(
    data.AsQueryable(),
    new RetryOptions(),
    InMemoryQueryExecutor.Default
);
var results = await query.Where(x => x.isActive).ReadAllAsync();
```

## Key Relationships

- **RetryOptions**: Governs retry behavior and logging
- **IQueryExecutor**: Abstraction for executing queries — production uses `CosmosQueryExecutor.Default` (FeedIterator-based), tests use `InMemoryQueryExecutor.Default` (LINQ-to-Objects)
- **IRetryableContainer**: Creates `RetryableQuery<T>` instances via `FilteredQuery<T>` and `GetItemLinqQueryable<T>`
- **FilteredQueryExtensions**: The underlying `Container.FilteredQuery<T>()` extension used by `RetryableContainer`
- **MockRetryableContainer**: Returns `RetryableQuery<T>` backed by `InMemoryQueryExecutor` for handler testing

## Source Files

- Implementation: `src/CosmosExtensions/RetryableQuery.cs`
- Tests: `nostify.Tests/RetryableQuery.Tests.cs`
