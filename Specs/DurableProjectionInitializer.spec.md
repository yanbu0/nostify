# DurableProjectionInitializer Specification

## Overview

`DurableProjectionInitializer<TProjection, TAggregate>` coordinates full projection rebuilds using Azure Durable Functions orchestration. It is the recommended approach for large datasets where a single Azure Function execution would time out before all projections are initialized.

The class breaks the work into paged, concurrent batches partitioned by tenant, enforces that only one rebuild can run at a time (via a fixed orchestration instance ID), and exposes activity-level helper methods so the host Azure Function class remains thin.

## Type Parameters

| Parameter | Constraint | Description |
|-----------|-----------|-------------|
| `TProjection` | `NostifyObject, IProjection, IHasExternalData<TProjection>, new()` | The projection type being initialized |
| `TAggregate` | `NostifyObject, IAggregate, new()` | The aggregate whose current-state container drives ID paging |

## Constructor

```csharp
public DurableProjectionInitializer(
    HttpClient httpClient,
    INostify nostify,
    string instanceId,
    int batchSize = 1000,
    int concurrentBatchCount = 5)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `httpClient` | `HttpClient` | — | HTTP client passed through to `ProjectionInitializer.InitAsync` for external data fetching |
| `nostify` | `INostify` | — | Nostify instance used to access Cosmos containers |
| `instanceId` | `string` | — | Durable orchestration instance ID; only one orchestration with this ID may run at a time. Falls back to `"{nameof(TProjection)}_Init"` if null |
| `batchSize` | `int` | 1000 | Number of aggregate IDs processed per activity invocation |
| `concurrentBatchCount` | `int` | 5 | Number of batches dispatched concurrently per page; page size = `batchSize × concurrentBatchCount` |

## Public Methods

### StartOrchestration

```csharp
Task<HttpResponseData> StartOrchestration(
    HttpRequestData req,
    DurableTaskClient client,
    string orchestratorName)
```

Starts the durable orchestration. Returns **409 Conflict** (with a descriptive message) if an orchestration with the same `instanceId` is already `Running`; otherwise schedules a new orchestration and returns the standard **202 Accepted** check-status response (including a `Location` header and polling URLs).

### CancelOrchestration

```csharp
Task<HttpResponseData> CancelOrchestration(
    HttpRequestData req,
    DurableTaskClient client)
```

Terminates a running orchestration (if any), waits for it to complete, and then purges the instance record. Always returns **200 OK**.

### OrchestrateInitAsync

```csharp
Task OrchestrateInitAsync(
    TaskOrchestrationContext context,
    string deleteActivityName,
    string getTenantIdsActivityName,
    string getIdsActivityName,
    string processBatchActivityName,
    ILogger? logger = null)
```

The orchestrator body. Orchestration steps:

1. Call `deleteActivityName` — deletes all existing projections.
2. Call `getTenantIdsActivityName` — fetches distinct tenant IDs from the aggregate's current-state container.
3. For each tenant, page through aggregate IDs (calling `getIdsActivityName` with `DurableInitPageInfo`) and fan out `processBatchActivityName` calls concurrently (up to `concurrentBatchCount` at a time), with a 3-attempt retry policy (5 s initial delay, 2× backoff).

Pass `context.CreateReplaySafeLogger` for the `logger` argument to avoid duplicate log entries during orchestration replay.

### DeleteAllProjections

```csharp
Task DeleteAllProjections()
```

Deletes every document of type `TProjection` from the bulk projection container. Intended to be called from the delete activity function.

### GetDistinctTenantIds

```csharp
Task<List<Guid>> GetDistinctTenantIds()
```

Queries the `TAggregate` current-state container for all distinct `tenantId` values. Intended to be called from the get-tenant-IDs activity function.

### GetIdsForTenant

```csharp
Task<List<Guid>> GetIdsForTenant(DurableInitPageInfo request)
```

Returns a page of aggregate IDs for a given tenant, ordered by `id` for stable paging. Intended to be called from the get-IDs activity function.

### ProcessBatch

```csharp
Task ProcessBatch(List<Guid> ids)
```

Replays all events for the supplied aggregate IDs, builds `TProjection` instances, and persists them via `ProjectionInitializer.InitAsync`. Intended to be called from the process-batch activity function.

## DurableInitPageInfo

```csharp
public struct DurableInitPageInfo
{
    public readonly Guid TenantId;
    public readonly int PageNumber;
}
```

Lightweight input struct passed to `GetIdsForTenant`. Carries the tenant partition key and the zero-based page number for stable, offset-based paging.

## Usage — Template-generated Activity Class

The `nostifyProjection` template generates a ready-to-use class (`_ProjectionName_DurableInit`) that:

- Injects `HttpClient`, `INostify`, and `ILogger` via constructor DI.
- Constructs a `DurableProjectionInitializer` with `nameof(_ProjectionName_DurableInit)` as the instance ID.
- Exposes a `POST` HTTP trigger to start the orchestration and a `DELETE` HTTP trigger to cancel it.
- Wires the orchestrator and four activity functions (`DeleteAll`, `GetDistinctTenantIds`, `GetIdsForTenant`, `ProcessBatch`) to the corresponding helper methods.

```csharp
public class MyProjectionDurableInit
{
    private readonly DurableProjectionInitializer<MyProjection, MyAggregate> _initializer;

    public MyProjectionDurableInit(HttpClient httpClient, INostify nostify, ILogger<MyProjectionDurableInit> logger)
    {
        _initializer = new DurableProjectionInitializer<MyProjection, MyAggregate>(
            httpClient, nostify, nameof(MyProjectionDurableInit),
            batchSize: 1000, concurrentBatchCount: 5);
    }

    [Function(nameof(MyProjectionDurableInit))]
    public Task<HttpResponseData> Run(
        [HttpTrigger("post", Route = "MyProjectionDurableInit")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
        => _initializer.StartOrchestration(req, client, nameof(OrchestrateMyProjectionDurableInit));

    [Function(nameof(CancelMyProjectionDurableInit))]
    public Task<HttpResponseData> CancelMyProjectionDurableInit(
        [HttpTrigger("delete", Route = "MyProjectionDurableInit")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
        => _initializer.CancelOrchestration(req, client);

    [Function(nameof(OrchestrateMyProjectionDurableInit))]
    public Task OrchestrateMyProjectionDurableInit([OrchestrationTrigger] TaskOrchestrationContext context)
        => _initializer.OrchestrateInitAsync(
            context,
            nameof(DeleteAllMyProjection),
            nameof(GetDistinctTenantIdsMyProjection),
            nameof(GetMyAggregateIdsForTenantMyProjection),
            nameof(ProcessMyProjectionBatch),
            context.CreateReplaySafeLogger<MyProjectionDurableInit>());

    [Function(nameof(DeleteAllMyProjection))]
    public Task DeleteAllMyProjection([ActivityTrigger] TaskActivityContext context)
        => _initializer.DeleteAllProjections();

    [Function(nameof(GetDistinctTenantIdsMyProjection))]
    public Task<List<Guid>> GetDistinctTenantIdsMyProjection([ActivityTrigger] TaskActivityContext context)
        => _initializer.GetDistinctTenantIds();

    [Function(nameof(GetMyAggregateIdsForTenantMyProjection))]
    public Task<List<Guid>> GetMyAggregateIdsForTenantMyProjection([ActivityTrigger] DurableInitPageInfo request)
        => _initializer.GetIdsForTenant(request);

    [Function(nameof(ProcessMyProjectionBatch))]
    public Task ProcessMyProjectionBatch([ActivityTrigger] List<Guid> ids)
        => _initializer.ProcessBatch(ids);
}
```

## Key Relationships

- [IProjection](IProjection.spec.md) — `TProjection` must implement `IProjection` and `IHasExternalData<TProjection>`
- [IAggregate](IAggregate.spec.md) — `TAggregate` provides the event and current-state data
- [IProjectionInitializer](IProjectionInitializer.spec.md) — `ProcessBatch` delegates to `INostify.ProjectionInitializer.InitAsync`
- [INostify](INostify.spec.md) — used for container access (`GetBulkProjectionContainerAsync`, `GetCurrentStateContainerAsync`, `GetEventStoreContainerAsync`)
