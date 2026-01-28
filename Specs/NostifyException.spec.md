# NostifyException Class Specification

## Overview

`NostifyException` is the base exception class for general errors in the nostify framework. It provides a simple wrapper around `System.Exception` for application-level errors.

## Class Definition

```csharp
public class NostifyException : Exception
```

## Constructor

```csharp
public NostifyException(string message) : base(message)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `message` | `string` | Human-readable error message |

## Properties

Inherits all properties from `System.Exception`:

| Property | Type | Description |
|----------|------|-------------|
| `Message` | `string` | Error message |
| `InnerException` | `Exception?` | Inner exception (if any) |
| `StackTrace` | `string?` | Stack trace |
| `Source` | `string?` | Source of the exception |

## Usage

### Throwing Exceptions

```csharp
// Invalid configuration
if (string.IsNullOrEmpty(connectionString))
{
    throw new NostifyException("Cosmos DB connection string is required");
}

// Invalid operation
if (aggregate.isDeleted)
{
    throw new NostifyException($"Cannot update deleted aggregate: {aggregate.id}");
}

// Invalid state
if (saga.status != SagaStatus.Pending)
{
    throw new NostifyException($"Saga already started: {saga.id}");
}
```

### Catching Exceptions

```csharp
try
{
    await nostify.PublishEventAsync(@event);
}
catch (NostifyException ex)
{
    _logger.LogError(ex, "Nostify operation failed: {Message}", ex.Message);
    throw;
}
```

### In Azure Functions

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
    catch (NostifyException ex)
    {
        return new BadRequestObjectResult(new { error = ex.Message });
    }
}
```

## When to Use

| Use `NostifyException` For | Use `NostifyValidationException` For |
|---------------------------|-------------------------------------|
| Configuration errors | Input validation failures |
| Invalid operations | Missing required fields |
| State violations | Data format errors |
| Missing resources | Business rule violations |

## Best Practices

1. **Clear Messages** - Provide descriptive error messages
2. **Include Context** - Add relevant IDs in messages
3. **Don't Expose Internals** - Avoid leaking implementation details
4. **Log Before Throwing** - Log detailed info before throwing simplified message

## Related Types

- [NostifyValidationException](NostifyValidationException.spec.md) - For validation errors
- [NostifyValidationExceptionMiddleware](NostifyValidationExceptionMiddleware.spec.md) - Exception middleware
