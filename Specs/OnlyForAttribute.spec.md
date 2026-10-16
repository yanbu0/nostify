# OnlyForAttribute Specification

## Overview

`OnlyForAttribute` is an event-aware validation attribute that restricts a property to one or more event types. A supplied property is invalid when the current event type is not in the configured allowlist. An omitted property remains valid, so the attribute does not make the property required.

## Class Definition

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class OnlyForAttribute : ValidationAttribute, INostifyValidation
```

## Constructors

```csharp
public OnlyForAttribute(string eventName)
public OnlyForAttribute(params string[] eventNames)
public OnlyForAttribute(params Type[] eventTypes)
```

The string overloads use logical event type names directly. The typed overload accepts concrete `EventType` definitions with parameterless constructors and resolves each definition's configured `EventType.name`.

```csharp
[OnlyFor(typeof(CreateOrder), typeof(UpdateOrder))]
public decimal Total { get; set; }
```

The package's Roslyn diagnostic `NOSTIFY001` checks each `typeof(...)` argument at build time and reports an error when it does not derive from `EventType`. Runtime validation remains authoritative for reflection-created attributes and rejects abstract definitions or definitions without parameterless constructors.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Events` | `List<string>` | Event type names for which the property may be supplied |
| `Commands` | `List<string>` | Obsolete compatibility alias for `Events`; removed in the next major version |

## Behavior

1. Resolve the current `EventType` from the validation context's `eventType` item, with legacy `command` support.
2. Return an event-context validation error when no event type can be resolved.
3. Return success when the current event type appears in `Events`.
4. For any other event type, return a member-scoped validation error if the property was supplied in the original payload.
5. Return success for an omitted property. `OnlyForAttribute` restricts use but does not require use.

Event identity matching uses the configured `EventType.name` and is case-sensitive.

## Combining with RequiredForAttribute

Use both attributes when a property must be present for particular events and must not be supplied for any others:

```csharp
[RequiredFor(typeof(CreateOrder), typeof(UpdateOrder))]
[OnlyFor(typeof(CreateOrder), typeof(UpdateOrder))]
public decimal Total { get; set; }
```

For the listed event types, `RequiredForAttribute` rejects omission and `OnlyForAttribute` permits the supplied property. For every other event type, `RequiredForAttribute` does not require the property and `OnlyForAttribute` rejects it when supplied.

## Custom Error Message

```csharp
[OnlyFor(typeof(CreateOrder), ErrorMessage = "Initial credit is only valid while creating an order.")]
public decimal InitialCredit { get; set; }
```

## Related Types

- [RequiredForAttribute](RequiredForAttribute.spec.md) - Requires a property for selected event types
- [INostifyValidation](INostifyValidation.spec.md) - Event-aware validation contract
- [Event](Event.spec.md) - Payload validation pipeline
- [NostifyValidationException](NostifyValidationException.spec.md) - Structured validation failure
