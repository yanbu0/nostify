# Event Class Specification

## Overview

`Event` is the core immutable data structure representing state changes in the event store. Events capture what happened in the system as a series of commands with payloads.

## Class Definition

```csharp
public class Event : IEvent
```

## Constructors

### Full Constructor

```csharp
public Event(
    NostifyCommand command,
    Guid aggregateRootId,
    object payload,
    Guid createdBy,
    int version = 1
)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `command` | `NostifyCommand` | Required | The command that triggered this event |
| `aggregateRootId` | `Guid` | Required | ID of the aggregate this event affects |
| `payload` | `object` | Required | Data containing properties to update |
| `createdBy` | `Guid` | Required | User who triggered the event |
| `version` | `int` | `1` | Event version for compatibility |

### Auto-Extract ID Constructor

```csharp
public Event(
    NostifyCommand command,
    object payload,
    Guid createdBy,
    int version = 1
)
```

Automatically extracts `aggregateRootId` from the payload's `id` property.

### String IDs Constructor

```csharp
public Event(
    NostifyCommand command,
    string aggregateRootId,
    object payload,
    string createdBy,
    int version = 1
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
| `id` | `string` | Unique event identifier (auto-generated GUID string) |
| `timestamp` | `DateTime` | When the event occurred (UTC) |
| `partitionKey` | `string` | Partition key for routing (aggregate type name) |
| `createdBy` | `Guid` | User who triggered the event |
| `command` | `NostifyCommand` | The command being performed |
| `aggregateRootId` | `Guid` | ID of the aggregate this event applies to |
| `payload` | `object` | Data containing properties to update |
| `version` | `int` | Version for compatibility/migration |

## Methods

### HasProperty

```csharp
public bool HasProperty(string propertyName)
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
public bool ValidatePayload<A>() where A : IAggregate
```

Validates that payload properties match the aggregate type.

**Returns:** `bool` - True if all payload properties are valid

### ApplyTo

```csharp
public void ApplyTo<T>(T target) where T : IApplyable
```

Applies the event's payload to a target object.

| Parameter | Type | Description |
|-----------|------|-------------|
| `target` | `T` | Object to apply payload properties to |

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
if (@event.HasProperty("Status"))
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
        "name": "CreateOrder",
        "aggregateType": "Order",
        "isExternalDataEvent": false
    },
    "payload": {
        "customerId": "customer-guid",
        "total": 99.99,
        "status": "Pending"
    },
    "version": 1
}
```

## Validation

Event validation ensures:

1. **Non-null command** - Events must have a command
2. **Valid aggregate ID** - Must be a valid GUID
3. **Payload validation** - Properties must match aggregate type (when validated)

```csharp
// Validate payload against aggregate
if (!@event.ValidatePayload<Order>())
{
    throw new NostifyValidationException("Invalid payload properties");
}
```

## Kafka Integration

Events are published to Kafka topics:

- **Topic Name**: `command.name` (e.g., "CreateOrder")
- **Message Key**: `aggregateRootId.ToString()`
- **Message Value**: JSON serialized event

## Best Practices

1. **Immutable Payloads** - Use anonymous objects or records
2. **Descriptive Commands** - Use clear command names
3. **Include Context** - Always set `createdBy`
4. **Version Events** - Use `version` for schema evolution
5. **Validate Early** - Validate payloads before publishing

## Related Types

- [IEvent](IEvent.spec.md) - Event interface
- [NostifyCommand](NostifyCommand.spec.md) - Command class
- [IApplyable](IApplyable.spec.md) - Event application interface
- [NostifyKafkaTriggerEvent](NostifyKafkaTriggerEvent.spec.md) - Kafka trigger wrapper
