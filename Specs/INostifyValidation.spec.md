# INostifyValidation Interface Specification

## Overview

`INostifyValidation` is a marker interface for validation attributes that support event-type-aware validation in the nostify framework.

## Interface Definition

```csharp
public interface INostifyValidation
{
    List<string> Events { get; }

    [Obsolete("Commands is retained for backwards compatibility and will be removed in the next major version. Use Events instead.")]
    List<string> Commands => Events;
}
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Events` | `List<string>` | List of event type names for which this validation applies |
| `Commands` | `List<string>` | Obsolete backwards-compatible alias; scheduled for removal in the next major version |

## Purpose

This interface enables the validation system to:

1. Identify validation attributes that are event-specific
2. Extract the list of relevant event type names
3. Apply validation conditionally based on the current event context

`Events` is the canonical contract. The default obsolete `Commands` implementation delegates to `Events`, preserving callers of the old property while requiring implementers to adopt the new terminology. This is a source-breaking change for external custom implementations of `INostifyValidation`; those implementations must rename or add their property to `Events` when updating.

## Implementation

The primary implementation is [RequiredForAttribute](RequiredForAttribute.spec.md):

```csharp
public class RequiredForAttribute : RequiredAttribute, INostifyValidation
{
    public List<string> Events { get; }

    [Obsolete("Commands is retained for backwards compatibility and will be removed in the next major version. Use Events instead.")]
    public List<string> Commands => Events;

    public RequiredForAttribute(params string[] eventNames)
    {
        Events = [.. eventNames];
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
- [NostifyValidationExceptionHandler](NostifyValidation.spec.md) - Validation utilities
- [NostifyCommand](NostifyCommand.spec.md) - Command context
