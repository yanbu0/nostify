# DefaultCommandHandler Specification

## Purpose
`DefaultCommandHandler` is a static utility class providing default command handler implementations for common nostify CQRS/Event Sourcing operations. These handlers cover single and bulk command processing for aggregates: create, update, and delete.

## Location
`src/DefaultHandlers/DefaultCommandHandlers.cs`

## Key Design Principles

1. **All handlers return meaningful values** — Single-event handlers return the `Guid` of the affected aggregate root; bulk handlers return `int` (count of events processed).
2. **`allowRetry` defaults to `true`** — All handlers expose retry control. Single-event handlers call `INostify.PersistEventAsync(IEvent, RetryOptions?)`; bulk handlers have a `bool allowRetry = true` overload that uses `nostify.DefaultRetryOptions` (from `NostifyFactory.WithCosmos`). A developer must explicitly pass `allowRetry: false` or supply `null` to the `RetryOptions?` overload to disable retry.
3. **Dual overloads for retry** — Each bulk handler has two overloads: one accepting `bool allowRetry` (simple, defaults to `true`) and one accepting `RetryOptions?` (configurable retry, requires explicit `userId`, `partitionKey`, `batchSize` to avoid ambiguity). The `bool` overload delegates to the `RetryOptions?` overload passing `nostify.DefaultRetryOptions` when true or `null` when false.
4. **Static methods** — All handlers are `public async static`, designed to be called directly without instantiation.

## Method Groups

### Single-Event Handlers

| Method | Return Type | Description |
|--------|-------------|-------------|
| `HandlePostAsync<T>` | `Task<Guid>` | Creates a single aggregate root from an `HttpRequestData` body. Returns the new aggregate root ID. |
| `HandlePatchAsync<T>` | `Task<Guid>` | Updates a single aggregate root from an `HttpRequestData` body. Returns the aggregate root ID. |
| `HandleDeleteAsync<T>` | `Task<Guid>` | Deletes a single aggregate root by ID. Returns the aggregate root ID. |

### Bulk Create Handlers

| Method | Overload | Description |
|--------|----------|-------------|
| `HandleBulkCreateAsync<T>` | `bool allowRetry` | Bulk creates aggregates from request data. Passes `allowRetry` to `BulkPersistEventAsync`. |
| `HandleBulkCreateAsync<T>` | `RetryOptions? retryOptions` | Bulk creates aggregates with configurable retry. Passes `retryOptions` to `BulkPersistEventAsync`. |

Both overloads accept `partitionKeyName` (default: `"tenantId"`) to set the partition key property on each dynamic object.

### Bulk Update Handlers

| Method | Overload | Description |
|--------|----------|-------------|
| `HandleBulkUpdateAsync<T>` | `bool allowRetry` | Bulk updates aggregates from request data. Validates each object has a valid `id` property. |
| `HandleBulkUpdateAsync<T>` | `RetryOptions? retryOptions` | Bulk updates with configurable retry. Same validation as `bool` overload. |

### Bulk Delete Handlers (from HttpRequestData)

| Method | Overload | Description |
|--------|----------|-------------|
| `HandleBulkDeleteAsync<T>` | `HttpRequestData req, bool allowRetry` | Bulk deletes aggregates from a request body containing ID strings. Validates each ID parses as a GUID. |
| `HandleBulkDeleteAsync<T>` | `HttpRequestData req, RetryOptions? retryOptions` | Same as above with configurable retry. |

### Bulk Delete Handlers (from List\<Guid\>)

| Method | Overload | Description |
|--------|----------|-------------|
| `HandleBulkDeleteAsync<T>` | `List<Guid> aggregateRootIds, bool allowRetry` | Bulk deletes aggregates from a list of GUIDs. |
| `HandleBulkDeleteAsync<T>` | `List<Guid> aggregateRootIds, RetryOptions? retryOptions` | Same as above with configurable retry. |

### Obsolete Backward-Compatible Methods

The following non-`Async` method names are preserved as `[Obsolete]` wrappers that delegate to their `Async` counterparts. They will be removed in a future version.

| Obsolete Method | Replacement |
|-----------------|-------------|
| `HandlePatch<T>` | `HandlePatchAsync<T>` |
| `HandlePost<T>` | `HandlePostAsync<T>` |
| `HandleDelete<T>` | `HandleDeleteAsync<T>` |
| `HandleBulkCreate<T>` | `HandleBulkCreateAsync<T>` |
| `HandleBulkUpdate<T>` | `HandleBulkUpdateAsync<T>` |
| `HandleBulkDelete<T>` | `HandleBulkDeleteAsync<T>` |

## Common Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nostify` | `INostify` | — | The Nostify instance for event persistence |
| `command` | `NostifyCommand` | — | The command to execute |
| `userId` | `Guid` | `default` | User identifier for the operations |
| `partitionKey` | `Guid` | `default` | Tenant identifier for the operations |
| `batchSize` | `int` | `100` | Number of events per batch for bulk operations |
| `allowRetry` | `bool` | `true` | When `true`, uses `nostify.DefaultRetryOptions` for retry. Set to `false` to disable retry entirely. |
| `retryOptions` | `RetryOptions?` | — (required) | Configurable retry options for per-item retry behavior. No default to avoid ambiguity with `bool allowRetry` overload. |
| `publishErrorEvents` | `bool` | `false` | Whether to publish error events for failed operations |

## Key Relationships

- **`INostify`** — Used for event persistence via `PersistEventAsync(IEvent, RetryOptions?)` (single) and `BulkPersistEventAsync` (bulk). Both `bool allowRetry` and `RetryOptions?` retry controls are available through the handler surface.
- **`EventFactory`** — Used to create events from dynamic payloads (`Create<T>`) or null-payload events for deletes (`CreateNullPayloadEvent`).
- **`HttpRequestData`** — Request body deserialized as `List<dynamic>` (create/update) or `List<string>` (delete by ID strings).
- **`RetryOptions`** — When provided to a bulk handler, passed directly to `INostify.BulkPersistEventAsync(RetryOptions?)` for per-item retry via `RetryableContainer`.

## Error Handling

- **Single handlers**: Exceptions propagate to the caller. `PersistEventAsync(IEvent, RetryOptions?)` on the underlying `Nostify` implementation logs the error and writes the event to the undeliverable container via `HandleUndeliverableAsync` before re-throwing. Passing `null` disables retry entirely for the single-event handler path.
- **Bulk handlers**: Error handling is delegated to `BulkPersistEventAsync`, which writes failed events to the undeliverable events container and optionally publishes error events to Kafka.
- **Validation**: `HandleBulkUpdate` throws `ArgumentException` if any object lacks a valid `id`. `HandleBulkDelete` (from request) throws `ArgumentException` for unparseable GUIDs.
- **Null body after deserialization**: `HandleBulkUpdateAsync` and `HandleBulkDeleteAsync` (HttpRequestData overloads) throw `NostifyException` when the request body deserializes to null (for example, a literal JSON `null` body), preventing silent no-op responses. Malformed JSON fails earlier during deserialization and is not converted into `NostifyException` by these handlers.
