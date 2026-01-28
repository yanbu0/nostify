# IEvent Interface Specification

## Overview

`IEvent` defines the contract for events in the nostify event-sourcing framework. Events are immutable records of state changes that occur in the system.

## Interface Definition

```csharp
public interface IEvent
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `id` | `string` | Unique event identifier |
| `timestamp` | `DateTime` | When the event occurred (UTC) |
| `partitionKey` | `string` | Partition key for event routing |
| `createdBy` | `Guid` | ID of user who triggered the event |
| `command` | `NostifyCommand` | The command being performed |
| `aggregateRootId` | `Guid` | ID of the aggregate this event applies to |
| `payload` | `object` | Data containing properties to update |
| `version` | `int` | Version for compatibility/migration |

## Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `HasProperty` | `bool HasProperty(string propertyName)` | Checks if payload contains a property |
| `GetPayload<T>` | `T GetPayload<T>()` | Returns typed payload |
| `ValidatePayload<A>` | `bool ValidatePayload<A>() where A : IAggregate` | Validates payload against aggregate type |

## Property Details

### id

- **Type**: `string`
- **Description**: Unique identifier for the event, typically a GUID string
- **Usage**: Document ID in Cosmos DB, message deduplication

### timestamp

- **Type**: `DateTime`
- **Description**: UTC timestamp when the event was created
- **Usage**: Event ordering, temporal queries, point-in-time reconstruction

### partitionKey

- **Type**: `string`
- **Description**: Partition key for Cosmos DB and event routing
- **Convention**: Usually the aggregate type name (e.g., "Order")

### createdBy

- **Type**: `Guid`
- **Description**: The user ID who initiated the event
- **Usage**: Audit trails, access control, analytics

### command

- **Type**: `NostifyCommand`
- **Description**: The command that caused this event
- **Usage**: Event categorization, Kafka topic routing

### aggregateRootId

- **Type**: `Guid`
- **Description**: The ID of the aggregate this event affects
- **Usage**: Event stream grouping, rehydration queries

### payload

- **Type**: `object`
- **Description**: The data representing the state change
- **Usage**: Contains properties to apply to aggregates

### version

- **Type**: `int`
- **Description**: Schema version for event evolution
- **Usage**: Migration, backward compatibility

## Method Details

### HasProperty

```csharp
bool HasProperty(string propertyName)
```

Checks if the payload contains a specific property. Useful for conditional event handling.

**Example:**
```csharp
if (@event.HasProperty("Status"))
{
    // Handle status change
}
```

### GetPayload<T>

```csharp
T GetPayload<T>()
```

Deserializes the payload to a strongly-typed object.

**Example:**
```csharp
var orderData = @event.GetPayload<OrderPayload>();
Console.WriteLine($"Total: {orderData.Total}");
```

### ValidatePayload<A>

```csharp
bool ValidatePayload<A>() where A : IAggregate
```

Validates that all payload properties exist on the target aggregate type.

**Example:**
```csharp
if (!@event.ValidatePayload<Order>())
{
    throw new NostifyValidationException("Invalid payload");
}
```

## Implementation

The primary implementation is the [Event](Event.spec.md) class.

## Usage Examples

### Event Handler

```csharp
public async Task HandleEvent(IEvent @event)
{
    switch (@event.command.name)
    {
        case "CreateOrder":
            await HandleCreateOrder(@event);
            break;
        case "UpdateOrder":
            await HandleUpdateOrder(@event);
            break;
        case "DeleteOrder":
            await HandleDeleteOrder(@event);
            break;
    }
}
```

### Event Filtering

```csharp
public async Task<List<IEvent>> GetOrderEvents(Guid orderId, DateTime since)
{
    var events = await nostify.GetAllEventsAsync<Order>(orderId);
    return events
        .Where(e => e.timestamp >= since)
        .OrderBy(e => e.timestamp)
        .Cast<IEvent>()
        .ToList();
}
```

## Event Sourcing Principles

Events following this interface support:

1. **Immutability** - Events never change after creation
2. **Append-Only** - New events are added, never modified
3. **Replayability** - Aggregate state can be rebuilt from events
4. **Auditability** - Complete history with user attribution
5. **Temporal Queries** - State at any point in time

## Related Types

- [Event](Event.spec.md) - Primary implementation
- [NostifyCommand](NostifyCommand.spec.md) - Command definition
- [IAggregate](IAggregate.spec.md) - Aggregate interface
- [NostifyKafkaTriggerEvent](NostifyKafkaTriggerEvent.spec.md) - Kafka wrapper
