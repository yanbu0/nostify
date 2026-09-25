# DurableCurrentStateInitializer Specification

## Overview

`DurableCurrentStateInitializer<TAggregate>` coordinates a full aggregate current-state rebuild from the event store with Azure Durable Functions. It replaces one long-running HTTP execution with retryable delete, paging, and batch activities.

A fixed orchestration instance ID prevents overlapping rebuilds. Aggregate IDs are read from the event store in stable pages, split into concurrent batches, rehydrated in timestamp order, and bulk-upserted into the aggregate current-state container.

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
- `concurrentBatchCount` controls fan-out per page.
- Activity calls default to three attempts with five-second initial delay and 2x backoff.
- Cosmos operations use `RetryOptions`.

## Host Function Flow

1. `StartOrchestration` starts the fixed instance or returns HTTP 409 when it is active.
2. `OrchestrateInitAsync` calls the delete activity.
3. The orchestrator requests stable pages of distinct aggregate IDs.
4. Each page is split into concurrent processing batches.
5. `ProcessBatch` reads events, orders each stream by timestamp, rehydrates aggregates, and bulk-upserts them.
6. `CancelOrchestration` terminates and purges the active instance.

## Activity Helpers

| Method | Purpose |
|---|---|
| `DeleteAllCurrentState` | Deletes existing aggregate current-state documents |
| `GetAggregateIds` | Returns a stable page of distinct IDs from the event store |
| `ProcessBatch` | Rehydrates and persists one ID batch |
| `IsCancellationRequestedAsync` | Prevents activity work after cancellation |

## Template Integration

Both aggregate-producing templates generate an Admin function backed by this initializer:

- `templates/nostifyAggregate/_ReplaceMe_/Admin/_ReplaceMe_CurrentStateInit.cs`
- `templates/nostify/_ReplaceMe_/Aggregates/_ReplaceMe_/Admin/_ReplaceMe_CurrentStateInit.cs`

The generated HTTP route accepts `POST` to start and `DELETE` to cancel.

## Related Types

- [IAggregate](IAggregate.spec.md)
- [DurableProjectionInitializer](DurableProjectionInitializer.spec.md)
- [RetryOptions](RetryOptions.spec.md)
