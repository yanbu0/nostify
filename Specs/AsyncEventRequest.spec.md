# AsyncEventRequest Specification

## Overview

`AsyncEventRequest` is a POCO (Plain Old C# Object) that represents a request for events from an external service via Kafka. When a projection needs events from another microservice, an `AsyncEventRequest` is serialized and produced to the target service's `{ServiceName}_EventRequest` Kafka topic.

## Class Definition

```csharp
public class AsyncEventRequest
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `topic` | `string` | The Kafka topic to publish the request to and listen for the response on. Format: `{ServiceName}_EventRequest` |
| `subtopic` | `string` | Reserved for future use. Subtopic for more granular filtering. |
| `aggregateRootIds` | `List<Guid>` | The aggregate root IDs to request events for. Defaults to empty list. |
| `pointInTime` | `DateTime?` | Optional point in time to query events up to. If null, queries all events. |
| `correlationId` | `string` | Unique correlation ID for matching responses to this request. Generated as a new Guid string per batch. |

## Serialization

Serialized with `Newtonsoft.Json` for Kafka transport. All properties use camelCase (default C# naming convention).

## Usage

```csharp
var request = new AsyncEventRequest
{
    topic = "InventoryService_EventRequest",
    subtopic = "",
    aggregateRootIds = new List<Guid> { productId1, productId2 },
    pointInTime = DateTime.UtcNow.AddHours(-1),
    correlationId = Guid.NewGuid().ToString()
};
```

## Key Relationships

- Produced by [`ExternalDataEventFactory<P>.GetAsyncEventsAsync`](ExternalDataEventFactory.spec.md) when async requestors are configured
- Consumed by the template-generated `AsyncEventRequestHandler` in the target microservice
- Paired with [`AsyncEventRequestResponse`](AsyncEventRequestResponse.spec.md) for the response

## Version History

- **4.5.0** - Initial release
