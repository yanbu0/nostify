# EventType Class Specification

## Overview

`EventType` is the abstract runtime base for event metadata in nostify. Events carry `EventType` instances for serialization, compatibility, dispatch, and validation.

## Class Definition

```csharp
public abstract class EventType
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `name` | `string` | Unique event type name, also used as the Kafka topic name |
| `isNew` | `bool` | Indicates whether the event type creates a new aggregate |
| `allowNullPayload` | `bool` | Indicates whether this event type allows an empty payload |

## Constructor

```csharp
protected EventType(string name, bool isNew = false, bool allowNullPayload = false)
```

Concrete event types inherit directly from `EventType` and call this constructor to define metadata.

## Purpose

`EventType` lets events carry concrete CLR type metadata instead of only legacy command metadata. This enables:

- Runtime dispatch via `NostifyObject.Apply(IEvent)`
- Attribute-based dispatch via `[ApplyEvents(typeof(...))]`
- Event-type-aware payload validation
- Topic discovery from concrete event type definitions

## Equality

Equality is based on both concrete CLR type and `name`. Two different subclasses with the same `name` are not equal.

## Usage Example

```csharp
public sealed class CreateOrder : EventType
{
    public CreateOrder() : base("Create_Order", isNew: true)
    {
    }
}

EventType eventType = new CreateOrder();
```

## Definition Resolution

`EventType.GetRequiredInstance(Type)` resolves an `EventType` definition for a concrete CLR type. It:

1. Requires a concrete `EventType` subclass
2. Creates/returns a cached definition instance
3. Supports parameterless constructors and common `(string...)` constructor signatures
4. Throws a clear `InvalidOperationException` for unsupported types

## Backward Compatibility

`NostifyCommand` remains an obsolete legacy metadata object and is not an `EventType` subclass. Legacy command-only envelopes map to the internal compatibility adapter `LegacyNostifyCommandEventType` so modern `Event.eventType` behavior remains consistent.

## Related Types

- [NostifyCommand](NostifyCommand.spec.md) - Obsolete compatibility metadata type
- [Event](Event.spec.md) - Events carry an `eventType`
- [NostifyObject](NostifyObject.spec.md) - Dispatches `Apply(IEvent)` through `eventType`
