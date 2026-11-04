# DefaultCommandHandler Specification

## Purpose
`DefaultCommandHandler` is a static utility class providing default command handler implementations for common nostify CQRS/Event Sourcing operations. These handlers cover single and bulk command processing for aggregates: create, update, and delete.

## Location
`src/DefaultHandlers/DefaultCommandHandlers.cs`

## Key Design Principles

1. **Payloads are change sets** — Create and Update payloads should contain only properties intentionally set by the current event. Omitted properties are not changes. Explicit `null`, `false`, `0`, empty strings, and empty collections are changes.
2. **Result-first bulk APIs** — Single-event handlers return the `Guid` of the affected aggregate root. `GetBulk*ResultsAsync<T>` methods return one ordered `BulkPersistEventResult` per input without hiding item failures. `CountBulk*ResultsAsync<T>` methods return the number of results, including unsuccessful results.
3. **Single-event handlers use direct persistence** — `HandlePostAsync`, `HandlePatchAsync`, and `HandleDeleteAsync` call `INostify.PersistEventAsync(IEvent)` with no retry configuration because they persist single items through the standard Cosmos SDK path.
4. **Dual overloads for bulk retry** — Each bulk handler has two overloads: one accepting `bool allowRetry` (simple, defaults to `true`) and one accepting `RetryOptions?` (configurable retry, requires explicit `userId`, `partitionKey`, `batchSize` to avoid ambiguity). The `bool` overload delegates to the `RetryOptions?` overload passing `nostify.DefaultRetryOptions` when true or `null` when false.
5. **Static methods** — All handlers are `public async static`, designed to be called directly without instantiation.

## Method Groups

### Single-Event Handlers

| Method | Return Type | Description |
|--------|-------------|-------------|
| `HandlePostAsync<T>` | `Task<Guid>` | Creates a single aggregate root from an `HttpRequestData` body. The body may be a partial aggregate containing only initial properties set by this event; the handler adds the generated `id` and configured partition-key property. Returns the new aggregate root ID. |
| `HandlePatchAsync<T>` | `Task<Guid>` | Updates a single aggregate root from an `HttpRequestData` body. The body should contain only changed properties; the aggregate ID may be supplied by route binding. Returns the aggregate root ID. |
| `HandleDeleteAsync<T>` | `Task<Guid>` | Deletes a single aggregate root by ID. Returns the aggregate root ID. |

#### Normative Payload Guidance for Consumers and AI Agents

- **MUST** model Create and Update payloads as event-specific change sets, not snapshots of the complete aggregate.
- **MUST** include every property intentionally set by the event, including intentional clearing/defaulting values.
- **MUST NOT** include unchanged properties merely because they exist in UI state, a generated client model, or the aggregate CLR type.
- **SHOULD** supply the Update aggregate ID through the route when using the generated PATCH handler; include it in the payload only when the selected overload or binding path requires it.
- **MUST** satisfy properties marked `[Required]` and properties whose `[RequiredFor(...)]` matches the current event type. Other omitted properties remain untouched on Update and retain defaults on Create.
- **MUST NOT** call `NoValidate()` simply to make partial payloads work. Partial payloads are supported by normal validation; validation should be bypassed only for an intentional, separately justified use case.

This rule prevents stale UI state and serializer-generated default values from overwriting newer aggregate state. Event payloads also remain an accurate record of what each event changed.

### Bulk Result and Count Handlers

| Operation | Result API | Count API | Inputs |
|-----------|------------|-----------|--------|
| Create | `GetBulkCreateResultsAsync<T>` | `CountBulkCreateResultsAsync<T>` | `HttpRequestData` or `List<T>` |
| Update | `GetBulkUpdateResultsAsync<T>` | `CountBulkUpdateResultsAsync<T>` | `HttpRequestData` |
| Delete | `GetBulkDeleteResultsAsync<T>` | `CountBulkDeleteResultsAsync<T>` | `HttpRequestData` or `List<Guid>` |

Every API has a simple `bool allowRetry` overload and an explicit `RetryOptions?` overload. Result APIs are the primary implementations and return the exact ordered list from `INostify.BulkPersistEventsAsync`. Count APIs delegate to the corresponding result API and return `results.Count`; this is the number of processed inputs, not the number of successful results. Callers that need to distinguish created, conflict/idempotent success, failed, or retry-exhausted outcomes must use the result API.

Create overloads accept `partitionKeyName` (default: `"tenantId"`) to set the partition key property on each aggregate object.

### Obsolete Backward-Compatible Methods

The `Task<int>` overloads named `HandleBulkCreateAsync<T>`, `HandleBulkUpdateAsync<T>`, and `HandleBulkDeleteAsync<T>` are compatibility wrappers over the corresponding `CountBulk*ResultsAsync<T>` methods. They are marked `[Obsolete]` with warnings and will be removed in v6.

The following older non-`Async` method names are also preserved as `[Obsolete]` wrappers that delegate to their `Async` counterparts. They will be removed in a future version.

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
| `allowRetry` | `bool` | `true` | Bulk handlers only. When `true`, uses `nostify.DefaultRetryOptions` for retry. Set to `false` to disable retry entirely. |
| `retryOptions` | `RetryOptions?` | — (required) | Configurable retry options for per-item retry behavior. No default to avoid ambiguity with `bool allowRetry` overload. |
| `publishErrorEvents` | `bool` | `false` | Whether to publish error events for failed operations |

## Key Relationships

- **`INostify`** — Used for event persistence via `PersistEventAsync(IEvent)` (single) and `BulkPersistEventsAsync` (result-aware bulk; obsolete `BulkPersistEventAsync` remains for Task compatibility). Retry controls are exposed only on the bulk handler surface.
- **`EventFactory`** — Used to create events from dynamic payloads (`Create<T>`) or null-payload events for deletes (`CreateNullPayloadEvent`).
- **`HttpRequestData`** — Request body deserialized as `List<dynamic>` (create/update) or `List<string>` (delete by ID strings).
- **`RetryOptions`** — When provided to a bulk handler, passed directly to `INostify.BulkPersistEventsAsync(RetryOptions?)` for direct per-item 429 retry and result telemetry.

## Partial Payload Example

`PATCH /Order/{id}`

```json
{
  "status": "Shipped",
  "shippedDate": "2026-10-07T22:00:00Z"
}
```

The payload intentionally omits fields such as `customerId`, `total`, and `isDeleted`. When the aggregate applies the event with `UpdateProperties<T>()`, only `status` and `shippedDate` change. Sending `"status": null` would instead explicitly clear `status`, if its type and validation permit null.

## Error Handling

- **Single handlers**: Exceptions propagate to the caller. `PersistEventAsync(IEvent)` on the underlying `Nostify` implementation logs the error and writes the event to the undeliverable container via `HandleUndeliverableAsync` before re-throwing.
- **Bulk handlers**: `BulkPersistEventsAsync` writes failed events to the undeliverable container, optionally publishes error events, and returns one result per input. `GetBulk*ResultsAsync<T>` exposes those results unchanged. `CountBulk*ResultsAsync<T>` and obsolete `HandleBulk*Async<T>` wrappers return the result count without discarding or reclassifying outcomes; use the result APIs whenever item status matters.
- **Validation**: `HandleBulkUpdate` throws `ArgumentException` if any object lacks a valid `id`. `HandleBulkDelete` (from request) throws `ArgumentException` for unparseable GUIDs.
- **Null body after deserialization**: `HandleBulkUpdateAsync` and `HandleBulkDeleteAsync` (HttpRequestData overloads) throw `NostifyException` when the request body deserializes to null (for example, a literal JSON `null` body), preventing silent no-op responses. Malformed JSON fails earlier during deserialization and is not converted into `NostifyException` by these handlers.
