# NostifyCommand Class Specification

## Overview

`NostifyCommand` is an obsolete legacy metadata object retained for backward compatibility with older event documents and APIs that still use `event.command`. It is no longer an `EventType` subclass.

## Class Definition

```csharp
public class NostifyCommand
```

## Constructor

```csharp
public NostifyCommand(string name, bool isNew = false, bool allowNullPayload = false)
```

Creates legacy command metadata.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | `string` | Required | Logical event/command name (Kafka topic name) |
| `isNew` | `bool` | `false` | Whether the event creates a new aggregate |
| `allowNullPayload` | `bool` | `false` | Whether null payloads are allowed |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `name` | `string` | Logical event/command name |
| `isNew` | `bool` | Create-aggregate flag |
| `allowNullPayload` | `bool` | Null payload allowance |

## Equality

Equality matches `EventType` semantics: two commands are equal when they have the same concrete CLR type and the same `name`.

`==` and `!=` operators are overloaded and handle null values.

## Interop With Event

- `Event.command` is the legacy compatibility surface.
- Setting `Event.command` hydrates `Event.eventType` with an internal legacy adapter (`LegacyNostifyCommandEventType`) when needed.
- Reading `Event.command` from a typed event returns a compatibility shim mirroring `eventType` metadata.
- `NostifyCommand` has an implicit conversion to `EventType`, enabling existing command-based APIs to flow into `EventType` signatures without changing call sites.

## Migration Guidance

For new development, define concrete event types using `EventType<TSelf>` and publish/dispatch through `eventType`. Keep `NostifyCommand` only where legacy payloads or APIs still require it.

## Related Types

- [EventType](EventType.spec.md) - Canonical typed metadata model
- [Event](Event.spec.md) - Carries canonical `eventType` and legacy `command`
