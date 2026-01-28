# EventFactory Specification

## Overview

`EventFactory` provides methods to build and validate `Event` instances for the event store. It offers a fluent API for creating events with optional payload validation against aggregate types.

## Class Definition

```csharp
public class EventFactory
```

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ValidatePayload` | `bool` | `true` | Gets or sets whether to validate the payload against the aggregate type |

## Constructor

```csharp
public EventFactory()
```

Initializes a new instance with validation enabled by default.

## Methods

### NoValidate

```csharp
public EventFactory NoValidate()
```

Sets the factory to skip payload validation. Returns `this` for fluent chaining.

**Returns:** The current `EventFactory` instance.

### Create Overloads

#### Create with explicit aggregateRootId (Guid parameters)

```csharp
public IEvent Create<T>(
    NostifyCommand command,
    Guid aggregateRootId,
    object payload,
    Guid userId = default,
    Guid partitionKey = default)
    where T : class
```

Creates a new `Event` instance with explicit aggregate root ID.

**Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `command` | `NostifyCommand` | Yes | The command to persist |
| `aggregateRootId` | `Guid` | Yes | The ID of the root aggregate to perform the command on |
| `payload` | `object` | Yes | The properties to update or the ID of the aggregate to delete |
| `userId` | `Guid` | No | The ID of the user responsible for the event |
| `partitionKey` | `Guid` | No | The partition key for the aggregate |

#### Create with payload-derived aggregateRootId

```csharp
public IEvent Create<T>(
    NostifyCommand command,
    object payload,
    Guid userId = default,
    Guid partitionKey = default)
    where T : class
```

Creates a new `Event` instance, parsing the `aggregateRootId` from the payload's `id` property.

#### Create with string parameters

```csharp
public IEvent Create<T>(
    NostifyCommand command,
    string aggregateRootId,
    object payload,
    string userId,
    string partitionKey)
    where T : NostifyObject, IAggregate
```

Creates a new `Event` instance, parsing `aggregateRootId`, `userId`, and `partitionKey` from string values.

## Payload Validation

When `ValidatePayload` is `true` (default), the factory calls `Event.ValidatePayload<T>()` which:

1. Extracts properties from the payload
2. Compares them against public instance properties of the aggregate type `T`
3. Throws `ValidationException` if payload contains properties not found in the aggregate

This ensures that events only contain valid data for the target aggregate type.

## Usage Examples

### Basic Usage with Validation

```csharp
var factory = new EventFactory();

var evt = factory.Create<Customer>(
    Customer.Create,
    customerId,
    new { name = "John Doe", email = "john@example.com" },
    userId);
```

### Skip Validation

```csharp
var factory = new EventFactory();

var evt = factory.NoValidate().Create<Customer>(
    Customer.Update,
    customerId,
    new { temporaryField = "value" },  // Won't be validated
    userId);
```

### With Payload-Derived ID

```csharp
var factory = new EventFactory();

var evt = factory.Create<Order>(
    Order.Create,
    new { id = orderId, customerId = customer.id, total = 99.99m },
    userId);
```

### With String Parameters

```csharp
var factory = new EventFactory();

var evt = factory.Create<Product>(
    Product.Create,
    aggregateRootIdString,
    new { name = "Widget", price = 19.99m },
    userIdString,
    partitionKeyString);
```

## Design Notes

- The factory is **not** related to `ExternalDataEventFactory` despite the similar naming
- `EventFactory` creates new events for the event store
- `ExternalDataEventFactory` fetches existing events from external sources for projection initialization

## Related Classes

- [`Event`](../src/Event/Event.cs) - The event class created by this factory
- [`NostifyCommand`](../src/NostifyCommand.cs) - Command definitions for aggregates
- [`IAggregate`](../src/Shared_Interfaces/IAggregate.cs) - Interface for aggregate types

## Version History

- **4.2.1** - Fixed validation to use `BindingFlags.Public | BindingFlags.Instance`
- **4.0.0** - Initial release with fluent validation API
