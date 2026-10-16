# RequiredForAttribute Specification

## Overview

`RequiredForAttribute` is a custom validation attribute that marks properties as required only for specific event types. It enables event-specific validation in event-sourcing applications.

## Class Definition

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class RequiredForAttribute : RequiredAttribute, INostifyValidation
```

## Inheritance

- Extends `System.ComponentModel.DataAnnotations.RequiredAttribute`
- Implements `INostifyValidation`

## Constructors

### Single Event

```csharp
public RequiredForAttribute(string eventName)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `eventName` | `string` | Event type name for which this property is required |

### Multiple Events

```csharp
public RequiredForAttribute(params string[] eventNames)
```

The `params` overload accepts either an existing array or multiple event names directly.

| Parameter | Type | Description |
|-----------|------|-------------|
| `eventNames` | `string[]` | Event type names for which this property is required |

### Event Definition Types

```csharp
public RequiredForAttribute(params Type[] eventTypes)
```

Attribute constructors cannot be generic, so a `params T` constructor is not valid C#. This overload accepts CLR `Type` values and validates at runtime that every supplied type is a concrete `EventType` with a parameterless constructor. It resolves each definition's configured `EventType.name`; it does not use the CLR class name.

```csharp
[RequiredFor(typeof(CreateOrder), typeof(UpdateOrder))]
public decimal Total { get; set; }
```

The event definition classes derive from `EventType`, rather than `IEvent`. `IEvent` represents a persisted event envelope and therefore is not the appropriate type constraint for this metadata.

The NuGet package includes Roslyn diagnostic `NOSTIFY_EVENTTYPE_CHECK`, an error that checks every `typeof(...)` argument at build time. A type that does not derive from `EventType` is rejected before execution. Runtime validation remains authoritative for reflection-created attributes and also rejects abstract definitions or definitions without parameterless constructors.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Events` | `List<string>` | List of event types requiring this property |
| `Commands` | `List<string>` | Obsolete backwards-compatible alias that returns the same list as `Events`; removed in the next major version |

## Behavior

The `IsValid` method:

1. Gets the validation context.
2. Extracts the current `EventType` from the `eventType` context item, with legacy `command` context support.
3. If the event type is not in the `Events` list, returns `ValidationResult.Success` (skips validation).
4. If the event type is in the list, verifies that the property name exists in the original payload's `payloadProperties` set.
5. For a supplied property, invokes base `RequiredAttribute` validation to reject null, empty, or whitespace string values.

Checking original payload membership distinguishes an omitted non-nullable value type from an explicitly supplied default value after deserialization. For example, omitting a required `Guid` fails even though its deserialized value would be `Guid.Empty`; explicitly supplying `Guid.Empty` satisfies `RequiredFor` presence and can be rejected separately by a value-specific validator when needed.

The legacy `Commands` property refers to the same mutable list instance as `Events`, so existing consumers remain source- and behavior-compatible. It is marked obsolete and will be removed in the next major version.

## Usage Examples

### Basic Usage

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    // Required only for CreateOrder, resolved from its EventType definition
    [RequiredFor(typeof(CreateOrder))]
    public Guid CustomerId { get; set; }
    
    // Required for both Create and Update
    [RequiredFor(typeof(CreateOrder), typeof(UpdateOrder))]
    public decimal Total { get; set; }
    
    // Required only for ShipOrder
    [RequiredFor("ShipOrder")]
    public string ShippingAddress { get; set; }
    
    // Not validated (no attribute)
    public string Notes { get; set; }
}
```

### With Command Constants

```csharp
public static class OrderCommands
{
    public const string Create = "CreateOrder";
    public const string Update = "UpdateOrder";
    public const string Ship = "ShipOrder";
    public const string Cancel = "CancelOrder";
}

public class Order : NostifyObject, IAggregate, IApplyable
{
    [RequiredFor(OrderCommands.Create)]
    public Guid CustomerId { get; set; }
    
    [RequiredFor(OrderCommands.Create, OrderCommands.Update)]
    public decimal Total { get; set; }
    
    [RequiredFor(OrderCommands.Ship)]
    public string ShippingAddress { get; set; }
}
```

### With Custom Error Messages

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    [RequiredFor("CreateOrder")]
    [Display(Name = "Customer")]
    public Guid CustomerId { get; set; }
    
    [RequiredFor("ShipOrder", ErrorMessage = "Shipping address is required for shipping")]
    public string ShippingAddress { get; set; }
}
```

### Validation in Event Handlers

```csharp
public async Task HandleCreateOrder(Event @event)
{
    // Validate event payload against Order aggregate
    var validationResults = NostifyValidationExceptionHandler.ValidateWithCommand(
        @event.GetPayload<Order>(),
        @event.command
    );
    
    if (validationResults != null)
    {
        throw new NostifyValidationException(validationResults);
    }
    
    // Process valid event...
}
```

## Validation Flow

```
Event Received
     │
     ▼
Extract Payload
     │
     ▼
Get Command Name
     │
     ▼
For Each Property:
     │
     ├─▶ Has [RequiredFor]? 
     │        │
     │        No ──▶ Skip Property
     │        │
     │        Yes
     │        │
     │        ▼
     │   Command in List?
     │        │
     │        No ──▶ Skip Validation
     │        │
     │        Yes
     │        │
     │        ▼
     │   Value Present?
     │        │
     │        No ──▶ Add Error
     │        │
     │        Yes ──▶ Pass
     │
     ▼
Return Results
```

## INostifyValidation Interface

```csharp
public interface INostifyValidation
{
    List<string> Events { get; }

    [Obsolete("Commands is retained for backwards compatibility and will be removed in the next major version. Use Events instead.")]
    List<string> Commands => Events;
}
```

`Events` is the canonical terminology. The default interface implementation preserves callers of `Commands`; custom validators must implement `Events`.

## Best Practices

1. **Use Constants** - Define event type names as constants
2. **Document Requirements** - Comment which properties each event needs
3. **Group Related Events** - Use multiple event types in one attribute when logical
4. **Custom Messages** - Provide clear error messages for better UX
5. **Validate Early** - Validate before processing events

## Comparison with Standard Required

| Feature | `[Required]` | `[RequiredFor]` |
|---------|--------------|-----------------|
| Always validates | ✓ | ✗ |
| Command-specific | ✗ | ✓ |
| Multiple conditions | ✗ | ✓ |
| Event sourcing aware | ✗ | ✓ |

## Related Types

- [INostifyValidation](INostifyValidation.spec.md) - Validation interface
- [NostifyValidationException](NostifyValidationException.spec.md) - Validation exception
- [NostifyValidationExceptionHandler](NostifyValidation.spec.md) - Validation utilities
- [NostifyCommand](NostifyCommand.spec.md) - Command class
