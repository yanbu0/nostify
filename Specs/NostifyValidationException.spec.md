# NostifyValidationException Class Specification

## Overview

`NostifyValidationException` is thrown when validation fails for nostify objects. It extends `ValidationException` and provides structured access to validation errors.

## Class Definition

```csharp
public class NostifyValidationException : ValidationException
```

## Constructors

### Single Message

```csharp
public NostifyValidationException(string message) : base(message)
```

Creates exception with a simple error message.

### Multiple Results

```csharp
public NostifyValidationException(IEnumerable<ValidationResult> validationResults)
```

Creates exception with detailed validation results.

### With Inner Exception

```csharp
public NostifyValidationException(string message, Exception innerException) 
    : base(message, innerException)
```

Creates exception with message and inner exception.

### Custom Message with Results

```csharp
public NostifyValidationException(string message, IEnumerable<ValidationResult> results) 
    : base(message)
```

Creates exception with custom message and validation results.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `ValidationMessages` | `IEnumerable<ValidationResult>` | Collection of validation failures |
| `Message` | `string` | Inherited from Exception |

## Methods

### GetAllErrorsAsString

```csharp
public string GetAllErrorsAsString()
```

Returns all validation errors as a single formatted string.

**Returns:** `string` - Concatenated error messages

### GetErrorsByMember

```csharp
public Dictionary<string, List<string>> GetErrorsByMember()
```

Groups errors by property/member name.

**Returns:** `Dictionary<string, List<string>>` - Errors grouped by member

## Usage Examples

### Basic Throwing

```csharp
// Simple message
throw new NostifyValidationException("Order total must be positive");

// With validation results
var results = new List<ValidationResult>
{
    new ValidationResult("CustomerId is required", new[] { "CustomerId" }),
    new ValidationResult("Total must be greater than 0", new[] { "Total" })
};
throw new NostifyValidationException(results);
```

### With Validation

```csharp
public async Task CreateOrder(Order order, NostifyCommand command)
{
    var errors = NostifyValidationExceptionHandler.ValidateWithCommand(order, command);
    
    if (errors != null)
    {
        throw new NostifyValidationException(errors);
    }
    
    // Process valid order...
}
```

### Catching and Handling

```csharp
try
{
    await CreateOrder(order, command);
}
catch (NostifyValidationException ex)
{
    // Get all errors as string
    var errorString = ex.GetAllErrorsAsString();
    _logger.LogWarning("Validation failed: {Errors}", errorString);
    
    // Get errors by member
    var errorsByMember = ex.GetErrorsByMember();
    foreach (var (member, errors) in errorsByMember)
    {
        Console.WriteLine($"{member}: {string.Join(", ", errors)}");
    }
}
```

### HTTP Response

```csharp
[Function("CreateOrder")]
public async Task<IActionResult> CreateOrder(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
{
    try
    {
        var order = await req.ReadFromJsonAsync<Order>();
        await ProcessOrder(order);
        return new OkResult();
    }
    catch (NostifyValidationException ex)
    {
        return new BadRequestObjectResult(new
        {
            type = "validation",
            title = "Validation Failed",
            errors = ex.GetErrorsByMember()
        });
    }
}
```

## Error Response Format

When using `GetErrorsByMember()`:

```json
{
    "CustomerId": ["CustomerId is required"],
    "Total": [
        "Total is required",
        "Total must be greater than 0"
    ],
    "ShippingAddress": ["ShippingAddress is required for shipping orders"]
}
```

When using `GetAllErrorsAsString()`:

```
CustomerId is required; Total must be greater than 0; ShippingAddress is required for shipping orders
```

## Integration with Middleware

The `NostifyValidationExceptionMiddleware` automatically catches this exception:

```csharp
public class NostifyValidationExceptionMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (NostifyValidationException ex)
        {
            // Returns HTTP 400 with structured error body
            await HandleValidationException(context, ex);
        }
    }
}
```

## Best Practices

1. **Use Structured Results** - Pass `ValidationResult` collection when possible
2. **Include Member Names** - Specify which properties failed
3. **Clear Messages** - Provide actionable error messages
4. **Handle in Middleware** - Use middleware for consistent HTTP responses
5. **Log Appropriately** - Log validation failures at Warning level

## Related Types

- [NostifyException](NostifyException.spec.md) - Base exception
- [NostifyValidation](NostifyValidation.spec.md) - Validation utilities
- [NostifyValidationExceptionMiddleware](NostifyValidationExceptionMiddleware.spec.md) - Azure Functions middleware
- [RequiredForAttribute](RequiredForAttribute.spec.md) - Validation attribute
