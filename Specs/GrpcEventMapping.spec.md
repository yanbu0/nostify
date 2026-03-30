# GrpcEventMapping Specification

## Overview

`GrpcEventMapping` is a static helper class that provides bidirectional mapping between nostify `Event` objects and gRPC protobuf `EventMessage` messages. It handles the conversion of all event properties including GUIDs (as strings), timestamps (as `google.protobuf.Timestamp`), commands, and payloads (serialized as JSON strings).

## Class Definition

```csharp
public static class GrpcEventMapping
```

## Methods

### MapFromProto (Single)

```csharp
public static Event MapFromProto(EventMessage msg)
```

Converts a gRPC `EventMessage` to a nostify `Event`.

**Field Mapping:**

| Proto Field | Event Field | Conversion |
|-------------|-------------|------------|
| `Id` | `id` | `Guid.Parse()` |
| `AggregateRootId` | `aggregateRootId` | `Guid.Parse()` |
| `Timestamp` | `timestamp` | `.ToDateTime()` |
| `PartitionKey` | `partitionKey` | `Guid.Parse()` (empty → `Guid.Empty`) |
| `UserId` | `userId` | `Guid.Parse()` (empty → `Guid.Empty`) |
| `SchemaVersion` | `schemaVersion` | Direct int |
| `Command` | `command` | `NostifyCommand(name, isNew, allowNullPayload)` |
| `PayloadJson` | `payload` | `JsonConvert.DeserializeObject<object>()` |

**Throws:** `ArgumentNullException` if `msg` is null.

### MapToProto (Single)

```csharp
public static EventMessage MapToProto(Event evt)
```

Converts a nostify `Event` to a gRPC `EventMessage`.

**Throws:** `ArgumentNullException` if `evt` is null.

### MapFromProto (Batch)

```csharp
public static List<Event> MapFromProto(IEnumerable<EventMessage> messages)
```

Converts a list of protobuf messages to events. Returns empty list for null input.

### MapToProto (Batch)

```csharp
public static List<EventMessage> MapToProto(IEnumerable<Event> events)
```

Converts a list of events to protobuf messages. Returns empty list for null input.

## Null/Empty Handling

| Scenario | Behavior |
|----------|----------|
| Null `EventMessage` | Throws `ArgumentNullException` |
| Null `Event` | Throws `ArgumentNullException` |
| Empty `PayloadJson` | Sets `payload` to `null` |
| Null `payload` | Sets `PayloadJson` to `""` |
| Null `command` | Creates `CommandMessage { Name = "Unknown" }` |
| Null `Command` | Creates `NostifyCommand("Unknown", false, false)` |
| Empty `PartitionKey`/`UserId` | Maps to `Guid.Empty` |
| Null list input | Returns empty list |

## Round-Trip Fidelity

All core fields survive a `MapToProto` → `MapFromProto` round-trip:
- `id`, `aggregateRootId`, `timestamp`, `partitionKey`, `userId`
- `command.name`, `command.isNew`, `command.allowNullPayload`
- `payload` (via JSON serialization/deserialization)

## Usage Examples

### Convert Event to Proto

```csharp
var eventMessage = GrpcEventMapping.MapToProto(myEvent);
```

### Convert Proto to Event

```csharp
var evt = GrpcEventMapping.MapFromProto(eventMessage);
```

### Batch Conversion (used in server handler)

```csharp
var response = new EventResponseMessage();
response.Events.AddRange(GrpcEventMapping.MapToProto(allEvents));
```

## Key Relationships

- [`Event`](Event.spec.md) - The domain event class being mapped
- [`DefaultEventRequestHandlers`](DefaultEventRequestHandlers.spec.md) - Uses `MapToProto` in `HandleGrpcEventRequestAsync`
- [`ExternalDataEvent`](ExternalDataEvent.spec.md) - Uses `MapFromProto` in `GetEventsViaGrpcAsync`
- [`GrpcEventRequester`](GrpcEventRequester.spec.md) - Requestor class that triggers gRPC event fetching

## File Location

`src/Projection/GrpcEventMapping.cs`

## Version History

- **4.5.0** - Initial release (gRPC transport support)
