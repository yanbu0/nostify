# ContainerExtensions Specification

## Overview

`ContainerExtensions` provides static extension methods on `Microsoft.Azure.Cosmos.Container` for applying events and persisting aggregate/projection state. These methods implement the core CQRS apply-and-persist pattern used throughout nostify.

## Class Definition

```csharp
public static class ContainerExtensions
```

## Key Methods

### ApplyAndPersistAsync (core overload)

```csharp
public static async Task<T> ApplyAndPersistAsync<T>(
    this Container container,
    List<IEvent> newEvents,
    PartitionKey partitionKey,
    Guid? projectionBaseAggregateId,
    ILogger? logger = null) where T : NostifyObject, new()
```

Reads the current state of an item, applies one or more events via `Apply()`, and either creates a new Cosmos item or patches the existing one.

**Create path** (new item): Calls `Container.CreateItemAsync<T>`. Catches `409 Conflict` as idempotent success (at-least-once Kafka delivery). All other `CosmosException`s are wrapped in `NostifyException`. `429 TooManyRequests` is propagated with `throw` so `RetryableContainer.ExecuteWithRetryAsync` can retry it.

**Patch path** (existing item): Calls `SafePatchItemAsync<T>` to build and apply diff-based patch operations. If `SafePatchItemAsync` returns a `PatchItemResult` with `statusCode == TooManyRequests`, the original `CosmosException` is rethrown via `ExceptionDispatchInfo.Capture(ce).Throw()` (stored in `patchResult.capturedDispatchInfo`), preserving `RetryAfter`, `ActivityId`, and the original stack trace for `RetryableContainer` to process. A top-level `catch (CosmosException ... TooManyRequests)` also propagates any 429 thrown during the patch via `throw`. All other `CosmosException`s are wrapped in `NostifyException`.

### SafePatchItemAsync

```csharp
public static async Task<PatchItemResult> SafePatchItemAsync<T>(
    this Container container,
    string id,
    PartitionKey partitionKey,
    IReadOnlyList<PatchOperation> patchOperations)
```

Performs a Cosmos DB patch operation and always returns a `PatchItemResult` — never throws for `CosmosException`s. This is the "never throws" contract that direct callers rely on.

| Result type | Condition |
|-------------|-----------|
| `SuccessResult` | Patch succeeded, or all operations were filtered out |
| `NotFoundResult` | `404 NotFound` from Cosmos |
| `ExceptionResult` | Any other `CosmosException` (including `429 TooManyRequests`) or general `Exception` |

> **Note**: `SafePatchItemAsync` preserves its always-returns-a-result contract and does NOT rethrow 429. The caller (`ApplyAndPersistAsync`) is responsible for detecting `patchResult.statusCode == TooManyRequests` and re-throwing a raw `CosmosException(429)` so `RetryableContainer` can retry.

### MultiApplyAndPersistAsync

```csharp
public static async Task<List<P>> MultiApplyAndPersistAsync<P>(
    this Container bulkContainer,
    INostify nostify,
    Event eventToApply,
    List<Guid> projectionIds,
    int batchSize = 100,
    RetryOptions? retryOptions = null) where P : NostifyObject, new()
```

Delegates to `INostify.MultiApplyAndPersistAsync`. Applies an event to all listed projection IDs in batches, with optional per-item retry.

### BulkDeleteFromEventsAsync / BulkDeleteAsync

```csharp
public static async Task<int> BulkDeleteFromEventsAsync<P>(
    this Container containerToDeleteFrom,
    string[] events,
    RetryOptions? retryOptions = null) where P : NostifyObject
```

Bulk delete operations are implemented as Cosmos patch operations that set `ttl = 1` on matching documents.

- `BulkDeleteFromEventsAsync` resolves target IDs from Kafka trigger events and delegates to `BulkDeleteAsync`.
- `BulkDeleteAsync` (GUID list and object list overloads) performs batched patch operations and returns the number of successful patches.
- When `retryOptions` is provided, each patch retries on transient failures:
  - always retries `429 TooManyRequests` (using `RetryAfter` when present),
  - retries `404 NotFound` only when `RetryOptions.RetryWhenNotFound` is `true`,
  - calculates backoff using the caller-configured `RetryOptions.DelayMultiplier`,
  - logs retry attempts through `RetryOptions.LogRetry`.

## 429 TooManyRequests Propagation

`ApplyAndPersistAsync` is designed to be called through `RetryableContainer`, which wraps it in `ExecuteWithRetryAsync`. For retry to work, 429 `CosmosException`s must escape as raw `CosmosException`s — not wrapped in `NostifyException`.

The two paths where 429 could previously be swallowed, and the fix applied:

| Path | Old behavior | Fix |
|------|-------------|-----|
| Create (`CreateItemAsync`) | 429 wrapped in `NostifyException` | `catch (CosmosException ex) when (ex.StatusCode == TooManyRequests) { throw; }` added before general handler |
| Patch result check (`patchResult.IsException`) | 429 result converted to `NostifyException` | Check `patchResult.statusCode == TooManyRequests` and rethrow as raw `CosmosException(429)` |

## Related Types

- [RetryableContainer](RetryableContainer.spec.md) — Wraps `Container` with `ExecuteWithRetryAsync`; calls `ContainerExtensions.ApplyAndPersistAsync` internally
- [NostifyException](NostifyException.spec.md) — Thrown for non-transient errors
- [RetryOptions](RetryOptions.spec.md) — Retry configuration
