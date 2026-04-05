# GrpcEventRequester Specification

## Overview

`GrpcEventRequester<TProjection>` represents a request configuration for fetching events from an external service via gRPC. It mirrors `EventRequester<TProjection>` (HTTP) and `AsyncEventRequester<TProjection>` (Kafka) but uses a gRPC endpoint address instead of a URL or Kafka service name.

## Type Parameters

- `TProjection` - The type of projection that will be initialized. Must implement `IUniquelyIdentifiable`.

## Class Definition

```csharp
public class GrpcEventRequester<TProjection> where TProjection : IUniquelyIdentifiable
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Address` | `string` | The gRPC endpoint address (e.g. `"https://localhost:5001"`) |
| `ForeignIdSelectors` | `Func<TProjection, Guid?>[]` | Functions to get foreign IDs for aggregates |
| `SingleSelectors` | `Func<TProjection, Guid?>[]` | Single ID selectors |
| `ListSelectors` | `Func<TProjection, List<Guid?>>[]` | List ID selectors for expanding lists |

## Constructors

### Nullable Guid Selectors

```csharp
public GrpcEventRequester(string address, params Func<TProjection, Guid?>[] foreignIdSelectors)
```

Creates a GrpcEventRequester with nullable Guid selectors.

**Throws:** `NostifyException` if `address` is null or empty.

### Non-Nullable Guid Selectors

```csharp
public GrpcEventRequester(string address, params Func<TProjection, Guid>[] singleIdSelectors)
```

Creates a GrpcEventRequester with non-nullable Guid selectors. Automatically wraps each selector to return `Guid?`.

### Nullable Guid List Selectors

```csharp
public GrpcEventRequester(string address, params Func<TProjection, List<Guid?>>[] listIdSelectors)
```

### Non-Nullable Guid List Selectors

```csharp
public GrpcEventRequester(string address, params Func<TProjection, List<Guid>>[] listIdSelectors)
```

### Mixed Nullable Selectors

```csharp
public GrpcEventRequester(string address, Func<TProjection, Guid?>[] singleIdSelectors, Func<TProjection, List<Guid?>>[] listIdSelectors)
```

### Mixed Non-Nullable Selectors

```csharp
public GrpcEventRequester(string address, Func<TProjection, Guid>[] singleIdSelectors, Func<TProjection, List<Guid>>[] listIdSelectors)
```

## Methods

### GetAllForeignIdSelectors

```csharp
public Func<TProjection, Guid?>[] GetAllForeignIdSelectors(List<TProjection> projections)
```

Gets all foreign ID selectors by combining single selectors with expanded list selectors. List selectors are expanded per-projection into individual single-ID selectors.

## Usage Examples

### Basic Single ID Selector

```csharp
var requester = new GrpcEventRequester<OrderProjection>(
    "https://customer-service:5001",
    p => p.CustomerId);
```

### Multiple Selectors

```csharp
var requester = new GrpcEventRequester<OrderProjection>(
    "https://product-service:5001",
    p => p.PrimaryProductId,
    p => p.SecondaryProductId);
```

### With ExternalDataEventFactory

```csharp
var events = await new ExternalDataEventFactory<OrderProjection>(nostify, projections)
    .WithGrpcEventRequestor("https://customers:5001", p => p.CustomerId)
    .WithGrpcEventRequestor("https://products:5001", p => p.ProductId)
    .GetEventsAsync();
```

### Dependant gRPC Requestor

```csharp
var events = await new ExternalDataEventFactory<OrderProjection>(nostify, projections)
    .WithSameServiceIdSelectors(p => p.OrderId)
    .WithDependantGrpcEventRequestor("https://fulfillment:5001", p => p.FulfillmentId)
    .GetEventsAsync();
```

## gRPC Transport Details

When events are requested, the `GrpcEventRequester` is used by `ExternalDataEvent.GetMultiServiceEventsViaGrpcAsync` to:

1. Extract all foreign IDs from projections using the selectors
2. Create a `GrpcChannel` for each Address
3. Send a unary `RequestEvents` RPC with the list of aggregate root IDs
4. Map the protobuf `EventResponseMessage` back to nostify `Event` objects

## Protobuf Contract

Uses the `EventRequestService.RequestEvents` RPC defined in `event_request.proto`:

```protobuf
service EventRequestService {
  rpc RequestEvents (EventRequestMessage) returns (EventResponseMessage);
}
```

## Null Handling

- Selectors returning `null` (`Guid?`) are filtered out before the gRPC request
- Empty arrays for selectors are handled gracefully
- Address is validated in the constructor

## Validation

- Address is validated in every constructor
- Throws `NostifyException` if address is null or empty

## Key Relationships

- [`EventRequester`](EventRequester.spec.md) - HTTP equivalent
- [`AsyncEventRequester`](AsyncEventRequester.spec.md) - Kafka equivalent
- [`ExternalDataEvent`](ExternalDataEvent.spec.md) - Uses this class via `GetMultiServiceEventsViaGrpcAsync`
- [`ExternalDataEventFactory<P>`](ExternalDataEventFactory.spec.md) - Fluent API integration via `WithGrpcEventRequestor`
- [`GrpcEventMapping`](GrpcEventMapping.spec.md) - Proto ↔ Event conversion used during transport

## Version History

- **4.5.0** - Initial release (gRPC transport support)
