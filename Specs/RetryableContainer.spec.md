# RetryableContainer Specification

## Overview

`RetryableContainer` wraps a Cosmos DB `Container` with configurable retry logic. It automatically retries on 429 TooManyRequests (always, using server RetryAfter header) and optionally retries on 404 NotFound based on `RetryOptions.RetryWhenNotFound`. Supports exponential backoff and optional logging.

## Interface

```csharp
public interface IRetryableContainer
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Container` | `Container` | The underlying Cosmos DB container |
| `Options` | `RetryOptions` | The retry options governing retry behavior |

### Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `ApplyAndPersistAsync<T>(IEvent, ...)` | `Task<T?>` | Apply event and persist with retry logic |
| `ApplyAndPersistAsync<T>(IEvent, Guid, ...)` | `Task<T?>` | Apply event to specific projection with retry |
| `ReadItemAsync<T>(string, PartitionKey, ...)` | `Task<ItemResponse<T>?>` | Read item with retry for transient failures |
| `CreateItemAsync<T>(T, PartitionKey?, ...)` | `Task<ItemResponse<T>?>` | Create item with 429 retry; when partition key is null, Cosmos SDK infers it from the item |
| `CreateItemAsync<T>(T, ...)` | `Task<ItemResponse<T>?>` | Create item with 429 retry; partition key inferred by Cosmos SDK |
| `UpsertItemAsync<T>(T, ...)` | `Task<ItemResponse<T>?>` | Upsert item with 429 retry |
| `DoBulkCreateAsync<T>(List<T>, ...)` | `Task` | Bulk creates items with per-item 429 retry; container must have bulk enabled |
| `DoBulkUpsertAsync<T>(List<T>, ...)` | `Task` | Bulk upserts items with per-item 429 retry; container must have bulk enabled |
| `DoBulkCreateEventAsync(List<IEvent>, ...)` | `Task` | Bulk creates events with per-item 429 retry using each event's `aggregateRootId` partition key; container must have bulk enabled |
| `DoBulkUpsertEventAsync(List<IEvent>, ...)` | `Task` | Bulk upserts events with per-item 429 retry using each event's `aggregateRootId` partition key; container must have bulk enabled |

### Callback Parameters

Apply/read/write methods accept optional callbacks to handle different failure scenarios:

| Callback | Type | When Invoked |
|----------|------|-------------|
| `onExhausted` | `Func<Task>?` | All retries exhausted for not-found result |
| `onNotFound` | `Func<Task>?` | Item not found and `RetryWhenNotFound` is false |
| `onException` | `Func<Exception, Task>?` | Non-transient exception occurs |

Bulk methods use item-aware exception callbacks so callers can identify which item failed:

| Method | `onException` Type |
|--------|---------------------|
| `DoBulkCreateAsync<T>(...)` | `Func<T, Exception, Task>?` |
| `DoBulkUpsertAsync<T>(...)` | `Func<T, Exception, Task>?` |
| `DoBulkCreateEventAsync(...)` | `Func<IEvent, Exception, Task>?` |
| `DoBulkUpsertEventAsync(...)` | `Func<IEvent, Exception, Task>?` |

## Implementation

```csharp
public class RetryableContainer : IRetryableContainer
```

### Constructor

```csharp
public RetryableContainer(Container container, RetryOptions options)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `container` | `Container` | The underlying Cosmos DB container (required) |
| `options` | `RetryOptions` | Retry configuration (required) |

Throws `ArgumentNullException` if either parameter is null.

### Retry Behavior

1. **429 TooManyRequests**: Always retries. Waits for a delay derived from the server-provided `RetryAfter` header and then applies exponential backoff via `RetryOptions.GetDelayForAttempt(attempt, delay, delayMultiplier: 3)`. If Cosmos provides a positive `RetryAfter`, the effective base delay is the greater of that value and `Options.Delay.TotalMilliseconds`; if `RetryAfter` is missing or non-positive, the fallback floor of `MIN_DELAY_MS = 100ms` is used as the base delay. The current `RequestCharge` is included in the retry log message. Throws after `MaxRetries` exhausted.
2. **409 Conflict on retry** (`CreateItemAsync` only): When `attempt > 0`, a `409 Conflict` response means the item was already committed in a prior attempt before a 429 was returned. Treated as idempotent success — returns `default` (null) instead of throwing. A first-attempt 409 (attempt == 0) is a real conflict and is passed to the `onException` callback as usual.
3. **404 NotFound (ApplyAndPersist)**: `ApplyAndPersistAsync` returns null on NotFound internally. If `RetryWhenNotFound=true`, retries up to `MaxRetries`. If false, invokes `onNotFound` callback immediately.
4. **404 NotFound (ReadItem)**: Catches `CosmosException` with NotFound status. Same retry logic as above.
5. **Other Exceptions**: Invoked `onException` callback if provided, otherwise rethrows.

### Exponential Backoff

Uses `RetryOptions.GetDelayForAttempt(attempt)` for delay calculation between retry attempts.

## Extension Method

```csharp
public static class RetryableContainerExtensions
{
    public static IRetryableContainer WithRetry(this Container container, RetryOptions retryOptions)
    public static IRetryableContainer WithRetry(this Container container, bool retryWhenNotFound)
    public static IRetryableContainer WithRetry(this Container container)
    public static IRetryableContainer WithRetry(this Container container, ILogger logger)
    public static IRetryableContainer WithRetry(this Container container, bool retryWhenNotFound, ILogger logger)
}
```

Creates a new `RetryableContainer` wrapping the given container. The boolean overload creates default `RetryOptions` with `RetryWhenNotFound` set to the provided value — a convenient shorthand for eventual consistency scenarios (`container.WithRetry(true)`). The no-arg overload uses default `RetryOptions` — convenient for query-only scenarios (`container.WithRetry()`). The `ILogger` overloads create default `RetryOptions` with `Logger` set and `LogRetries = true` — enabling structured logging of retry operations (`container.WithRetry(logger)` or `container.WithRetry(true, logger)`).

## LINQ Query Support (RetryableQuery)

`RetryableQuery<T>` wraps an `IQueryable<T>` with `RetryOptions` to provide retry-aware LINQ query execution against Cosmos DB. Terminal operations (ReadAllAsync, FirstOrDefaultAsync, CountAsync) automatically retry on 429 TooManyRequests.

### Creating Queries

Use `FilteredQuery<T>()` or `GetItemLinqQueryable<T>()` on `IRetryableContainer`:

```csharp
// Via FilteredQuery (tenant-scoped)
var results = await container.WithRetry()
    .FilteredQuery<MyProjection>(tenantId)
    .Where(x => x.name.Contains("test"))
    .OrderBy(x => x.name)
    .ReadAllAsync();

// Via FilteredQuery (string partition key)
var results = await container.WithRetry()
    .FilteredQuery<MyProjection>("partitionValue")
    .Where(x => x.isActive)
    .Take(10)
    .ReadAllAsync();

// Via GetItemLinqQueryable (unfiltered)
var count = await container.WithRetry()
    .GetItemLinqQueryable<MyProjection>()
    .Where(x => x.isDeleted)
    .CountAsync();
```

### Query Methods (IRetryableContainer)

| Method | Returns | Description |
|--------|---------|-------------|
| `FilteredQuery<T>(Guid tenantId, ...)` | `RetryableQuery<T>` | Tenant-scoped filtered query (requires ITenantFilterable) |
| `FilteredQuery<T>(string partitionKeyValue, ...)` | `RetryableQuery<T>` | Partition-scoped filtered query |
| `FilteredQuery<T>(PartitionKey partitionKey, ...)` | `RetryableQuery<T>` | Partition-scoped filtered query |
| `GetItemLinqQueryable<T>(QueryRequestOptions?)` | `RetryableQuery<T>` | Unfiltered LINQ queryable |

### RetryableQuery<T> LINQ Chain Methods

All chain methods return a new `RetryableQuery<T>` (or `RetryableQuery<TResult>` for Select), preserving retry options through the chain.

| Method | Description |
|--------|-------------|
| `Where(predicate)` | Filters by the specified predicate |
| `Select(selector)` | Projects each element into a new form |
| `OrderBy(keySelector)` | Sorts ascending by key |
| `OrderByDescending(keySelector)` | Sorts descending by key |
| `ThenBy(keySelector)` | Secondary ascending sort (after OrderBy) |
| `ThenByDescending(keySelector)` | Secondary descending sort (after OrderBy) |
| `Take(count)` | Returns first N elements |
| `Skip(count)` | Bypasses first N elements |
| `Distinct()` | Returns distinct elements |

### RetryableQuery<T> Terminal Methods

Terminal methods execute the query and retry on 429 TooManyRequests using the server's RetryAfter header.

| Method | Returns | Description |
|--------|---------|-------------|
| `ReadAllAsync()` | `Task<List<T>>` | Execute and return all matching items |
| `FirstOrDefaultAsync()` | `Task<T?>` | Execute and return first match or null |
| `CountAsync()` | `Task<int>` | Execute and return count of matches |
| `AsQueryable()` | `IQueryable<T>` | Access underlying queryable for interop (e.g., PagedQueryAsync) |

### Extension Method: FirstOrNewAsync

```csharp
public static async Task<T> FirstOrNewAsync<T>(this RetryableQuery<T> query) where T : new()
```

Returns the first matching item or a new instance of `T`. Defined as an extension method because the `new()` constraint cannot be applied to just one method on the unconstrained `RetryableQuery<T>` class (needed for `Select<TResult>` where TResult may not be `new()`).

### IQueryExecutor Integration

`RetryableQuery<T>` accepts an optional `IQueryExecutor` at construction time (defaults to `CosmosQueryExecutor.Default`). For testing, use `InMemoryQueryExecutor.Default` to execute queries against in-memory collections without Cosmos DB.

### RetryableQuery Retry Behavior

- **429 TooManyRequests**: Retries the entire query execution from scratch. Uses server-provided `RetryAfter` header (falls back to 1000ms). Throws `CosmosException` after `MaxRetries` exhausted.
- **Other exceptions**: Not caught — propagate to caller immediately.
- **Logging**: Uses the same `RetryOptions.LogRetry()` mechanism as write operations.

## Usage Examples

### Basic Usage

```csharp
var container = await nostify.GetProjectionContainerAsync<MyProjection>();
var retryable = container.WithRetry(new RetryOptions(
    maxRetries: 3, 
    delay: TimeSpan.FromSeconds(1), 
    retryWhenNotFound: true
));
var result = await retryable.ApplyAndPersistAsync<MyProjection>(evt);
```

### With Callbacks

```csharp
var result = await retryable.ApplyAndPersistAsync<MyProjection>(evt,
    onExhausted: () => nostify.HandleUndeliverableAsync("Retry", "Exhausted", evt),
    onNotFound: () => nostify.HandleUndeliverableAsync("NotFound", "Not retrying", evt),
    onException: (ex) => nostify.HandleUndeliverableAsync("Error", ex.Message, evt)
);
```

### With Exponential Backoff and Logging

```csharp
var retryable = container.WithRetry(new RetryOptions(
    maxRetries: 5,
    delay: TimeSpan.FromSeconds(1),
    retryWhenNotFound: true,
    delayMultiplier: 2.0,
    logRetries: true,
    logger: myLogger
));
```

## Key Relationships

- **RetryOptions**: Governs all retry behavior (max retries, delays, backoff, logging)
- **Container**: Underlying Cosmos DB container that operations are delegated to
- **ContainerExtensions.ApplyAndPersistAsync**: Called internally for apply-and-persist operations
- **DefaultEventHandlers**: Uses `RetryableContainer` via `.WithRetry()` for all retry-enabled handlers
- **INostify**: Not directly referenced — callbacks decouple retry logic from the nostify interface
- **RetryableQuery<T>**: Query wrapper returned by FilteredQuery/GetItemLinqQueryable, provides retry-aware LINQ execution
- **IQueryExecutor**: Abstraction for query execution — `CosmosQueryExecutor` (production), `InMemoryQueryExecutor` (tests)
- **FilteredQueryExtensions**: Used internally by RetryableContainer to create the underlying IQueryable from Container

## Source Files

- Interface: `src/ErrorHandling/IRetryableContainer.cs`
- Implementation: `src/ErrorHandling/RetryableContainer.cs`
- Extension: `src/ErrorHandling/RetryableContainerExtensions.cs`
- Query Wrapper: `src/CosmosExtensions/RetryableQuery.cs`
- In-Memory Executor: `src/TestClasses/InMemoryQueryExecutor.cs`
- Tests: `nostify.Tests/RetryableContainer.Tests.cs`
- Query Tests: `nostify.Tests/RetryableQuery.Tests.cs`
