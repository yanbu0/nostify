# AsyncEventRequestResponse Specification

## Overview

`AsyncEventRequestResponse` is a POCO that represents a response to an `AsyncEventRequest` sent via Kafka. Responses may arrive in multiple chunks — the consumer accumulates events until the `complete` flag is true.

## Class Definition

```csharp
public class AsyncEventRequestResponse
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `topic` | `string` | The Kafka topic this response was published to |
| `subtopic` | `string` | Reserved for future use. Subtopic for more granular filtering. |
| `complete` | `bool` | Whether this is the final chunk of the response. When true, the consumer stops waiting. |
| `events` | `List<Event>` | The events in this chunk of the response. Defaults to empty list. |
| `correlationId` | `string` | The correlation ID matching the original `AsyncEventRequest.correlationId` |

## Static Methods

### ChunkEvents

```csharp
public static List<AsyncEventRequestResponse> ChunkEvents(
    List<Event> events,
    int maxBytes,
    string topic,
    string subtopic,
    string correlationId)
```

Splits a list of events into chunks that fit within the specified maximum byte size when serialized.

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `events` | `List<Event>` | The full list of events to chunk. May be null or empty. |
| `maxBytes` | `int` | Maximum serialized size per chunk in bytes (default ~900KB) |
| `topic` | `string` | The topic for each response chunk |
| `subtopic` | `string` | The subtopic for each response chunk |
| `correlationId` | `string` | The correlation ID for each response chunk |

**Returns:** `List<AsyncEventRequestResponse>` — List of response chunks. Always produces at least one response with `complete = true`, even if events is null/empty.

**Behavior:**
- Each chunk becomes an `AsyncEventRequestResponse` with `complete = false`, except the last chunk which has `complete = true`
- If `events` is null or empty, returns a single response with `complete = true` and an empty events list
- Events that individually exceed `maxBytes` are still included (one per chunk)

## Chunking Algorithm

```
for each event:
    serialize event to JSON, measure byte size
    if adding this event would exceed maxBytes AND current chunk is not empty:
        finalize current chunk (complete = false)
        start new chunk
    add event to current chunk
finalize last chunk (complete = true)
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `AsyncEventRequestMaxMessageBytes` | `900000` | Maximum serialized size per response chunk in bytes. Used by both the template handler and the consumer to configure chunking. |

## Key Relationships

- Produced by the `AsyncEventRequestHandler` template in the responding service
- Consumed by [`ExternalDataEventFactory<P>.GetAsyncEventsAsync`](ExternalDataEventFactory.spec.md)
- Paired with [`AsyncEventRequest`](AsyncEventRequest.spec.md)

## Version History

- **4.5.0** - Initial release
