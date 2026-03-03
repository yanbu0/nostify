# DefaultEventHandlers Specification

## Purpose
`DefaultEventHandlers` is a static utility class providing default event handler implementations for common nostify CQRS/Event Sourcing operations. These handlers cover the full lifecycle of aggregates and projections: create, update, delete, and multi-apply events, in both single-event and bulk (batch) variants.

## Location
`src/DefaultHandlers/DefaultEventHandlers.cs`

## Key Design Principles

1. **All handlers return meaningful values** — Single-event handlers return the updated entity (`T?` or `P?`); bulk handlers return `int` (count of successfully processed records); `HandleMultiApplyEventAsync` returns `int` (count of updated projections).
2. **Backwards compatibility** — Deprecated non-`Async` methods delegate to the `Async` methods and retain their original `Task` return types. Since `Task<int>` derives from `Task`, this is seamlessly compatible.
3. **Async naming convention** — All primary implementations use the `Async` suffix. Deprecated wrappers without the suffix are marked `[Obsolete]`.

## Method Groups

### Single-Event Handlers

| Method | Return Type | Description |
|--------|-------------|-------------|
| `HandleAggregateEventAsync<T>` | `Task<T?>` | Applies a single event to an aggregate's current state projection. Returns the updated aggregate or `null` if not found. |
| `HandleProjectionEventAsync<P>` | `Task<P?>` | Applies a single event to a projection, optionally initializing external data via `HttpClient`. Returns the updated projection or `null`. |

### Multi-Apply Handler

| Method | Return Type | Description |
|--------|-------------|-------------|
| `HandleMultiApplyEventAsync<P>` | `Task<int>` | Applies a single event to all projections matching a foreign key selector. Returns count of successfully updated projections. Returns `0` if the event is filtered out. |

### Bulk Create Handlers

| Method | Return Type | Description |
|--------|-------------|-------------|
| `HandleAggregateBulkCreateEventAsync<T>` | `Task<int>` | Bulk creates aggregate current state projections from Kafka trigger events. Returns count of matching events processed. |
| `HandleProjectionBulkCreateEventAsync<P>` | `Task<int>` | Bulk creates projections from Kafka trigger events and initializes uninitialized projections. Returns count of matching events processed. |

Each has 3 overloads: no filter, single string filter, `List<string>` filter.

### Bulk Update Handlers

| Method | Return Type | Description |
|--------|-------------|-------------|
| `HandleAggregateBulkUpdateEventAsync<T>` | `Task<int>` | Bulk updates aggregate current states with retry support. Uses `ConcurrentBag<T>` to track successful updates. Returns count of non-null results. |
| `HandleProjectionBulkUpdateEventAsync<P>` | `Task<int>` | Bulk updates projections with retry support and initializes updated projections. Uses `ConcurrentBag<P>` to track successes. Returns count of successfully updated projections. |

Each has 4 overloads: no filter, `RetryOptions` only, single string filter, `List<string>` filter + optional `RetryOptions`.

### Bulk Delete Handlers

| Method | Return Type | Description |
|--------|-------------|-------------|
| `HandleAggregateBulkDeleteEventAsync<T>` | `Task<int>` | Bulk deletes aggregate current states. Propagates the count from `BulkDeleteFromEventsAsync`. |
| `HandleProjectionBulkDeleteEventAsync<P>` | `Task<int>` | Bulk deletes projections. Propagates the count from `BulkDeleteFromEventsAsync`. |

Each has 3 overloads: no filter, single string filter, `List<string>` filter.

## Return Value Semantics

- **Single-event handlers**: Return the entity instance on success, `null` on not-found/failure. Exceptions are caught, reported to undeliverable, and re-thrown.
- **Bulk update handlers**: Return count of entities where `ApplyAndPersistAsync` returned non-null. Failed items are reported to `HandleUndeliverableAsync` individually but do not prevent other items from succeeding.
- **Bulk create handlers**: Return count of events matching the filter. The underlying `BulkCreateFromKafkaTriggerEventsAsync` is all-or-nothing — on success all matching events are created; on failure the exception is caught, all events are reported as undeliverable, and the exception is re-thrown.
- **Bulk delete handlers**: Return the count directly from `BulkDeleteFromEventsAsync<T>`, which reports the number of successfully deleted items.
- **Multi-apply handler**: Returns `List<P>.Count` from `MultiApplyAndPersistAsync`. Returns `0` when the trigger event doesn't match the filter.

## Deprecated Methods

All methods without the `Async` suffix are deprecated via `[Obsolete]` attributes. They delegate directly to their `Async` counterpart using expression-bodied syntax. Their return types remain `Task` (for bulk) or `Task<T?>` / `Task<P?>` (for single-event) for backwards compatibility.

## Key Relationships

- **`INostify`** — Used for container access (`GetCurrentStateContainerAsync`, `GetProjectionContainerAsync`, `GetBulkCurrentStateContainerAsync`, `GetBulkProjectionContainerAsync`), undeliverable handling, and projection initialization. Also provides `Logger` property as fallback for retry logging.
- **`ILogger`** — Optional logger parameter on main handler methods (`HandleAggregateEventAsync`, `HandleProjectionEventAsync`, `HandleAggregateBulkUpdateEventAsync`, `HandleProjectionBulkUpdateEventAsync`). When provided (or when `nostify.Logger` is set), automatically wired into `RetryOptions.Logger` with `LogRetries = true`. Falls back to `nostify.Logger` when the explicit parameter is null.
- **`RetryableContainer`** — Created via `container.WithRetry(retryOptions)` for retry-enabled update operations.
- **`ContainerExtensions`** — Provides `BulkCreateFromKafkaTriggerEventsAsync`, `BulkDeleteFromEventsAsync`, `ApplyAndPersistAsync`.
- **`NostifyKafkaTriggerEvent`** — Deserialized from Kafka trigger event strings; provides `GetEvent()` for event extraction and filtering.
- **`EventFactory`** — Used for creating null-payload error events when exception handling requires an event reference.

## Error Handling Pattern

All handlers follow this pattern:
1. Parse events from Kafka trigger strings
2. Perform the operation (create/update/delete)
3. On success, return meaningful value
4. On exception, iterate all events and report each to `HandleUndeliverableAsync`, then re-throw

For bulk update handlers with retry support, individual event failures are handled inline via the `onExhausted`, `onNotFound`, and `onException` callbacks without stopping processing of other events.

## Test Coverage

Tests are in `nostify.Tests/DefaultEventHandlers.Tests.cs` and cover:
- Success on first attempt (returns expected count)
- Retry paths: succeeds after 1 retry, succeeds after 3 retries
- Exhausted retries → reports undeliverable (returns 0)
- `RetryWhenNotFound=false` → immediate undeliverable (returns 0)
- Default `RetryOptions` behavior
- Non-Cosmos exceptions → immediate undeliverable
- `MaxRetries=0` → single attempt only
- Multiple events with mixed outcomes
- Both projection and aggregate variants
