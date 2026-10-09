# DurableProjectionInitializer Specification

## Overview

`DurableProjectionInitializer<TProjection, TAggregate>` coordinates destructive and rolling projection rebuilds using Azure Durable Functions orchestration. It is the recommended approach for large datasets where a single Azure Function execution would time out before all projections are initialized.

The class breaks the work into paged, concurrent batches partitioned by either tenant ID or an arbitrary Cosmos partition key, enforces that only one rebuild can run at a time (via a fixed orchestration instance ID), and exposes activity-level helper methods so the host Azure Function class remains thin.

Four orchestrator entry points are provided so the class works with either rebuild policy and any supported container partitioning scheme:

- `OrchestrateInitAsync` — destructive rebuild for aggregates partitioned by `tenantId` (`Guid`).
- `OrchestrateInitByPartitionAsync` — destructive rebuild for any string-valued partition key.
- `OrchestrateRollingInitAsync` — non-destructive rolling rebuild for `tenantId` partitioning.
- `OrchestrateRollingInitByPartitionAsync` — non-destructive rolling rebuild for any string-valued partition key.

The existing destructive APIs and all non-Durable initialization APIs remain unchanged. Rolling initialization is available only through `DurableProjectionInitializer` and its generated Durable Functions.

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
    int concurrentBatchCount = 5,
    TaskOptions? durableTaskOptions = null,
    RetryOptions? cosmosRetryOptions = null)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `httpClient` | `HttpClient` | — | HTTP client passed through to `ProjectionInitializer.InitAsync` for external data fetching |
| `nostify` | `INostify` | — | Nostify instance used to access Cosmos containers |
| `instanceId` | `string` | — | Durable orchestration instance ID; only one orchestration with this ID may run at a time. Falls back to `"{nameof(TProjection)}_Init"` if null |
| `batchSize` | `int` | 1000 | Number of aggregate IDs processed per activity invocation |
| `concurrentBatchCount` | `int` | 5 | Number of batches dispatched concurrently per page; page size = `batchSize × concurrentBatchCount` |
| `durableTaskOptions` | `TaskOptions?` | null | Durable retry policy applied to each activity call the orchestrator makes. When null, defaults to 3 attempts / 5 s initial delay / 2× backoff |
| `cosmosRetryOptions` | `RetryOptions?` | null | Retry policy for the individual Cosmos reads and writes performed inside activity methods. When null, default `RetryOptions` are used (3 retries, 1 s delay, 2× backoff) |

## Public Methods

### StartOrchestration

```csharp
Task<HttpResponseData> StartOrchestration(
    HttpRequestData req,
    DurableTaskClient client,
    string orchestratorName)
```

Starts the durable orchestration. Returns **409 Conflict** (with a descriptive message) if an orchestration with the same `instanceId` is active (`Running`, `Pending`, or `Suspended`); otherwise schedules a new orchestration and returns the standard **202 Accepted** check-status response (including a `Location` header and polling URLs).

### StartRollingOrchestration

```csharp
Task<HttpResponseData> StartRollingOrchestration(
    HttpRequestData req,
    DurableTaskClient client,
    string orchestratorName,
    DurableRollingProjectionInput input)
```

Validates the rolling options, applies the same fixed-instance concurrency check as `StartOrchestration`, and schedules the rolling orchestrator with immutable serialized input. It returns **409 Conflict** when the instance is `Running`, `Pending`, or `Suspended`; otherwise it returns the standard **202 Accepted** check-status response.

### CancelOrchestration

```csharp
Task<HttpResponseData> CancelOrchestration(
    HttpRequestData req,
    DurableTaskClient client)
```

Terminates an active orchestration (if any), waits for it to complete, and then purges the completed instance record. A suspended instance is resumed before termination so cancellation can complete. Always returns **200 OK**.

### OrchestrateInitAsync

```csharp
Task OrchestrateInitAsync(
    TaskOrchestrationContext context,
    DurableTenantInitActivityNames activities,
    ILogger? logger = null)

Task OrchestrateInitAsync(
    TaskOrchestrationContext context,
    string deleteActivityName,
    string getTenantIdsActivityName,
    string getIdsActivityName,
    string processBatchActivityName,
    ILogger? logger = null)
```

Tenant-partitioned orchestrator body. Prefer the `DurableTenantInitActivityNames` overload, which groups the four related activity names and validates that none are blank. The positional-string overload remains supported and delegates to the typed overload.

Use this orchestration when the aggregate's current-state container is partitioned by `tenantId`. Its steps are:

1. Call the delete activity to remove existing projections.
2. Call the tenant-ID activity to fetch distinct tenant IDs (`List<Guid>`) from the aggregate's current-state container.
3. For each tenant, page through aggregate IDs (calling the get-IDs activity with `DurableInitPageInfo`) and fan out process activity calls concurrently (up to `concurrentBatchCount` at a time).

The process activity payload remains `List<Guid>`. Tenant partitioning scopes aggregate-ID discovery in the current-state container; events are subsequently retrieved by `aggregateRootId`, which is the event store's physical partition key. No tenant value is propagated through the process activity or event-retrieval transports.

By default, each activity is made with a 3-attempt retry policy (5 s initial delay, 2× backoff). Pass `context.CreateReplaySafeLogger` for the `logger` argument to report deletion, tenant count, processed count, and completion without duplicate log entries during orchestration replay.

### OrchestrateInitByPartitionAsync

```csharp
Task OrchestrateInitByPartitionAsync(
    TaskOrchestrationContext context,
    string deleteActivityName,
    string getPartitionKeysActivityName,
    string getIdsActivityName,
    string processBatchActivityName,
    ILogger? logger = null)
```

General partition-key orchestrator body. Use this overload when the aggregate's current-state container is partitioned by something other than `tenantId` (e.g. organization ID, region, composite key). Orchestration steps mirror `OrchestrateInitAsync` but:

1. `getPartitionKeysActivityName` returns `List<string>` (the distinct partition key values).
2. `getIdsActivityName` is invoked with a `DurablePartitionInitPageInfo` input rather than `DurableInitPageInfo`.

Both overloads share the same paging, chunking, retry, and concurrency behavior.

### Rolling Orchestrations

```csharp
Task OrchestrateRollingInitAsync(
    TaskOrchestrationContext context,
    DurableRollingTenantInitActivityNames activities,
    DurableRollingProjectionInput input,
    ILogger? logger = null)

Task OrchestrateRollingInitByPartitionAsync(
    TaskOrchestrationContext context,
    string getPartitionKeysActivityName,
    string getIdsActivityName,
    string processBatchActivityName,
    DurableRollingProjectionInput input,
    ILogger? logger = null)
```

Rolling orchestrations use the same aggregate discovery, stable paging, chunking, Durable activity retries, and concurrency limits as destructive orchestration. They never invoke a delete activity. Their process activities receive `DurableRollingProjectionBatch`, whose work items include both aggregate ID and physical partition key so activities can use Cosmos point reads and conditional writes.

Rolling orchestration does not remove orphaned projection documents that no longer correspond to a current aggregate. Documents may temporarily contain mixed old and rebuilt values while a run is in progress.

### DeleteAllProjections

```csharp
Task DeleteAllProjections(DurableTaskClient? client = null)
```

Deletes every document of type `TProjection` from the bulk projection container. When a Durable client is supplied, the method returns without touching the projection container if cancellation has been requested. Intended to be called from the delete activity function.

### GetDistinctTenantIds

```csharp
Task<List<Guid>> GetDistinctTenantIds()
```

Queries the `TAggregate` current-state container for all distinct `tenantId` values. Intended to be called from the get-tenant-IDs activity function used with `OrchestrateInitAsync`.

### GetDistinctPartitionKeys

```csharp
Task<List<string>> GetDistinctPartitionKeys<TKey>(
    Expression<Func<TAggregate, TKey>> partitionKeySelector)
```

Queries the `TAggregate` current-state container for distinct values of an arbitrary partition key property (selected via expression, e.g. `x => x.organizationId`) and returns them as a `List<string>` for serialization across activity boundaries. Null values are projected to empty strings. Intended to be called from the get-partition-keys activity function used with `OrchestrateInitByPartitionAsync`.

### GetIdsForTenant

```csharp
Task<List<Guid>> GetIdsForTenant(DurableInitPageInfo request)
```

Returns a page of aggregate IDs for a given tenant, ordered by `id` for stable paging. Internally delegates to `GetIdsForPartition(request.TenantId.ToPartitionKey(), request.PageNumber)`. Intended to be called from the get-IDs activity function used with `OrchestrateInitAsync`.

### GetIdsForPartition

```csharp
Task<List<Guid>> GetIdsForPartition(DurablePartitionInitPageInfo request)
Task<List<Guid>> GetIdsForPartition(PartitionKey partitionKey, int pageNumber)
```

Returns a page of aggregate IDs for an arbitrary Cosmos partition, ordered by `id` for stable paging. The `DurablePartitionInitPageInfo` overload is the activity-friendly wrapper (constructs a `PartitionKey` from the string value); the `PartitionKey`/`int` overload is the canonical implementation. Both `GetIdsForTenant` and the `DurablePartitionInitPageInfo` overload delegate here so all paging logic lives in one place.

### ProcessBatch

```csharp
Task ProcessBatch(List<Guid> ids, DurableTaskClient? client = null)
```

Retrieves events for the supplied aggregate IDs through `IQueryExecutor`, builds `TProjection` instances, and persists them via `ProjectionInitializer.InitAsync`. Each aggregate stream is replayed in ascending `timestamp` order with `id` as a deterministic tie-breaker. When a Durable client is supplied, cancellation is checked before event retrieval and again before persistence. The method is intended to be called from the process-batch activity function shared by both destructive orchestrators.

### ProcessRollingBatch

```csharp
Task ProcessRollingBatch(
    DurableRollingProjectionBatch batch,
    DurableTaskClient? client = null)
```

Processes each work item independently and observes cancellation between items. Events are queried by aggregate root ID and ordered deterministically by `timestamp`, then event `id`. Base events are replayed first; external events are discovered from the complete base-event shadow and replayed second, matching normal initialization semantics.

In full mode, the rebuilt projection is ETag-replaced when the target exists or created when absent. In selective mode:

1. A complete shadow is built from base events before external-data discovery, so selectors may depend on properties outside the selected set.
2. Queried events are not mutated. Relevant events are cloned, and their payloads are reduced to `id` plus selected properties.
3. Existing documents preserve unselected fields and `initialized`. Selected values are merged into the latest point-read resource and written with an ETag-guarded replace.
4. Missing targets are fully reconstructed and created as complete projections. The point-read happens before replay, so a missing target selects the full property set up front and each attempt performs only one event-store query and one external-data lookup.

Cosmos patch operations do not support `IfMatchEtag`; therefore the conditional selective write is a merged replace rather than a patch. HTTP 409 and 412 responses consume the bounded ETag retry budget and use exponential backoff. After exhaustion, full mode performs an unconditional upsert while selective mode performs an unconditional patch of selected paths only. A structured warning identifies this hot-document fallback. HTTP 429 handling uses the independent Cosmos retry policy and does not consume conflict attempts; after the configured Cosmos retry count is exhausted, the 429 response propagates to fail the activity.

## Rolling Contracts

`DurableRollingProjectionOptions` contains normalized `SelectedProperties`, `MaxEtagRetries`, `InitialBackoff`, `BackoffCoefficient`, and `PartitionKeyPath`. An empty selected-property list means full mode. Selected properties must be writable public projection properties and cannot be identity, partition, initialization, TTL, or Cosmos system fields.

`DurableRollingProjectionInput` wraps those options for orchestration serialization. `DurableRollingProjectionWorkItem` carries an aggregate ID, serialized physical partition-key value, and a flag identifying GUID partition keys. `DurableRollingProjectionBatch` combines work items with options. `DurableRollingTenantInitActivityNames` groups the three rolling tenant activity names; unlike destructive orchestration, it has no delete activity.

## DurableTenantInitActivityNames

```csharp
public sealed class DurableTenantInitActivityNames
{
    public DurableTenantInitActivityNames(
        string delete,
        string getTenantIds,
        string getIds,
        string processBatch);

    public string Delete { get; }
    public string GetTenantIds { get; }
    public string GetIds { get; }
    public string ProcessBatch { get; }
}
```

Strongly typed grouping for tenant-orchestration activity names. Its constructor rejects null, empty, or whitespace names, reducing positional-string wiring mistakes without changing Durable Function names or serialized activity payloads.

## DurableInitPageInfo

```csharp
public struct DurableInitPageInfo
{
    public readonly Guid TenantId;
    public readonly int PageNumber;
}
```

Lightweight input struct passed to `GetIdsForTenant`. Carries the tenant partition key (`Guid`) and the zero-based page number for stable, offset-based paging.

## DurablePartitionInitPageInfo

```csharp
public struct DurablePartitionInitPageInfo
{
    public readonly string PartitionKey;
    public readonly int PageNumber;
}
```

Lightweight input struct passed to `GetIdsForPartition`. Carries the partition key value as a `string` (so it can be serialized across Durable Function activity boundaries) and the zero-based page number. Reconstruct a `Microsoft.Azure.Cosmos.PartitionKey` via `new PartitionKey(request.PartitionKey)` if you need the strongly-typed value directly.

## Usage — Template-generated Initializer Class (Tenant-partitioned)

The `nostifyProjection` template generates a ready-to-use class (`_ProjectionName_Init`) that:

- Injects `HttpClient` and `INostify` via constructor DI.
- Constructs a `DurableProjectionInitializer` with the generated initializer name as the instance ID.
- Exposes the existing destructive `POST` endpoint, a rolling `POST .../rolling` endpoint, and the existing `DELETE` cancellation endpoint.
- Wires the destructive orchestrator and activities without changing their contracts.
- Wires a rolling orchestrator and `ProcessRollingBatch` activity that reuse tenant and aggregate-ID discovery but omit deletion.

```csharp
public class MyProjectionDurableInit
{
    private readonly DurableProjectionInitializer<MyProjection, MyAggregate> _initializer;

    public MyProjectionDurableInit(HttpClient httpClient, INostify nostify)
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
            new DurableTenantInitActivityNames(
                nameof(DeleteAllMyProjection),
                nameof(GetDistinctTenantIdsMyProjection),
                nameof(GetMyAggregateIdsForTenantMyProjection),
                nameof(ProcessMyProjectionBatch)),
            context.CreateReplaySafeLogger<MyProjectionDurableInit>());

    [Function(nameof(DeleteAllMyProjection))]
    public Task DeleteAllMyProjection(
        [ActivityTrigger] TaskActivityContext context,
        [DurableClient] DurableTaskClient client)
        => _initializer.DeleteAllProjections(client);

    [Function(nameof(GetDistinctTenantIdsMyProjection))]
    public Task<List<Guid>> GetDistinctTenantIdsMyProjection([ActivityTrigger] TaskActivityContext context)
        => _initializer.GetDistinctTenantIds();

    [Function(nameof(GetMyAggregateIdsForTenantMyProjection))]
    public Task<List<Guid>> GetMyAggregateIdsForTenantMyProjection([ActivityTrigger] DurableInitPageInfo request)
        => _initializer.GetIdsForTenant(request);

    [Function(nameof(ProcessMyProjectionBatch))]
    public Task ProcessMyProjectionBatch(
        [ActivityTrigger] List<Guid> ids,
        [DurableClient] DurableTaskClient client)
        => _initializer.ProcessBatch(ids, client);

    [Function(nameof(RollingMyProjectionDurableInit))]
    public Task<HttpResponseData> RollingMyProjectionDurableInit(
        [HttpTrigger("post", Route = "MyProjectionDurableInit/rolling")] HttpRequestData req,
        [FromBody] DurableRollingProjectionInput input,
        [DurableClient] DurableTaskClient client)
        => _initializer.StartRollingOrchestration(
            req, client, nameof(OrchestrateRollingMyProjectionDurableInit), input);

    [Function(nameof(OrchestrateRollingMyProjectionDurableInit))]
    public Task OrchestrateRollingMyProjectionDurableInit(
        [OrchestrationTrigger] TaskOrchestrationContext context)
        => _initializer.OrchestrateRollingInitAsync(
            context,
            new DurableRollingTenantInitActivityNames(
                nameof(GetDistinctTenantIdsMyProjection),
                nameof(GetMyAggregateIdsForTenantMyProjection),
                nameof(ProcessRollingMyProjectionBatch)),
            context.GetInput<DurableRollingProjectionInput>()
                ?? new DurableRollingProjectionInput(),
            context.CreateReplaySafeLogger<MyProjectionDurableInit>());

    [Function(nameof(ProcessRollingMyProjectionBatch))]
    public Task ProcessRollingMyProjectionBatch(
        [ActivityTrigger] DurableRollingProjectionBatch batch,
        [DurableClient] DurableTaskClient client)
        => _initializer.ProcessRollingBatch(batch, client);
}
```

## Usage — Arbitrary Partition Key

For aggregates whose container is partitioned by a property other than `tenantId`, use `OrchestrateInitByPartitionAsync` and the partition-based helpers:

```csharp
public class MyProjectionDurableInit
{
    private readonly DurableProjectionInitializer<MyProjection, MyAggregate> _initializer;

    public MyProjectionDurableInit(HttpClient httpClient, INostify nostify)
    {
        _initializer = new DurableProjectionInitializer<MyProjection, MyAggregate>(
            httpClient, nostify, nameof(MyProjectionDurableInit));
    }

    [Function(nameof(OrchestrateMyProjectionDurableInit))]
    public Task OrchestrateMyProjectionDurableInit([OrchestrationTrigger] TaskOrchestrationContext context)
        => _initializer.OrchestrateInitByPartitionAsync(
            context,
            nameof(DeleteAllMyProjection),
            nameof(GetMyAggregatePartitionKeys),
            nameof(GetMyAggregateIdsForPartition),
            nameof(ProcessMyProjectionBatch),
            context.CreateReplaySafeLogger<MyProjectionDurableInit>());

    [Function(nameof(GetMyAggregatePartitionKeys))]
    public Task<List<string>> GetMyAggregatePartitionKeys([ActivityTrigger] TaskActivityContext context)
        => _initializer.GetDistinctPartitionKeys(x => x.organizationId);

    [Function(nameof(GetMyAggregateIdsForPartition))]
    public Task<List<Guid>> GetMyAggregateIdsForPartition([ActivityTrigger] DurablePartitionInitPageInfo request)
        => _initializer.GetIdsForPartition(request);

    // DeleteAllMyProjection, ProcessMyProjectionBatch, StartOrchestration, CancelOrchestration
    // are the same as the tenant-partitioned example above.
}
```

## Key Relationships

- [IProjection](IProjection.spec.md) — `TProjection` must implement `IProjection` and `IHasExternalData<TProjection>`
- [IAggregate](IAggregate.spec.md) — `TAggregate` provides the event and current-state data
- [IProjectionInitializer](IProjectionInitializer.spec.md) — `ProcessBatch` delegates to `INostify.ProjectionInitializer.InitAsync`
- [INostify](INostify.spec.md) — used for container access (`GetBulkProjectionContainerAsync`, `GetCurrentStateContainerAsync`, `GetEventStoreContainerAsync`)
