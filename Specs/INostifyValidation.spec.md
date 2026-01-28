# INostifyValidation Interface Specification

## Overview

`INostifyValidation` is a marker interface for validation attributes that support command-aware validation in the nostify framework.

## Interface Definition

```csharp
public interface INostifyValidation
{
    List<string> Commands { get; }
}
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Commands` | `List<string>` | List of command names for which this validation applies |

## Purpose

This interface enables the validation system to:

1. Identify validation attributes that are command-specific
2. Extract the list of relevant commands
3. Apply validation conditionally based on current command context

## Implementation

The primary implementation is [RequiredForAttribute](RequiredForAttribute.spec.md):

```csharp
public class RequiredForAttribute : RequiredAttribute, INostifyValidation
{
    public List<string> Commands { get; private set; }
    
    public RequiredForAttribute(params string[] commands)
    {
        Commands = commands.ToList();
    }
}
```

## Custom Validation Attributes

Create custom command-aware validators by implementing this interface:

```csharp
public class RangeForAttribute : RangeAttribute, INostifyValidation
{
    public List<string> Commands { get; private set; }
    
    public RangeForAttribute(string command, double minimum, double maximum)
        : base(minimum, maximum)
    {
        Commands = new List<string> { command };
    }
    
    protected override ValidationResult IsValid(object value, ValidationContext context)
    {
        var command = context.Items["NostifyCommand"] as NostifyCommand;
        
        if (command == null || !Commands.Contains(command.name))
        {
            return ValidationResult.Success;
        }
        
        return base.IsValid(value, context);
    }
}
```

## Usage Example

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    [RequiredFor("CreateOrder")]
    public Guid CustomerId { get; set; }
    
    [RequiredFor("CreateOrder", "UpdateOrder")]
    [RangeFor("CreateOrder", 0.01, double.MaxValue)]
    public decimal Total { get; set; }
}
```

## Validation System Integration

The validation handler checks for `INostifyValidation`:

```csharp
public static List<ValidationResult>? ValidateWithCommand(object obj, NostifyCommand command)
{
    var context = new ValidationContext(obj);
    context.Items["NostifyCommand"] = command;
    
    var results = new List<ValidationResult>();
    Validator.TryValidateObject(obj, context, results, validateAllProperties: true);
    
    return results.Any() ? results : null;
}
```

## Related Types

- [RequiredForAttribute](RequiredForAttribute.spec.md) - Primary implementation
- [NostifyValidationExceptionHandler](NostifyValidationExceptionHandler.spec.md) - Validation utilities
- [NostifyCommand](NostifyCommand.spec.md) - Command context
