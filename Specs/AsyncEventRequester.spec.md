# AsyncEventRequester Specification

## Overview

`AsyncEventRequester<TProjection>` represents a request configuration for fetching events from an external service via Kafka async messaging. It mirrors `EventRequester<TProjection>` but uses a service name (for Kafka topic derivation) instead of a URL (for HTTP calls).

## Type Parameters

- `TProjection` - The type of projection that will be initialized. Must implement `IUniquelyIdentifiable`.

## Class Definition

```csharp
public class AsyncEventRequester<TProjection> where TProjection : IUniquelyIdentifiable
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `ServiceName` | `string` | The name of the external service. Used to derive the Kafka topic. |
| `TopicName` | `string` | Computed: `{ServiceName}_EventRequest`. The Kafka topic for request/response. |
| `ForeignIdSelectors` | `Func<TProjection, Guid?>[]` | Functions to get foreign IDs for aggregates |
| `SingleSelectors` | `Func<TProjection, Guid?>[]` | Single ID selectors |
| `ListSelectors` | `Func<TProjection, List<Guid?>>[]` | List ID selectors for expanding lists |

## Constructors

All constructors throw `NostifyException` if `serviceName` is null or empty.

### 1. Nullable Guid selectors

```csharp
public AsyncEventRequester(string serviceName, params Func<TProjection, Guid?>[] foreignIdSelectors)
```

### 2. Non-nullable Guid selectors

```csharp
public AsyncEventRequester(string serviceName, params Func<TProjection, Guid>[] singleIdSelectors)
```

Wraps each `Func<TProjection, Guid>` as `Func<TProjection, Guid?>`.

### 3. Nullable Guid list selectors

```csharp
public AsyncEventRequester(string serviceName, params Func<TProjection, List<Guid?>>[] listIdSelectors)
```

### 4. Non-nullable Guid list selectors

```csharp
public AsyncEventRequester(string serviceName, params Func<TProjection, List<Guid>>[] listIdSelectors)
```

Wraps each `List<Guid>` as `List<Guid?>` via `.Cast<Guid?>().ToList()`.

### 5. Mixed nullable selectors

```csharp
public AsyncEventRequester(string serviceName, Func<TProjection, Guid?>[] singleIdSelectors, Func<TProjection, List<Guid?>>[] listIdSelectors)
```

### 6. Mixed non-nullable selectors

```csharp
public AsyncEventRequester(string serviceName, Func<TProjection, Guid>[] singleIdSelectors, Func<TProjection, List<Guid>>[] listIdSelectors)
```

## Methods

### GetAllForeignIdSelectors

```csharp
public Func<TProjection, Guid?>[] GetAllForeignIdSelectors(List<TProjection> projectionsToInit)
```

Expands list selectors across all projections and combines with single selectors.

**Returns:** Combined array of all foreign ID selectors (single + expanded list).

## Usage

```csharp
// Simple single-ID selector
var requester = new AsyncEventRequester<OrderProjection>(
    "InventoryService",
    p => p.ProductId);

// Multiple selectors
var requester = new AsyncEventRequester<OrderProjection>(
    "UserService",
    p => p.CustomerId,
    p => p.AgentId);

// Mixed single and list selectors
var requester = new AsyncEventRequester<OrderProjection>(
    "TagService",
    new Func<OrderProjection, Guid?>[] { p => p.CategoryId },
    new Func<OrderProjection, List<Guid?>>[] { p => p.TagIds.Cast<Guid?>().ToList() });
```

## Key Relationships

- Used by [`ExternalDataEventFactory<P>.WithAsyncEventRequestor`](ExternalDataEventFactory.spec.md)
- Mirrors [`EventRequester<P>`](EventRequester.spec.md) for Kafka instead of HTTP
- Produces [`AsyncEventRequest`](AsyncEventRequest.spec.md) messages

## Comparison with EventRequester

| Aspect | `EventRequester` | `AsyncEventRequester` |
|--------|------------------|-----------------------|
| Transport | HTTP (synchronous) | Kafka (asynchronous) |
| Key Property | `Url` | `ServiceName` |
| Requires | `HttpClient` | Kafka consumer/producer |
| Topic Derivation | N/A | `{ServiceName}_EventRequest` |

## Version History

- **4.5.0** - Initial release
