# NostifyValidationExceptionMiddleware Specification

## Overview

`NostifyValidationExceptionMiddleware` is Azure Functions middleware that catches `NostifyValidationException` and converts it to HTTP 400 Bad Request responses with structured JSON error bodies.

## Class Definition

```csharp
public class NostifyValidationExceptionMiddleware : IFunctionsWorkerMiddleware
```

## Interface Implementation

Implements `Microsoft.Azure.Functions.Worker.Middleware.IFunctionsWorkerMiddleware`.

## Methods

### Invoke

```csharp
public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
```

Middleware pipeline method that wraps function execution.

| Parameter | Type | Description |
|-----------|------|-------------|
| `context` | `FunctionContext` | Function execution context |
| `next` | `FunctionExecutionDelegate` | Next middleware in pipeline |

## Response Types

### ValidationErrorResponse

```csharp
internal class ValidationErrorResponse
{
    public string Type { get; set; } = "validation";
    public string Title { get; set; } = "Validation Failed";
    public List<ValidationErrorDetail> Errors { get; set; }
}
```

### ValidationErrorDetail

```csharp
internal class ValidationErrorDetail
{
    public string Field { get; set; }
    public string Message { get; set; }
}
```

## Behavior

The middleware:

1. Calls the next middleware/function in the pipeline
2. Catches `NostifyValidationException` (including as inner exception)
3. For HTTP-triggered functions, returns HTTP 400 with JSON body
4. For non-HTTP functions, logs the error

## Usage

### Registration in Program.cs

```csharp
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker =>
    {
        worker.UseMiddleware<NostifyValidationExceptionMiddleware>();
    })
    .Build();

host.Run();
```

### Automatic Error Handling

```csharp
[Function("CreateOrder")]
public async Task<IActionResult> CreateOrder(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
{
    var order = await req.ReadFromJsonAsync<Order>();
    
    // This will be caught by middleware
    var errors = NostifyValidationExceptionHandler.ValidateWithCommand(
        order, 
        NostifyCommand.Create("Order")
    );
    
    if (errors != null)
    {
        throw new NostifyValidationException(errors);
    }
    
    // Process order...
    return new OkResult();
}
```

### Response Format

The middleware produces this JSON response:

```json
{
    "type": "validation",
    "title": "Validation Failed",
    "errors": [
        {
            "field": "customerId",
            "message": "CustomerId is required"
        },
        {
            "field": "total",
            "message": "Total must be greater than 0"
        }
    ]
}
```

## HTTP Response

| Status Code | Content-Type | Description |
|-------------|--------------|-------------|
| 400 | application/json | Validation failed |

## Serialization

Uses camelCase JSON serialization:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
```

## Error Handling Flow

```
HTTP Request
     │
     ▼
┌─────────────────────────────────────┐
│ NostifyValidationExceptionMiddleware │
└─────────────────┬───────────────────┘
                  │
                  ▼
           ┌──────────────┐
           │ Azure Function│
           └──────┬───────┘
                  │
         ┌───────┴───────┐
         │               │
    Success        Throws NostifyValidationException
         │               │
         ▼               ▼
    HTTP 200      HTTP 400 + JSON Error
```

## Best Practices

1. **Register Early** - Add middleware before other custom middleware
2. **Throw Early** - Validate and throw at the start of functions
3. **Use with Handler** - Combine with `NostifyValidationExceptionHandler`
4. **Consistent Format** - All validation errors follow same structure

## Non-HTTP Functions

For non-HTTP triggered functions (e.g., Kafka triggers), the middleware logs the exception but cannot return an HTTP response:

```csharp
catch (NostifyValidationException ex)
{
    if (!IsHttpTrigger(context))
    {
        _logger.LogWarning(ex, "Validation failed in non-HTTP function");
        throw; // Re-throw for function host to handle
    }
    // Handle HTTP response...
}
```

## Related Types

- [NostifyValidationException](NostifyValidationException.spec.md) - Exception class
- [NostifyValidation](NostifyValidation.spec.md) - Validation utilities
- [RequiredForAttribute](RequiredForAttribute.spec.md) - Validation attribute
