# RetryOptions Specification

## Overview

`RetryOptions` configures retry behavior for operations that may transiently fail, such as Cosmos DB queries or event processing. Provides settings for maximum retries, delay, not-found retry behavior, exponential backoff, and optional logging.

## Class Definition

```csharp
public class RetryOptions
```

## Constructors

### Default Constructor

```csharp
public RetryOptions()
```

Creates an instance with default values (3 retries, 1s delay, no retry on not-found, 2x exponential backoff, no logging).

### Parameterized Constructor

```csharp
public RetryOptions(int maxRetries, TimeSpan delay, bool retryWhenNotFound, 
    double? delayMultiplier = 2.0, bool logRetries = false, ILogger? logger = null)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `maxRetries` | `int` | Required | Max retry attempts before failure |
| `delay` | `TimeSpan` | Required | Base delay between retries |
| `retryWhenNotFound` | `bool` | Required | Whether to retry on 404 NotFound |
| `delayMultiplier` | `double?` | `2.0` | Multiplier for exponential backoff |
| `logRetries` | `bool` | `false` | Enable retry attempt logging |
| `logger` | `ILogger?` | `null` | Structured logger instance |

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Maximum number of retry attempts |
| `Delay` | `TimeSpan` | `1 second` | Base delay between retries |
| `RetryWhenNotFound` | `bool` | `false` | Retry on 404 NotFound responses |
| `DelayMultiplier` | `double?` | `2.0` | Exponential backoff multiplier (null = constant delay) |
| `LogRetries` | `bool` | `false` | Whether to log retry attempts |
| `Logger` | `ILogger?` | `null` | Optional ILogger; falls back to Console.Error |

## Methods

### GetDelayForAttempt

```csharp
public TimeSpan GetDelayForAttempt(int attempt, double? delay = null, double? delayMultiplier = null)
```

Calculates the delay for a given retry attempt using exponential backoff when a delay multiplier is set.

| Parameter | Type | Description |
|-----------|------|-------------|
| `attempt` | `int` | Zero-based attempt number (0 = first retry) |
| `delay` | `double?` | Optional delay (in milliseconds) overriding the instance `Delay`. Defaults to `null` (use `Delay.TotalMilliseconds`). |
| `delayMultiplier` | `double?` | Optional multiplier overriding the instance `DelayMultiplier`. Defaults to `null` (use `DelayMultiplier`). |

**Returns:** The calculated delay `TimeSpan`.

**Behavior:**
- When the effective multiplier (override or instance) is null, or `attempt` is 0: returns the effective delay (constant)
- Otherwise: `effectiveDelayMs * Math.Pow(effectiveMultiplier, attempt)` milliseconds

**Examples:**
- Delay=1s, Multiplier=2.0: 1s, 2s, 4s, 8s
- Delay=100ms, Multiplier=1.5: 100ms, 150ms, 225ms
- Override delay=100ms, override multiplier=3 (instance values ignored): 100ms, 300ms, 900ms, 2700ms

The override parameters allow callers (e.g., `RetryableContainer`'s 429 handler) to apply exponential backoff anchored at a server-supplied delay (such as a Cosmos `RetryAfter`) without mutating the shared `RetryOptions` instance.

### LogRetry

```csharp
public void LogRetry(string message)
```

Logs a retry message. Only logs if `LogRetries` is `true`.

**Behavior:**
- If `Logger` is not null: calls `Logger.LogWarning(message)`
- If `Logger` is null: writes to `Console.Error` with `[nostify:retry]` prefix

## Usage Examples

### Default Options

```csharp
var options = new RetryOptions(); // 3 retries, 1s delay, no retry on not-found, 2x backoff
```

### Custom Options

```csharp
var options = new RetryOptions(
    maxRetries: 5, 
    delay: TimeSpan.FromSeconds(2), 
    retryWhenNotFound: true
);
```

### Exponential Backoff with Logging

```csharp
var options = new RetryOptions(
    maxRetries: 3, 
    delay: TimeSpan.FromSeconds(1), 
    retryWhenNotFound: true,
    delayMultiplier: 2.0,   // 1s, 2s, 4s
    logRetries: true,
    logger: myLogger
);
```

## Key Relationships

- **RetryableContainer**: Uses RetryOptions to govern all retry behavior
- **DefaultEventHandlers**: Accepts optional RetryOptions parameter
- **ILogger (Microsoft.Extensions.Logging)**: Optional dependency for structured logging

## Source Files

- Implementation: `src/ErrorHandling/RetryOptions.cs`
- Tests: `nostify.Tests/RetryOptions.Tests.cs`
