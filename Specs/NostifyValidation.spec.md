# NostifyValidation Utilities Specification

## Overview

`NostifyValidationExceptionHandler` is a static utility class providing consistent validation error handling and formatting across nostify applications.

## Class Definition

```csharp
public static class NostifyValidationExceptionHandler
```

## Methods

### ProcessValidationException

```csharp
public static ValidationErrorResponse ProcessValidationException(
    NostifyValidationException exception
)
```

Converts a validation exception to a structured response.

| Parameter | Type | Description |
|-----------|------|-------------|
| `exception` | `NostifyValidationException` | The validation exception |

**Returns:** `ValidationErrorResponse` - Structured error response

### ProcessValidationResults

```csharp
public static ValidationErrorResponse ProcessValidationResults(
    IEnumerable<ValidationResult> results
)
```

Converts validation results to a structured response.

| Parameter | Type | Description |
|-----------|------|-------------|
| `results` | `IEnumerable<ValidationResult>` | Validation results |

**Returns:** `ValidationErrorResponse` - Structured error response

### GetErrorResponseJson

```csharp
public static string GetErrorResponseJson(ValidationErrorResponse response)
```

Serializes an error response to JSON.

| Parameter | Type | Description |
|-----------|------|-------------|
| `response` | `ValidationErrorResponse` | Error response to serialize |

**Returns:** `string` - JSON string

### CreateErrorString

```csharp
public static string CreateErrorString(IEnumerable<ValidationResult> results)
```

Creates a concatenated error message string.

| Parameter | Type | Description |
|-----------|------|-------------|
| `results` | `IEnumerable<ValidationResult>` | Validation results |

**Returns:** `string` - Concatenated error messages

### Validate

```csharp
public static List<ValidationResult>? Validate(object obj)
```

Validates any object using data annotations.

| Parameter | Type | Description |
|-----------|------|-------------|
| `obj` | `object` | Object to validate |

**Returns:** `List<ValidationResult>?` - Null if valid, list of errors otherwise

### ValidateWithCommand

```csharp
public static List<ValidationResult>? ValidateWithCommand(
    object obj, 
    NostifyCommand command
)
```

Validates with command context for `[RequiredFor]` attributes.

| Parameter | Type | Description |
|-----------|------|-------------|
| `obj` | `object` | Object to validate |
| `command` | `NostifyCommand` | Current command context |

**Returns:** `List<ValidationResult>?` - Null if valid, list of errors otherwise

## Response Types

### ValidationErrorResponse

```csharp
public class ValidationErrorResponse
{
    public string Type { get; set; } = "validation";
    public string Title { get; set; } = "Validation Failed";
    public List<ValidationErrorDetail> Errors { get; set; }
}
```

### ValidationErrorDetail

```csharp
public class ValidationErrorDetail
{
    public string Field { get; set; }
    public string Message { get; set; }
}
```

## Usage Examples

### Basic Validation

```csharp
var order = new Order { Total = 0 };
var errors = NostifyValidationExceptionHandler.Validate(order);

if (errors != null)
{
    throw new NostifyValidationException(errors);
}
```

### Command-Aware Validation

```csharp
public async Task HandleCreateOrder(Event @event)
{
    var payload = @event.GetPayload<Order>();
    var errors = NostifyValidationExceptionHandler.ValidateWithCommand(
        payload, 
        @event.command
    );
    
    if (errors != null)
    {
        throw new NostifyValidationException(errors);
    }
    
    // Process valid order...
}
```

### HTTP Response Formatting

```csharp
[Function("CreateOrder")]
public async Task<IActionResult> CreateOrder(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
{
    try
    {
        var order = await req.ReadFromJsonAsync<Order>();
        var errors = NostifyValidationExceptionHandler.ValidateWithCommand(
            order, 
            NostifyCommand.Create("Order")
        );
        
        if (errors != null)
        {
            var response = NostifyValidationExceptionHandler.ProcessValidationResults(errors);
            return new BadRequestObjectResult(response);
        }
        
        // Create order...
        return new OkResult();
    }
    catch (NostifyValidationException ex)
    {
        var response = NostifyValidationExceptionHandler.ProcessValidationException(ex);
        var json = NostifyValidationExceptionHandler.GetErrorResponseJson(response);
        return new BadRequestObjectResult(json);
    }
}
```

### Error String for Logging

```csharp
var errors = NostifyValidationExceptionHandler.Validate(order);
if (errors != null)
{
    var errorString = NostifyValidationExceptionHandler.CreateErrorString(errors);
    _logger.LogWarning("Validation failed: {Errors}", errorString);
}
```

## JSON Response Format

```json
{
    "type": "validation",
    "title": "Validation Failed",
    "errors": [
        {
            "field": "CustomerId",
            "message": "The CustomerId field is required."
        },
        {
            "field": "Total",
            "message": "The Total field must be greater than 0."
        }
    ]
}
```

## Integration with Middleware

Used by `NostifyValidationExceptionMiddleware` for Azure Functions:

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
            var response = NostifyValidationExceptionHandler.ProcessValidationException(ex);
            // Return HTTP 400 with response body...
        }
    }
}
```

## Best Practices

1. **Use ValidateWithCommand** - For event sourcing scenarios
2. **Structured Responses** - Use `ValidationErrorResponse` for APIs
3. **Early Validation** - Validate before processing
4. **Consistent Format** - Use handler methods for consistent error format

## Related Types

- [NostifyValidationException](NostifyValidationException.spec.md) - Validation exception
- [RequiredForAttribute](RequiredForAttribute.spec.md) - Command-specific validation
- [NostifyValidationExceptionMiddleware](NostifyValidationExceptionMiddleware.spec.md) - Azure Functions middleware
