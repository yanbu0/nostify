# ExternalDataEvent Specification

## Overview

`ExternalDataEvent` represents a collection of events queried from an external data source to update a projection. It groups events by the projection's aggregate root ID, allowing batch application of events during projection initialization.

## Class Definition

```csharp
public class ExternalDataEvent
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `aggregateRootId` | `Guid` | The ID of the projection (aggregate root) these events belong to |
| `events` | `List<Event>` | List of events to apply to the projection |

## Constructor

```csharp
public ExternalDataEvent(Guid aggregateRootId, List<Event> events = null)
```

Creates a new `ExternalDataEvent` instance. If `events` is null, initializes to an empty list.

## Static Methods

### GetEventsAsync (Container-based)

Multiple overloads for fetching events from the same service's Cosmos DB event store.

#### Nullable Single ID Selectors (Primary)

```csharp
public static async Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(
    Container eventStore,
    List<TProjection> projectionsToInit,
    IQueryExecutor queryExecutor,
    DateTime? pointInTime = null,
    params Func<TProjection, Guid?>[] foreignIdSelectors)
    where TProjection : IUniquelyIdentifiable
```

This is the primary implementation. Null values are filtered via `foreignId.HasValue`.

#### Non-nullable Single ID Selectors

```csharp
public static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(
    Container eventStore,
    List<TProjection> projectionsToInit,
    IQueryExecutor queryExecutor,
    DateTime? pointInTime = null,
    params Func<TProjection, Guid>[] foreignIdSelectors)
    where TProjection : IUniquelyIdentifiable
```

Converts non-nullable selectors to nullable and delegates to the primary method.

#### Nullable List ID Selectors

```csharp
public static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(
    Container eventStore,
    List<TProjection> projectionsToInit,
    IQueryExecutor queryExecutor,
    DateTime? pointInTime = null,
    params Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
    where TProjection : IUniquelyIdentifiable
```

Transforms list selectors into individual `Guid?` selectors using `TransformForeignIdSelectors`.

#### Non-nullable List ID Selectors

```csharp
public static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(
    Container eventStore,
    List<TProjection> projectionsToInit,
    IQueryExecutor queryExecutor,
    DateTime? pointInTime = null,
    params Func<TProjection, List<Guid>>[] foreignIdSelectorsList)
    where TProjection : IUniquelyIdentifiable
```

Converts to `List<Guid?>` and delegates.

### GetEventsAsync (HTTP-based)

For fetching events from external services via HTTP.

#### Nullable Single ID Selectors

```csharp
public static async Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(
    HttpClient httpClient,
    string url,
    List<TProjection> projectionsToInit,
    DateTime? pointInTime = null,
    params Func<TProjection, Guid?>[] foreignIdSelectors)
    where TProjection : IUniquelyIdentifiable
```

#### Non-nullable Single ID Selectors

```csharp
public static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(
    HttpClient httpClient,
    string url,
    List<TProjection> projectionsToInit,
    DateTime? pointInTime = null,
    params Func<TProjection, Guid>[] foreignIdSelectors)
    where TProjection : IUniquelyIdentifiable
```

#### Nullable List ID Selectors

```csharp
public static async Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(
    HttpClient httpClient,
    string url,
    List<TProjection> projectionsToInit,
    DateTime? pointInTime = null,
    params Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
    where TProjection : IUniquelyIdentifiable
```

### GetMultiServiceEventsAsync

Fetches events from multiple external services in parallel.

```csharp
public static async Task<List<ExternalDataEvent>> GetMultiServiceEventsAsync<TProjection>(
    HttpClient httpClient,
    List<TProjection> projectionsToInit,
    DateTime? pointInTime,
    params EventRequester<TProjection>[] eventRequestors)
    where TProjection : IUniquelyIdentifiable
```

## Null Handling

The core null handling happens in the primary `GetEventsAsync` method:

```csharp
var foreignIds =
    from p in projectionsToInit
    from f in foreignIdSelectors
    let foreignId = f(p)
    where foreignId.HasValue      // <-- Filters out null Guid?
    select foreignId!.Value;
```

This pattern:
1. Iterates all projections and selectors
2. Evaluates each selector for each projection
3. Filters out null values via `HasValue`
4. Extracts the non-null `Value`
5. Applies `Distinct()` to avoid duplicate queries

## List Selector Transformation

The `TransformForeignIdSelectors` helper method converts list selectors to individual `Guid?` selectors:

```csharp
private static Func<TProjection, Guid?>[] TransformForeignIdSelectors<TProjection>(
    List<TProjection> projectionsToInit,
    Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
    where TProjection : IUniquelyIdentifiable
```

This allows list selectors to benefit from the same null filtering logic.

## Query Execution

Events are queried from Cosmos DB using LINQ:

```csharp
var eventsQuery = eventStore
    .GetItemLinqQueryable<Event>()
    .Where(e => foreignIds.Contains(e.aggregateRootId));

if (pointInTime.HasValue)
{
    eventsQuery = eventsQuery.Where(e => e.timestamp <= pointInTime.Value);
}

var events = await queryExecutor.ReadAllAsync(eventsQuery.OrderBy(e => e.timestamp));
```

Events are then grouped by `aggregateRootId` using `ToLookup()`.

## Result Structure

The result maps each projection to its related events:

```csharp
var result = (
    from p in projectionsToInit
    from f in foreignIdSelectors
    let foreignId = f(p)
    where foreignId.HasValue
    let eventList = events[foreignId!.Value].ToList()
    where eventList.Any()
    select new ExternalDataEvent(p.id, eventList)
).ToList();
```

Note: The `aggregateRootId` in `ExternalDataEvent` is the **projection's ID**, not the foreign aggregate's ID. The `events` list contains events from the foreign aggregate(s).

## Usage Examples

### Basic Usage

```csharp
var externalEvents = await ExternalDataEvent.GetEventsAsync(
    eventStoreContainer,
    projectionsToInit,
    CosmosQueryExecutor.Default,
    pointInTime: null,
    p => p.CustomerId);  // Selector returning Guid?
```

### With List Selectors

```csharp
var externalEvents = await ExternalDataEvent.GetEventsAsync(
    eventStoreContainer,
    projectionsToInit,
    CosmosQueryExecutor.Default,
    pointInTime: null,
    p => p.TagIds);  // Selector returning List<Guid?>
```

### From External Service

```csharp
var externalEvents = await ExternalDataEvent.GetEventsAsync(
    httpClient,
    "https://customer-service/api/events",
    projectionsToInit,
    pointInTime: null,
    p => p.CustomerId);
```

### Multiple Services

```csharp
var externalEvents = await ExternalDataEvent.GetMultiServiceEventsAsync(
    httpClient,
    projectionsToInit,
    pointInTime: null,
    new EventRequester<OrderProjection>("https://customers/events", p => p.CustomerId),
    new EventRequester<OrderProjection>("https://products/events", p => p.ProductId));
```

## Testing

Use `InMemoryQueryExecutor` for unit testing:

```csharp
var events = await ExternalDataEvent.GetEventsAsync(
    mockContainer.Object,
    projections,
    InMemoryQueryExecutor.Default,
    pointInTime: null,
    p => p.ForeignId);
```

## Related Classes

- [`ExternalDataEventFactory<P>`](ExternalDataEventFactory.spec.md) - Fluent builder that uses this class
- [`EventRequester<P>`](EventRequester.spec.md) - Configuration for multi-service requests
- [`Event`](../src/Event/Event.cs) - The individual event records

## Version History

- **4.1.0** - Initial release with Cosmos and HTTP support
- **4.0.0** - Added `IQueryExecutor` parameter for testability
