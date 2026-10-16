# TogetherForAttribute Specification

## Overview

`TogetherForAttribute` defines an event-specific group of payload properties that must be supplied together. The group is optional by default: omitting every group member is valid. Supplying any active member requires every active member with the same group name to be present in that payload object.

Presence is based on JSON payload membership. An explicitly supplied `null`, `false`, `0`, or empty value counts as supplied; other validation attributes may independently reject that value.

## Declaration

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class TogetherForAttribute : ValidationAttribute, INostifyValidation
```

Multiple declarations are allowed so one property can participate in multiple uniquely named groups.

## Constructors

```csharp
public TogetherForAttribute(string uniqueGroupName, params string[] eventNames)

public TogetherForAttribute(
    string uniqueGroupName,
    Type eventType,
    params Type[] additionalEventTypes)
```

The typed overload resolves each concrete `EventType` definition's configured `name`. Runtime validation rejects types that are not concrete `EventType` definitions with parameterless constructors. Roslyn diagnostic `NOSTIFY_EVENTTYPE_CHECK` rejects invalid `typeof(...)` arguments at compile time.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `UniqueGroupName` | `string` | Name associating properties in one group within a payload model |
| `Events` | `List<string>` | Event type names for which the group is active |
| `Commands` | `List<string>` | Obsolete compatibility alias for `Events` |

## Behavior

For the current payload object and event type:

1. Attributes not configured for the current event type do nothing.
2. If no active property in a named group is supplied, the group is valid.
3. If any active group property is supplied, every active property in the group must be supplied.
4. A matching `RequiredForAttribute` on any group member activates the group even when every member is omitted. This requires the entire group and allows both attributes to compose predictably.
5. Nested models and collection elements are evaluated independently, and failures use fully qualified payload paths.

A group name should be unique for one logical property set within a model type. Group membership does not cross object instances or nesting levels.

## Examples

### Optional Group

```csharp
public sealed class ShippingUpdate
{
    [TogetherFor("shipping-window", typeof(ScheduleShipment))]
    public DateTimeOffset? StartsAt { get; set; }

    [TogetherFor("shipping-window", typeof(ScheduleShipment))]
    public DateTimeOffset? EndsAt { get; set; }
}
```

For `ScheduleShipment`, a payload may omit both properties or supply both. Supplying only one is invalid.

### Required Group

```csharp
public sealed class ShippingUpdate
{
    [RequiredFor(typeof(ScheduleShipment))]
    [TogetherFor("shipping-window", typeof(ScheduleShipment))]
    public DateTimeOffset? StartsAt { get; set; }

    [TogetherFor("shipping-window", typeof(ScheduleShipment))]
    public DateTimeOffset? EndsAt { get; set; }
}
```

`RequiredFor` activates the group for `ScheduleShipment`; therefore both `StartsAt` and `EndsAt` must be present.

## Related Types

- [RequiredForAttribute](RequiredForAttribute.spec.md) - Requires properties and can activate a together group
- [INostifyValidation](INostifyValidation.spec.md) - Event-aware validation contract
- [Event](Event.spec.md) - Recursive payload validation pipeline
- [NostifyValidationException](NostifyValidationException.spec.md) - Structured validation failure
