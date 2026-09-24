# Event Class Specification

## Overview

`Event` is the core immutable data structure representing state changes in the event store. Events now carry a typed `eventType`, keep the older `command` property as an obsolete compatibility alias, and expose `schemaVersion` as the envelope/schema version used for migration-aware serialization.

## Class Definition

```csharp
public class Event : IEvent
```

## Constructors

### Full Constructor

```csharp
public Event(
    EventType eventType,
    Guid aggregateRootId,
    object payload,
    Guid userId = default,
    Guid partitionKey = default
)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `eventType` | `EventType` | Required | The typed event metadata to persist |
| `aggregateRootId` | `Guid` | Required | ID of the aggregate this event affects |
| `payload` | `object` | Required | Data containing properties to update |
| `userId` | `Guid` | `default` | User who triggered the event |
| `partitionKey` | `Guid` | `default` | Partition key for the aggregate stream |

### Auto-Extract ID Constructor

```csharp
public Event(
    EventType eventType,
    object payload,
    Guid userId = default,
    Guid partitionKey = default
)
```

Automatically extracts `aggregateRootId` from the payload's `id` property.

### String IDs Constructor

```csharp
public Event(
    EventType eventType,
    string aggregateRootId,
    object payload,
    string userId,
    string partitionKey
)
```

Parses string IDs to Guids for convenience.

### Parameterless Constructor

```csharp
public Event()
```

For JSON deserialization from Cosmos DB.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `id` | `Guid` | Unique event identifier (auto-generated GUID) |
| `timestamp` | `DateTime` | When the event occurred (UTC) |
| `partitionKey` | `Guid` | Partition key for routing |
| `userId` | `Guid` | User who triggered the event |
| `eventType` | `EventType` | The typed event metadata being performed |
| `command` | `NostifyCommand` | Obsolete compatibility alias for `eventType`; typed events expose a cached metadata shim |
| `aggregateRootId` | `Guid` | ID of the aggregate this event applies to |
| `payload` | `object` | Data containing properties to update |
| `schemaVersion` | `int` | Envelope/schema version for compatibility and migration |

`schemaVersion` is not a public constructor parameter. New events infer it from the assigned metadata (`2` for modern non-legacy typed event types, `1` for legacy command-backed metadata), while deserialized documents preserve any explicitly stored value.

## Methods

### PayloadHasProperty

```csharp
public bool PayloadHasProperty(string propertyName)
```

Checks if the payload contains a specific property.

| Parameter | Type | Description |
|-----------|------|-------------|
| `propertyName` | `string` | Name of the property to check |

**Returns:** `bool` - True if property exists in payload

### GetPayload

```csharp
public T GetPayload<T>()
```

Returns the payload as a typed object.

**Returns:** `T` - Payload deserialized to type T

### ValidatePayload

```csharp
public IEvent ValidatePayload<T>(bool throwErrorIfExtraProps = true) where T : class
```

Validates that payload properties match the aggregate type.

**Returns:** `IEvent` - The current event for chaining; throws if validation fails

### ApplyTo

```csharp
public void ApplyTo<T>(T target) where T : IApplyable
```

Applies the event's payload to a target object.

| Parameter | Type | Description |
|-----------|------|-------------|
| `target` | `T` | Object to apply payload properties to |

## Backward Compatibility

The obsolete `command` property remains available for legacy callers. If the event was created with a typed `EventType`, `command` returns a cached compatibility `NostifyCommand` containing the same `name`, `isNew`, and `allowNullPayload` values. This preserves older metadata-based code paths without changing the underlying typed dispatch model.

The obsolete `Event(NostifyCommand, ...)` constructors remain available for legacy callers and throw `ArgumentNullException` when passed a null command.

`schemaVersion` now distinguishes modern typed envelopes from legacy command-only documents:

- New events with a concrete non-legacy `eventType` default to `schemaVersion = 2`
- Legacy command-only events remain `schemaVersion = 1`
- Incoming documents with an explicit `schemaVersion` keep that persisted value
- Incoming documents missing version metadata infer the version from the hydrated document shape

The logical `eventType.name` is the sole persisted event-type identity. JSON writes `name`, `isNew`, and `allowNullPayload` as event-type metadata.

Deserialization resolves names with ordinal, case-sensitive comparison. A unique loaded concrete `EventType` definition supplies canonical metadata and preserves typed dynamic dispatch. Unknown names hydrate as `LegacyNostifyCommandEventType` and retain serialized metadata. Multiple loaded concrete definitions with the same exact name are invalid configuration and produce a deterministic exception listing every conflicting CLR type.

When both `eventType` and legacy `command` are present in incoming JSON, `command` no longer overwrites an already resolved concrete `eventType`; it only hydrates `eventType` when no concrete value exists yet (or when the current value is still the internal legacy adapter).

## Usage Examples

### Creating Events

```csharp
// Create event with explicit ID
var @event = new Event(
    NostifyCommand.Create("Order"),
    orderId,
    new { CustomerId = customerId, Total = 99.99m, Status = "Pending" },
    userId
);

// Create event with ID extracted from payload
var @event = new Event(
    NostifyCommand.Create("Order"),
    new { id = orderId, CustomerId = customerId, Total = 99.99m },
    userId
);

// Create update event
var updateEvent = new Event(
    NostifyCommand.Update("Order"),
    orderId,
    new { Status = "Shipped", ShippedDate = DateTime.UtcNow },
    userId
);
```

### Publishing Events

```csharp
public async Task CreateOrder(CreateOrderCommand cmd)
{
    var @event = new Event(
        NostifyCommand.Create("Order"),
        cmd.OrderId,
        new { 
            CustomerId = cmd.CustomerId, 
            Total = cmd.Total,
            Status = OrderStatus.Pending 
        },
        cmd.UserId
    );
    
    await nostify.PublishEventAsync(@event);
}
```

### Applying Events

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    
    public void Apply(Event @event)
    {
        // ApplyTo uses reflection to set matching properties
        @event.ApplyTo(this);
    }
}

// Rehydration
var order = new Order(orderId);
var events = await nostify.GetAllEventsAsync<Order>(orderId);
foreach (var @event in events.OrderBy(e => e.timestamp))
{
    order.Apply(@event);
}
```

### Checking Payload Contents

```csharp
if (@event.PayloadHasProperty("Status"))
{
    var payload = @event.GetPayload<dynamic>();
    Console.WriteLine($"Status changed to: {payload.Status}");
}
```

## Event Store Structure

Events are stored in Cosmos DB with this structure:

```json
{
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "timestamp": "2024-01-15T10:30:00Z",
    "partitionKey": "Order",
    "createdBy": "user-guid-here",
    "aggregateRootId": "order-guid-here",
    "command": {
        "name": "Create_Order",
        "isNew": true,
        "allowNullPayload": false
    },
    "payload": {
        "customerId": "customer-guid",
        "total": 99.99,
        "status": "Pending"
    },
    "schemaVersion": 1
}
```

## Validation

Event validation ensures:

1. **Non-null event type** - Events must have event metadata
2. **Valid aggregate ID** - Must be a valid GUID
3. **Payload validation** - Properties must match aggregate type (when validated)

```csharp
// Validate payload against aggregate (throws on failure)
@event.ValidatePayload<Order>();
```

## Kafka Integration

Events are published to Kafka topics:

- **Topic Name**: `eventType.name` (e.g., "Create_Order")
- **Message Key**: `aggregateRootId.ToString()`
- **Message Value**: JSON serialized event

## Best Practices

1. **Immutable Payloads** - Use anonymous objects or records
2. **Descriptive Event Types** - Use clear logical event names
3. **Include Context** - Always set `userId` when available
4. **Version Awareness** - Let `schemaVersion` reflect typed-vs-legacy compatibility
5. **Validate Early** - Validate payloads before publishing

## Related Types

- [IEvent](IEvent.spec.md) - Event interface
- [EventType](EventType.spec.md) - Typed event metadata
- [NostifyCommand](NostifyCommand.spec.md) - Obsolete compatibility command class
- [IApplyable](IApplyable.spec.md) - Event application interface
- [NostifyKafkaTriggerEvent](NostifyKafkaTriggerEvent.spec.md) - Kafka trigger wrapper
