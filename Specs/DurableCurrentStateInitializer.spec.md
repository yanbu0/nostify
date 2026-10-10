# DurableCurrentStateInitializer Specification

## Overview

`DurableCurrentStateInitializer<TAggregate>` coordinates a full aggregate current-state rebuild from the event store with Azure Durable Functions. It replaces one long-running HTTP execution with retryable delete, cursor-paging, and batch activities.

A fixed orchestration instance ID prevents overlapping rebuilds. Distinct aggregate IDs are read from the event store in pages ordered by their persisted GUID string values. Every request carries the last ID returned by the previous page and queries strictly after that cursor. This prevents concurrent event inserts or deletes from shifting an offset and causing later aggregate IDs to be skipped or processed twice.

## Type Constraints

```csharp
where TAggregate : NostifyObject, IAggregate, new()
```

## Constructor

```csharp
new DurableCurrentStateInitializer<TAggregate>(
    nostify,
    instanceId,
    partitionKeyPath: "/tenantId",
    batchSize: 1000,
    concurrentBatchCount: 5,
    durableTaskOptions: null,
    cosmosRetryOptions: null)
```

- `instanceId` identifies the singleton orchestration for this rebuild.
- `partitionKeyPath` selects the aggregate current-state container partition path.
- `batchSize` controls work per processing activity.
- `concurrentBatchCount` controls fan-out per cursor page.
- Activity calls default to three attempts with five-second initial delay and 2x backoff.
- Cosmos operations use `RetryOptions`.

## Host Function Flow

1. `StartOrchestration` starts the fixed instance or returns HTTP 409 when it is active.
2. `OrchestrateInitAsync` calls the delete activity.
3. The orchestrator requests a page using `DurableCurrentStatePageInfo`; `LastSeenId` is `null` for the first request.
4. `GetAggregateIds` selects distinct `aggregateRootId` values, filters values strictly after the cursor, orders by ID, and takes one page.
5. Each page is split into concurrent processing batches.
6. After processing, the final ID in the page becomes the next `LastSeenId` cursor.
7. `ProcessBatch` reads events, orders each stream by timestamp, rehydrates aggregates, and bulk-upserts them.
8. `CancelOrchestration` terminates and purges the active instance.

Insertions that sort after the active cursor remain eligible for later pages. Insertions behind the cursor are outside the already-consumed scan range. Deleting an ID from an earlier page does not move the next page boundary.

## Cursor Request

```csharp
public readonly struct DurableCurrentStatePageInfo
{
    public DurableCurrentStatePageInfo(Guid? lastSeenId = null);

    public Guid? LastSeenId { get; }
}
```

The request is serialized across the Durable activity boundary. The cursor is exclusive: an ID equal to `LastSeenId` is not returned again.

## Activity Helpers

| Method | Purpose |
|---|---|
| `DeleteAllCurrentState` | Deletes existing aggregate current-state documents |
| `GetAggregateIds` | Returns the next ordered page of distinct IDs after the optional cursor |
| `ProcessBatch` | Rehydrates and persists one ID batch |
| `IsCancellationRequestedAsync` | Prevents activity work after cancellation |

## Template Integration

Both aggregate-producing templates generate an Admin function backed by this initializer:

- `templates/nostifyAggregate/_ReplaceMe_/Admin/_ReplaceMe_CurrentStateInit.cs`
- `templates/nostify/_ReplaceMe_/Aggregates/_ReplaceMe_/Admin/_ReplaceMe_CurrentStateInit.cs`

The generated HTTP route accepts `POST` to start and `DELETE` to cancel. Its get-ID activity accepts `DurableCurrentStatePageInfo` and forwards the cursor request to `GetAggregateIds`.

## Related Types

- [IAggregate](IAggregate.spec.md)
- [DurableProjectionInitializer](DurableProjectionInitializer.spec.md)
- [RetryOptions](RetryOptions.spec.md)
