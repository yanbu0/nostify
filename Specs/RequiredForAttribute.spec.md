# RequiredForAttribute Specification

## Overview

`RequiredForAttribute` is a custom validation attribute that marks properties as required only for specific commands. It enables command-specific validation in event-sourcing applications.

## Class Definition

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class RequiredForAttribute : RequiredAttribute, INostifyValidation
```

## Inheritance

- Extends `System.ComponentModel.DataAnnotations.RequiredAttribute`
- Implements `INostifyValidation`

## Constructors

### Single Command

```csharp
public RequiredForAttribute(string command)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `command` | `string` | Command name for which this property is required |

### Multiple Commands

```csharp
public RequiredForAttribute(params string[] commands)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `commands` | `string[]` | Command names for which this property is required |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Commands` | `List<string>` | List of commands requiring this property |

## Behavior

The `IsValid` method:

1. Gets the validation context
2. Extracts the current `NostifyCommand` from context
3. If command is not in the `Commands` list, returns `ValidationResult.Success` (skips validation)
4. If command is in the list, invokes base `RequiredAttribute` validation

```csharp
protected override ValidationResult IsValid(object value, ValidationContext context)
{
    // Get current command from context
    var command = context.Items["NostifyCommand"] as NostifyCommand;
    
    // Skip validation if command not in list
    if (command == null || !Commands.Contains(command.name))
    {
        return ValidationResult.Success;
    }
    
    // Apply required validation
    return base.IsValid(value, context);
}
```

## Usage Examples

### Basic Usage

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    // Required only for CreateOrder
    [RequiredFor("CreateOrder")]
    public Guid CustomerId { get; set; }
    
    // Required for both Create and Update
    [RequiredFor("CreateOrder", "UpdateOrder")]
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
    List<string> Commands { get; }
}
```

This interface allows the validation system to identify command-aware validation attributes.

## Best Practices

1. **Use Constants** - Define command names as constants
2. **Document Requirements** - Comment which properties each command needs
3. **Group Related Commands** - Use multiple commands in single attribute when logical
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
- [NostifyValidationExceptionHandler](NostifyValidationExceptionHandler.spec.md) - Validation utilities
- [NostifyCommand](NostifyCommand.spec.md) - Command class
