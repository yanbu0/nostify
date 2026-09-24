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

`EventType` gives runtime code concrete event definitions while `name` remains the stable wire and dispatch identity. This enables:

- Runtime dispatch via `NostifyObject.Apply(IEvent)`
- Attribute-based dispatch via `[ApplyEvents(typeof(...))]`
- Event-type-aware payload validation
- Topic discovery from concrete event type definitions

## Identity and Equality

Persisted identity and attribute dispatch use `name` with ordinal, case-sensitive comparison. Names that differ only by case are distinct, and multiple loaded concrete definitions with the same exact name are rejected during name resolution.

In-memory object equality remains based on both concrete CLR type and `name`; dispatch does not depend on that CLR-sensitive equality.

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
3. Requires a public parameterless constructor so metadata is resolved from the concrete type itself
4. Throws a clear `InvalidOperationException` for unsupported types

## Serialization and Backward Compatibility

JSON stores stable logical metadata (`name`, `isNew`, and `allowNullPayload`) and restores a unique concrete definition by exact name when available. Canonical concrete metadata takes precedence over serialized flags; unknown names preserve their serialized flags in the internal `LegacyNostifyCommandEventType` adapter.

`NostifyCommand` remains an obsolete legacy metadata object and is not an `EventType` subclass. Legacy command-only envelopes map to the adapter so modern `Event.eventType` behavior remains consistent.

## Related Types

- [NostifyCommand](NostifyCommand.spec.md) - Obsolete compatibility metadata type
- [Event](Event.spec.md) - Events carry an `eventType`
- [NostifyObject](NostifyObject.spec.md) - Dispatches `Apply(IEvent)` through `eventType`
