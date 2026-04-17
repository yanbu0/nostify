# IProjectionInitializer Interface Specification

## Overview

`IProjectionInitializer` defines the contract for initializing projections with external data and managing projection containers. Methods are available for all-partition, partition-key-scoped, and tenant-scoped scenarios.

## Interface Definition

```csharp
public interface IProjectionInitializer
```

## Methods

### InitAsync (Single ID)

```csharp
Task<List<P>> InitAsync<P, A>(Guid id, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
```

Initializes a single projection by querying all needed data from all services across all partitions.

### InitAsync (Single ID, PartitionKey)

```csharp
Task<List<P>> InitAsync<P, A>(Guid id, PartitionKey partitionKey, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
```

Initializes a single projection scoped to the given Cosmos DB `PartitionKey`.

### InitAsync (Single ID, TenantId)

```csharp
Task<List<P>> InitAsync<P, A>(Guid id, Guid tenantId, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
```

Convenience overload — constructs a `PartitionKey` from `tenantId` and delegates to the `PartitionKey` overload.

### InitAsync (Multiple IDs)

```csharp
Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
```

Initializes multiple projections by their IDs across all partitions.

### InitAsync (Multiple IDs, PartitionKey)

```csharp
Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, PartitionKey partitionKey, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
```

Initializes multiple projections scoped to the given `PartitionKey`.

### InitAsync (Multiple IDs, TenantId)

```csharp
Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, Guid tenantId, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
```

Convenience overload — constructs a `PartitionKey` from `tenantId` and delegates to the `PartitionKey` overload.

### InitAsync (Existing Objects)

```csharp
Task<List<P>> InitAsync<P>(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
```

Initializes existing projection objects by fetching external data events, applying them, setting `initialized = true`, and upserting to the container.

### InitContainerAsync (All Partitions)

```csharp
Task InitContainerAsync<P, A>(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000, DateTime? pointInTime = null)
```

Deletes **all** items in the projection container and rebuilds from the base aggregate event store. Destructive operation.

### InitContainerAsync (PartitionKey)

```csharp
Task InitContainerAsync<P, A>(INostify nostify, PartitionKey partitionKey, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000, DateTime? pointInTime = null)
```

Deletes and rebuilds only the items in the specified logical partition. Non-destructive to other partitions.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nostify` | `INostify` | — | Nostify singleton |
| `partitionKey` | `PartitionKey` | — | Cosmos DB partition key value to scope the operation |
| `httpClient` | `HttpClient?` | `null` | Optional HttpClient for external data requests |
| `partitionKeyPath` | `string` | `"/tenantId"` | Container partition key path |
| `loopSize` | `int` | `1000` | Batch size for processing |
| `pointInTime` | `DateTime?` | `null` | Replay events up to this timestamp |

### InitContainerAsync (TenantId)

```csharp
Task InitContainerAsync<P, A>(INostify nostify, Guid tenantId, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000, DateTime? pointInTime = null)
```

Convenience overload — constructs a `PartitionKey` from `tenantId` and delegates to the `PartitionKey` overload.

### InitAllUninitialized (All Partitions)

```csharp
Task InitAllUninitialized<P>(INostify nostify, HttpClient? httpClient = null, int maxloopSize = 10, DateTime? pointInTime = null)
```

Finds and initializes all projections where `initialized == false` across all partitions.

### InitAllUninitialized (PartitionKey)

```csharp
Task InitAllUninitialized<P>(INostify nostify, PartitionKey partitionKey, HttpClient? httpClient = null, int maxloopSize = 100, DateTime? pointInTime = null)
```

Finds and initializes all uninitialized projections within the specified logical partition.

### InitAllUninitialized (TenantId)

```csharp
Task InitAllUninitialized<P>(INostify nostify, Guid tenantId, HttpClient? httpClient = null, int maxloopSize = 100, DateTime? pointInTime = null)
```

Convenience overload — constructs a `PartitionKey` from `tenantId` and delegates to the `PartitionKey` overload.

## Type Constraints

All generic methods require:
- `P : NostifyObject` — Must extend NostifyObject
- `P : IProjection` — Must implement IProjection
- `P : IHasExternalData<P>` — Must implement IHasExternalData
- `P : new()` — Must have a parameterless constructor
- `A : IAggregate` — (where used) Must implement IAggregate

## Design: DRY Delegation

The `PartitionKey` overloads are the primary implementations. The `Guid tenantId` overloads are thin convenience wrappers that construct a `PartitionKey` and delegate:

```
tenantId overload
    └─▶ new PartitionKey(tenantId.ToString())
            └─▶ PartitionKey overload (primary implementation)
```

This ensures all filtering logic lives in one place (the `PartitionKey` overloads) and the tenantId overloads stay DRY.

## Usage Examples

### Initialize a Single Projection (Any Partition Key)

```csharp
// Using a PartitionKey directly (works for any partition key schema)
var pk = new PartitionKey(somePartitionValue);
var result = await nostify.ProjectionInitializer
    .InitAsync<OrderProjection, Order>(orderId, pk, nostify, httpClient);

// Using tenantId convenience overload
var result = await nostify.ProjectionInitializer
    .InitAsync<OrderProjection, Order>(orderId, tenantId, nostify, httpClient);
```

### Rebuild Container for a Single Tenant

```csharp
// Scoped to one partition — does not affect other tenants
await nostify.ProjectionInitializer
    .InitContainerAsync<OrderProjection, Order>(nostify, tenantId, httpClient);

// Equivalent using PartitionKey (e.g., for a non-tenantId partition key)
var pk = new PartitionKey(regionCode);
await nostify.ProjectionInitializer
    .InitContainerAsync<RegionProjection, Region>(nostify, pk, httpClient, partitionKeyPath: "/regionCode");
```

### Rebuild Entire Container (All Partitions)

```csharp
// Destructive — rebuilds ALL partitions
await nostify.ProjectionInitializer
    .InitContainerAsync<OrderProjection, Order>(nostify, httpClient);
```

### Initialize Uninitialized Projections for a Tenant

```csharp
await nostify.ProjectionInitializer
    .InitAllUninitialized<OrderProjection>(nostify, tenantId, httpClient);
```

## Initialization Flow (PartitionKey-Scoped)

```
┌──────────────────────────────────┐
│ InitAsync / InitContainerAsync   │
│ (PartitionKey partitionKey)      │
└──────────────┬───────────────────┘
               │
               ▼
   ┌───────────────────────┐
   │ QueryRequestOptions   │
   │ { PartitionKey = pk } │
   └──────────┬────────────┘
              │
              ▼
   ┌───────────────────────┐
   │ GetItemLinqQueryable  │
   │ (scoped to partition) │
   └──────────┬────────────┘
              │
              ▼
   ┌───────────────────────┐
   │ Apply Events          │
   │ Fetch External Data   │
   │ Set initialized=true  │
   └──────────┬────────────┘
              │
              ▼
   ┌───────────────────────┐
   │ DoBulkUpsertAsync     │
   └───────────────────────┘
```

## Best Practices

1. **Prefer scoped overloads** — Use `PartitionKey` or `tenantId` overloads when you only need to rebuild one partition; this avoids touching other tenants' data.
2. **Use list overloads** — Batch operations are more efficient than single-item calls.
3. **Error Handling** — Handle external service failures in `GetExternalDataEventsAsync`.
4. **Idempotency** — `initialized` flag prevents double-initialization; methods are safe to retry.
5. **Custom partition keys** — Use the `PartitionKey` overloads when the container's partition key is not `/tenantId`.

## Related Types

- [IProjection](IProjection.spec.md) - Projection interface
- [ExternalDataEventFactory](ExternalDataEventFactory.spec.md) - External data handling
- [ExternalDataEvent](ExternalDataEvent.spec.md) - Event container

