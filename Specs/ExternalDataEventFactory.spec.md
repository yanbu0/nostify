# ExternalDataEventFactory Specification

## Overview

`ExternalDataEventFactory<P>` is a fluent builder for gathering external data events during projection initialization. It provides a unified interface for fetching events from both the same service's event store and external services via HTTP.

## Type Parameters

- `P` - The projection type. Must implement:
  - `IProjection` - Base projection interface
  - `IUniquelyIdentifiable` - Provides `id` property of type `Guid`
  - `IApplyable` - Provides `Apply(Event)` method for applying events

## Constructor

```csharp
public ExternalDataEventFactory(
    INostify nostify,
    List<P> projectionsToInit,
    HttpClient? httpClient = null,
    DateTime? pointInTime = null,
    IQueryExecutor? queryExecutor = null)
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `nostify` | `INostify` | Yes | The nostify instance for accessing the event store |
| `projectionsToInit` | `List<P>` | Yes | List of projections to initialize with external data |
| `httpClient` | `HttpClient?` | No | HTTP client for external service calls. Required if using `AddEventRequestors` or `WithEventRequestor` |
| `pointInTime` | `DateTime?` | No | Point in time to query events up to. If null, queries all events |
| `queryExecutor` | `IQueryExecutor?` | No | Query executor for unit testing. Defaults to `CosmosQueryExecutor.Default` |

## Methods

### Same-Service ID Selectors

#### WithSameServiceIdSelectors (non-nullable)

```csharp
public ExternalDataEventFactory<P> WithSameServiceIdSelectors(params Func<P, Guid>[] selectors)
```

Adds selectors for single foreign key IDs from the same service. Non-nullable version for required relationships.

#### WithSameServiceIdSelectors (nullable)

```csharp
public ExternalDataEventFactory<P> WithSameServiceIdSelectors(params Func<P, Guid?>[] selectors)
```

Adds selectors for single nullable foreign key IDs from the same service. Null values are automatically filtered out during event retrieval.

#### WithSameServiceListIdSelectors (non-nullable)

```csharp
public ExternalDataEventFactory<P> WithSameServiceListIdSelectors(params Func<P, List<Guid>>[] selectors)
```

Adds selectors for lists of foreign key IDs from the same service.

#### WithSameServiceListIdSelectors (nullable)

```csharp
public ExternalDataEventFactory<P> WithSameServiceListIdSelectors(params Func<P, List<Guid?>>[] selectors)
```

Adds selectors for lists of nullable foreign key IDs. Null values within the lists are automatically filtered out.

### Dependant ID Selectors

These selectors are evaluated after the first round of events are applied to projections, allowing you to fetch events for IDs that weren't known until the first events were processed.

#### WithSameServiceDependantIdSelectors (non-nullable)

```csharp
public ExternalDataEventFactory<P> WithSameServiceDependantIdSelectors(params Func<P, Guid>[] selectors)
```

#### WithSameServiceDependantIdSelectors (nullable)

```csharp
public ExternalDataEventFactory<P> WithSameServiceDependantIdSelectors(params Func<P, Guid?>[] selectors)
```

#### WithSameServiceDependantListIdSelectors (non-nullable)

```csharp
public ExternalDataEventFactory<P> WithSameServiceDependantListIdSelectors(params Func<P, List<Guid>>[] selectors)
```

#### WithSameServiceDependantListIdSelectors (nullable)

```csharp
public ExternalDataEventFactory<P> WithSameServiceDependantListIdSelectors(params Func<P, List<Guid?>>[] selectors)
```

### External Service Event Requestors

#### AddEventRequestors

```csharp
public ExternalDataEventFactory<P> AddEventRequestors(params EventRequester<P>[] eventRequestors)
```

Adds event requestors for fetching events from external services.

**Throws:** `InvalidOperationException` if `httpClient` was not provided in constructor.

#### WithEventRequestor

```csharp
public ExternalDataEventFactory<P> WithEventRequestor(string serviceUrl, params Func<P, Guid?>[] foreignIdSelectors)
```

Convenience method to add a single external service requestor.

**Throws:** `InvalidOperationException` if `httpClient` was not provided in constructor.

#### AddDependantEventRequestors

```csharp
public ExternalDataEventFactory<P> AddDependantEventRequestors(params EventRequester<P>[] eventRequestors)
```

Adds event requestors that are evaluated after first-round events are applied.

#### WithDependantEventRequestor

```csharp
public ExternalDataEventFactory<P> WithDependantEventRequestor(string serviceUrl, params Func<P, Guid?>[] foreignIdSelectors)
```

Convenience method to add a single dependant external service requestor.

### GetEventsAsync

```csharp
public async Task<List<ExternalDataEvent>> GetEventsAsync()
```

Executes all configured selectors and requestors and returns the collected events.

**Execution Order:**
1. Same-service single ID selectors (non-nullable and nullable)
2. Same-service list ID selectors (non-nullable and nullable)
3. External service requestors
4. Dependant same-service selectors (after applying initial events)
5. Dependant external service requestors (after applying initial events)

## Internal Storage

The factory maintains separate lists for non-nullable and nullable selectors:

```csharp
// Non-nullable selectors
private List<Func<P, Guid>> _foreignKeySelectors;
private List<Func<P, List<Guid>>> _foreignKeyListSelectors;
private List<Func<P, Guid>> _dependantIdSelectors;
private List<Func<P, List<Guid>>> _dependantListIdSelectors;

// Nullable selectors
private List<Func<P, Guid?>> _nullableForeignKeySelectors;
private List<Func<P, List<Guid?>>> _nullableForeignKeyListSelectors;
private List<Func<P, Guid?>> _nullableDependantIdSelectors;
private List<Func<P, List<Guid?>>> _nullableDependantListIdSelectors;

// External requestors
private EventRequester<P>[] _eventRequestors;
private EventRequester<P>[] _dependantEventRequestors;
```

## Null Handling

- **Nullable single ID selectors:** Filtered via `id.HasValue && id.Value != Guid.Empty`
- **Nullable list ID selectors:** Each element filtered via `id.HasValue && id.Value != Guid.Empty`
- **Selector exceptions:** Caught silently, ID skipped (handles null reference exceptions)
- **Projection deserialization:** If `DeserializeObject<P>` returns null, projection is skipped

## Fluent API

All selector and requestor methods return `this` for fluent chaining:

```csharp
var events = await new ExternalDataEventFactory<OrderProjection>(nostify, projections, httpClient)
    .WithSameServiceIdSelectors(p => p.CustomerId)           // required FK
    .WithSameServiceIdSelectors(p => p.AssignedAgentId)      // optional FK (nullable)
    .WithSameServiceListIdSelectors(p => p.ProductIds)       // list of required FKs
    .WithSameServiceDependantIdSelectors(p => p.WarehouseId) // populated by first-round events
    .WithEventRequestor("https://inventory/api/events", p => p.ProductId)
    .GetEventsAsync();
```

## Usage Examples

### Basic Usage with Non-nullable Selectors

```csharp
var factory = new ExternalDataEventFactory<CustomerOrderProjection>(
    nostify,
    projectionsToInit);

factory.WithSameServiceIdSelectors(p => p.CustomerId, p => p.ProductId);

var events = await factory.GetEventsAsync();
```

### Mixed Nullable and Non-nullable Selectors

```csharp
var events = await new ExternalDataEventFactory<OrderProjection>(nostify, projections)
    .WithSameServiceIdSelectors(p => p.CustomerId)           // Guid - required
    .WithSameServiceIdSelectors(p => p.AssignedAgentId)      // Guid? - optional, nulls filtered
    .WithSameServiceListIdSelectors(p => p.TagIds)           // List<Guid?> - nulls in list filtered
    .GetEventsAsync();
```

### With External Services

```csharp
var events = await new ExternalDataEventFactory<OrderProjection>(nostify, projections, httpClient)
    .WithSameServiceIdSelectors(p => p.CustomerId)
    .WithEventRequestor("https://inventory-service/api/events", p => p.ProductId)
    .WithEventRequestor("https://shipping-service/api/events", p => p.ShippingMethodId)
    .GetEventsAsync();
```

### With Dependant Selectors

```csharp
// ShippingAddressId is populated by Customer events
var events = await new ExternalDataEventFactory<OrderProjection>(nostify, projections)
    .WithSameServiceIdSelectors(p => p.CustomerId)
    .WithSameServiceDependantIdSelectors(p => p.ShippingAddressId) // Available after Customer events applied
    .GetEventsAsync();
```

### Point-in-Time Queries

```csharp
var historicalEvents = await new ExternalDataEventFactory<OrderProjection>(
    nostify,
    projections,
    pointInTime: new DateTime(2025, 1, 1))
    .WithSameServiceIdSelectors(p => p.CustomerId)
    .GetEventsAsync();
```

## Testing

Use `InMemoryQueryExecutor` for unit testing:

```csharp
var factory = new ExternalDataEventFactory<TestProjection>(
    mockNostify.Object,
    projections,
    queryExecutor: InMemoryQueryExecutor.Default);
```

## Related Classes

- [`ExternalDataEvent`](ExternalDataEvent.spec.md) - The data structure returned by this factory
- [`EventRequester<P>`](EventRequester.spec.md) - Configuration for external service requests
- [`EventFactory`](EventFactory.spec.md) - Factory for creating Event instances (different purpose)

## Version History

- **4.3.0** - Added nullable `Guid?` overloads for all selector methods; all methods now return `this` for fluent chaining
- **4.1.0** - Initial release with basic selector support
