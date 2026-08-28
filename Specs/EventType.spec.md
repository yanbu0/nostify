# EventType Class Specification

## Overview

`EventType` is the new abstract base class for event metadata in nostify. It replaces `NostifyCommand` as the primary abstraction for identifying what kind of event is being persisted and applied.

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

## Constructors

### Base Constructor

```csharp
protected EventType(string name, bool isNew = false, bool allowNullPayload = false)
```

Creates a new typed event metadata object.

## Purpose

`EventType` exists so events can carry a concrete CLR type instead of only a value-like `NostifyCommand` instance. This enables `NostifyObject.Apply(IEvent)` to dispatch by runtime event type using overload resolution when derived aggregates or projections provide more specific `Apply(...)` overloads.

## Equality

Equality is value-based on `name`. Two event types with the same `name` compare as equal even if they are different concrete subclasses.

## Usage Example

```csharp
public sealed class CreateOrder : EventType
{
    public static readonly CreateOrder Instance = new();

    private CreateOrder() : base("Create_Order", true)
    {
    }
}
```

## Backward Compatibility

`NostifyCommand` currently remains in the codebase as an obsolete subclass of `EventType`. Existing code that still uses `NostifyCommand` continues to work, while new code can inherit directly from `EventType`.

## Related Types

- [NostifyCommand](NostifyCommand.spec.md) - Obsolete compatibility subclass
- [Event](Event.spec.md) - Events carry an `eventType`
- [NostifyObject](NostifyObject.spec.md) - Dispatches `Apply(IEvent)` through `eventType`
