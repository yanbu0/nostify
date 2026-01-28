# EventRequester Specification

## Overview

`EventRequester<TProjection>` represents a request configuration for fetching events from an external service to initialize projections. It encapsulates the service URL and foreign ID selectors needed to request events from another microservice.

## Type Parameters

- `TProjection` - The type of projection that will be initialized. Must implement `IUniquelyIdentifiable`.

## Class Definition

```csharp
public class EventRequester<TProjection> where TProjection : IUniquelyIdentifiable
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Url` | `string` | The URL of the service EventRequest endpoint |
| `ForeignIdSelectors` | `Func<TProjection, Guid?>[]` | Functions to get foreign IDs for aggregates |
| `SingleSelectors` | `Func<TProjection, Guid?>[]` | Single ID selectors (internal) |
| `ListSelectors` | `Func<TProjection, List<Guid?>>[]` | List ID selectors (internal) |

## Constructors

### Basic Constructor

```csharp
public EventRequester(string url, params Func<TProjection, Guid?>[] foreignIdSelectors)
```

Creates an EventRequester with single ID selectors.

**Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `url` | `string` | Yes | The URL of the service EventRequest endpoint |
| `foreignIdSelectors` | `Func<TProjection, Guid?>[]` | No | Functions to extract foreign IDs from projections |

**Throws:** `NostifyException` if `url` is null or empty.

### Mixed Selectors Constructor

```csharp
public EventRequester(
    string url,
    Func<TProjection, Guid?>[] singleIdSelectors,
    Func<TProjection, List<Guid?>>[] listIdSelectors)
```

Creates an EventRequester with both single and list ID selectors.

**Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `url` | `string` | Yes | The URL of the service EventRequest endpoint |
| `singleIdSelectors` | `Func<TProjection, Guid?>[]` | No | Functions returning single foreign IDs |
| `listIdSelectors` | `Func<TProjection, List<Guid?>>[]` | No | Functions returning lists of foreign IDs |

## Usage Examples

### Basic Single ID Selector

```csharp
var requester = new EventRequester<OrderProjection>(
    "https://customer-service/api/events",
    p => p.CustomerId);
```

### Multiple Single ID Selectors

```csharp
var requester = new EventRequester<OrderProjection>(
    "https://product-service/api/events",
    p => p.PrimaryProductId,
    p => p.SecondaryProductId);
```

### Mixed Selectors

```csharp
var requester = new EventRequester<OrderProjection>(
    "https://inventory-service/api/events",
    singleIdSelectors: new Func<OrderProjection, Guid?>[] { p => p.WarehouseId },
    listIdSelectors: new Func<OrderProjection, List<Guid?>>[] { p => p.ItemIds });
```

### With ExternalDataEventFactory

```csharp
var events = await new ExternalDataEventFactory<OrderProjection>(nostify, projections, httpClient)
    .AddEventRequestors(
        new EventRequester<OrderProjection>("https://customers/events", p => p.CustomerId),
        new EventRequester<OrderProjection>("https://products/events", p => p.ProductId))
    .GetEventsAsync();
```

### Using WithEventRequestor (Convenience Method)

```csharp
var events = await new ExternalDataEventFactory<OrderProjection>(nostify, projections, httpClient)
    .WithEventRequestor("https://customers/events", p => p.CustomerId)
    .WithEventRequestor("https://products/events", p => p.ProductId)
    .GetEventsAsync();
```

## HTTP Request Format

When events are requested, the `EventRequester` is used by `ExternalDataEvent.GetMultiServiceEventsAsync` to:

1. Extract all foreign IDs from projections using the selectors
2. Filter out null values
3. POST a JSON request to the URL with the list of IDs
4. Parse the response as a list of `Event` objects

## Null Handling

- Selectors returning `null` (`Guid?`) are filtered out before the HTTP request
- If all selectors return null for a projection, no request is made for that projection
- Empty arrays for selectors are handled gracefully

## Validation

- URL is validated in the constructor
- Throws `NostifyException` if URL is null or empty
- Selectors arrays default to empty arrays if null

## Related Classes

- [`ExternalDataEvent`](ExternalDataEvent.spec.md) - Uses this class for multi-service requests
- [`ExternalDataEventFactory<P>`](ExternalDataEventFactory.spec.md) - Uses this class via `AddEventRequestors`

## Version History

- **4.1.0** - Added mixed selectors constructor
- **4.0.0** - Initial release
