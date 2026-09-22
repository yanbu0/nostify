# EventType Class Specification

## Overview

`EventType` is the abstract runtime base for event metadata in nostify. Public APIs such as `IEvent.eventType`, serializers, event factories, and dispatch infrastructure rely on this polymorphic base, while concrete event types now also implement `IEventType` so topic metadata can be read from a static name contract.

## Class Definition

```csharp
public interface IEventType
{
    public static abstract string name { get; }
}

public abstract class EventType
public abstract class EventType<TSelf> : EventType where TSelf : EventType<TSelf>, IEventType, new()
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

`EventType<TSelf>.Instance` remains the authoritative runtime instance for dispatch and serialization, while `IEventType.name` is the authoritative static topic identifier for discovery in `NostifyFactory.Build`.

Nostify resolves concrete event types through the canonical instance for:

- Newtonsoft.Json / System.Text.Json / Cosmos event-type hydration
- `[ApplyEvents(typeof(...))]` handler mapping
- For Kafka topic auto-discovery, `Build<T>()` reads static `IEventType.name`

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

`EventType.GetRequiredInstance(Type)` is an internal shared resolver used by serializers and attribute dispatch. It:

1. Requires a concrete `EventType` subclass
2. Finds a public static `Instance` property (including inherited static members from `EventType<TSelf>`)
3. Validates the property returns exactly the requested concrete type
4. Throws a clear `InvalidOperationException` when a type is abstract, missing `Instance`, or exposes a mismatched canonical instance

## Backward Compatibility

`NostifyCommand` remains in the codebase as an obsolete legacy metadata object, but it is no longer an `EventType` subclass. Legacy command-only envelopes map to an internal compatibility adapter (`LegacyNostifyCommandEventType`) so `Event.eventType` stays canonical while old payloads remain readable.

## Related Types

- [NostifyCommand](NostifyCommand.spec.md) - Obsolete compatibility metadata type
- [Event](Event.spec.md) - Events carry an `eventType`
- [NostifyObject](NostifyObject.spec.md) - Dispatches `Apply(IEvent)` through `eventType`
