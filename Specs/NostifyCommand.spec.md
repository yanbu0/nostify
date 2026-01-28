# NostifyCommand Class Specification

## Overview

`NostifyCommand` is the base class for all commands in the nostify framework. Commands represent user intentions that will be captured as events in the event store.

## Class Definition

```csharp
public class NostifyCommand
```

## Constructors

### Name-Only Constructor

```csharp
public NostifyCommand(string name)
```

Creates a command with just a name. Use this for simple commands without additional configuration.

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | The command name (used as Kafka topic) |

### Full Constructor

```csharp
public NostifyCommand(string name, string aggregateType, bool isExternalDataEvent = false)
```

Creates a command with full configuration.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | `string` | Required | The command name |
| `aggregateType` | `string` | Required | The type name of the aggregate this command affects |
| `isExternalDataEvent` | `bool` | `false` | Whether this is for external data projection initialization |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `name` | `string` | Command name (also used as Kafka topic name) |
| `aggregateType` | `string?` | The aggregate type this command targets |
| `isExternalDataEvent` | `bool` | Flag for external data events |

## Static Factory Methods

### Create

```csharp
public static NostifyCommand Create(string aggregateType)
```

Creates a standardized "Create" command for an aggregate type.

**Returns:** `NostifyCommand` with name format `"Create{AggregateType}"`

### Update

```csharp
public static NostifyCommand Update(string aggregateType)
```

Creates a standardized "Update" command for an aggregate type.

**Returns:** `NostifyCommand` with name format `"Update{AggregateType}"`

### Delete

```csharp
public static NostifyCommand Delete(string aggregateType)
```

Creates a standardized "Delete" command for an aggregate type.

**Returns:** `NostifyCommand` with name format `"Delete{AggregateType}"`

### ExternalData

```csharp
public static NostifyCommand ExternalData(string sourceAggregateType)
```

Creates a command for external data projection initialization.

**Returns:** `NostifyCommand` with `isExternalDataEvent = true`

## Usage Examples

### Basic Command Creation

```csharp
// Using static factory methods
var createCommand = NostifyCommand.Create("Order");
var updateCommand = NostifyCommand.Update("Order");
var deleteCommand = NostifyCommand.Delete("Order");

// Using constructors
var customCommand = new NostifyCommand("ShipOrder", "Order");
```

### With Events

```csharp
// Create an event with a command
var @event = new Event(
    NostifyCommand.Create("Order"),
    orderId,
    new { CustomerId = customerId, Total = 99.99m },
    userId
);

await nostify.PublishEventAsync(@event);
```

### Custom Commands

```csharp
// Define domain-specific commands
public static class OrderCommands
{
    public static NostifyCommand Create => NostifyCommand.Create("Order");
    public static NostifyCommand Update => NostifyCommand.Update("Order");
    public static NostifyCommand Ship => new NostifyCommand("ShipOrder", "Order");
    public static NostifyCommand Cancel => new NostifyCommand("CancelOrder", "Order");
    public static NostifyCommand Refund => new NostifyCommand("RefundOrder", "Order");
}

// Usage
var @event = new Event(OrderCommands.Ship, orderId, payload, userId);
```

### With Validation

```csharp
// Commands work with RequiredFor attribute
public class Order : NostifyObject, IAggregate, IApplyable
{
    [RequiredFor("CreateOrder")]
    public Guid CustomerId { get; set; }
    
    [RequiredFor("CreateOrder", "UpdateOrder")]
    public decimal Total { get; set; }
    
    [RequiredFor("ShipOrder")]
    public string ShippingAddress { get; set; }
}
```

## Kafka Topic Naming

The `name` property is used directly as the Kafka topic name. This enables:

1. **Event Routing** - Consumers subscribe to specific command topics
2. **Filtering** - Process only relevant events
3. **Scaling** - Independent scaling of command handlers

**Naming Convention:**
- `Create{AggregateType}` - Creation events
- `Update{AggregateType}` - Update events
- `Delete{AggregateType}` - Deletion events
- `{Action}{AggregateType}` - Custom actions

## Best Practices

1. **Use Factory Methods** - Prefer `Create()`, `Update()`, `Delete()` for standard operations
2. **Define Command Constants** - Create static command classes per aggregate type
3. **Consistent Naming** - Follow `{Action}{AggregateType}` pattern
4. **Validation Integration** - Use `[RequiredFor]` attributes with command names

## Related Types

- [Event](Event.spec.md) - Events contain commands
- [RequiredForAttribute](RequiredForAttribute.spec.md) - Command-specific validation
- [NostifyValidation](NostifyValidation.spec.md) - Validation utilities
