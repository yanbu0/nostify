# EventType Class Specification

## Overview

`EventType` is the abstract runtime base for event metadata in nostify. Public APIs such as `IEvent.eventType`, serializers, event factories, and dispatch infrastructure all rely on this non-generic polymorphic base, while public concrete event types must inherit from `EventType<TSelf>` so they expose a canonical singleton-style `Instance`.

## Class Definition

```csharp
public abstract class EventType
public abstract class EventType<TSelf> : EventType where TSelf : EventType<TSelf>, new()
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
internal EventType(string name, bool isNew = false, bool allowNullPayload = false)
```

Creates a new typed event metadata object. The non-generic constructor is `internal` so external callers cannot inherit directly from `EventType`, which means public event metadata should no longer use `class X : EventType`.

### Generic Constructor

```csharp
protected EventType(string name, bool isNew = false, bool allowNullPayload = false)
```

Used by `EventType<TSelf>` so generated and external concrete event types can initialize their canonical metadata.

## Purpose

`EventType` exists so events can carry a concrete CLR type instead of only a value-like `NostifyCommand` instance. This enables `NostifyObject.Apply(IEvent)` to dispatch by runtime event type using overload resolution when derived aggregates or projections provide more specific `Apply(...)` overloads.

`EventType<TSelf>.Instance` is the authoritative source of metadata for a concrete event type. Nostify now resolves concrete event types through this canonical instance for:

- Kafka topic auto-discovery
- Newtonsoft.Json / System.Text.Json / Cosmos event-type hydration
- `[ApplyEvents(typeof(...))]` handler mapping

## Equality

Equality is based on both the concrete CLR type and `name`. Two event types with the same `name` but different subclasses do not compare as equal.

## Usage Example

```csharp
public sealed class CreateOrder : EventType<CreateOrder>
{
    public CreateOrder() : base("Create_Order", true)
    {
    }
}

EventType eventType = CreateOrder.Instance;
```

## Canonical Instance Resolution

`EventType.GetRequiredInstance(Type)` is an internal shared resolver used by topic discovery, serializers, and attribute dispatch. It:

1. Requires a concrete `EventType` subclass
2. Finds a public static `Instance` property (including inherited static members from `EventType<TSelf>`)
3. Validates the property returns exactly the requested concrete type
4. Throws a clear `InvalidOperationException` when a type is abstract, missing `Instance`, or exposes a mismatched canonical instance

## Backward Compatibility

`NostifyCommand` currently remains in the codebase as an obsolete subclass of `EventType`. Existing code that still uses `NostifyCommand` continues to work as a legacy compatibility exception, while new code should inherit from `EventType<TSelf>` and use the inherited canonical `Instance`.

## Related Types

- [NostifyCommand](NostifyCommand.spec.md) - Obsolete compatibility subclass
- [Event](Event.spec.md) - Events carry an `eventType`
- [NostifyObject](NostifyObject.spec.md) - Dispatches `Apply(IEvent)` through `eventType`
