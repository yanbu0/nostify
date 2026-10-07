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
    EventType eventType,
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
| `eventType` | `EventType` | Yes | The event metadata to persist |
| `aggregateRootId` | `Guid` | Yes | The ID of the root aggregate to perform the command on |
| `payload` | `object` | Yes | The properties to update or the ID of the aggregate to delete |
| `userId` | `Guid` | No | The ID of the user responsible for the event |
| `partitionKey` | `Guid` | No | The partition key for the aggregate |

#### Create with payload-derived aggregateRootId

```csharp
public IEvent Create<T>(
    EventType eventType,
    object payload,
    Guid userId = default,
    Guid partitionKey = default)
    where T : class
```

Creates a new `Event` instance, parsing the `aggregateRootId` from the payload's `id` property.

#### Create with string parameters

```csharp
public IEvent Create<T>(
    EventType eventType,
    string aggregateRootId,
    object payload,
    string userId,
    string partitionKey)
    where T : NostifyObject, IAggregate
```

Creates a new `Event` instance, parsing `aggregateRootId`, `userId`, and `partitionKey` from string values.

### CreateNullPayloadEvent overloads

```csharp
public IEvent CreateNullPayloadEvent(EventType eventType, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default)
public IEvent CreateNullPayloadEvent(EventType eventType, string aggregateRootId, string userId, string partitionKey)
```

Creates null-payload events without invoking payload validation. These methods do **not** mutate `ValidatePayload` on the factory instance.

## Payload Validation

When `ValidatePayload` is `true` (default), the factory calls `Event.ValidatePayload<T>()` which:

1. Extracts properties from the payload.
2. Compares them against public instance properties of aggregate type `T`.
3. Validates values supplied by the payload.
4. Enforces omitted properties only when `[Required]` applies or `[RequiredFor(...)]` matches the current event type.
5. Throws `NostifyValidationException` when validation fails, including when the payload contains a property not found on the aggregate.

Payload validation supports partial objects. A UI should send only properties intentionally set by the current Create or Update event. It should not serialize the complete aggregate or submit unchanged form/model values. Omitted properties remain unchanged when the event is applied through `UpdateProperties<T>()`; present default-like values such as `null`, `false`, `0`, and empty values are explicit updates.

> **AI-agent implementation rule:** Do not use `NoValidate()` to enable ordinary partial Create or Update payloads. They are supported with validation enabled. Use `[RequiredFor(...)]` to express event-specific required fields, and bypass validation only when the application explicitly requires that separate behavior.

## Usage Examples

### Recommended Partial Payload with Validation

```csharp
// Only status and shippedDate are changed by this event. Other Order properties
// are deliberately absent and will remain unchanged during event application.
IEvent evt = new EventFactory().Create<Order>(
    new Update_Order(),
    orderId,
    new { status = "Shipped", shippedDate = DateTime.UtcNow },
    userId);
```

### Basic Usage with Validation

```csharp
var factory = new EventFactory();

var evt = factory.Create<Customer>(
    Customer.Create,
    customerId,
    new { name = "John Doe", email = "john@example.com" },
    userId);
```

### Skip Validation for an Explicit Exceptional Case

```csharp
var factory = new EventFactory();

// Validation bypass is reserved for intentionally unvalidated workflows such as
// controlled legacy imports. It is not needed for normal partial updates.
var evt = factory.NoValidate().Create<Customer>(
    Customer.LegacyImport,
    customerId,
    importedPayload,
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
- [`EventType`](../src/Event/EventType.cs) - Canonical typed event metadata
- [`NostifyCommand`](../src/NostifyCommand.cs) - Legacy compatibility metadata
- [`IAggregate`](../src/Shared_Interfaces/IAggregate.cs) - Interface for aggregate types

## Version History

- **4.2.1** - Fixed validation to use `BindingFlags.Public | BindingFlags.Instance`
- **4.0.0** - Initial release with fluent validation API
