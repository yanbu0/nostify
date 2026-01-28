# ISaga Interface Specification

## Overview

`ISaga` defines the contract for saga orchestration in the nostify framework. Sagas coordinate long-running distributed transactions across multiple services using compensating transactions for failure handling.

## Interface Definition

```csharp
public interface ISaga
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `id` | `Guid` | Unique saga identifier |
| `name` | `string` | Human-readable saga name |
| `status` | `SagaStatus` | Current saga state |
| `createdAt` | `DateTime` | When the saga was created |
| `startedAt` | `DateTime?` | When execution began |
| `completedAt` | `DateTime?` | When execution completed |
| `rollingBackAt` | `DateTime?` | When rollback began |
| `rolledBackAt` | `DateTime?` | When rollback completed |
| `error` | `string?` | Execution error message |
| `rollbackError` | `string?` | Rollback error message |
| `steps` | `List<SagaStep>` | Ordered list of saga steps |

## Methods

### Step Management

| Method | Signature | Description |
|--------|-----------|-------------|
| `AddStep` | `void AddStep(IEvent stepEvent, IEvent? rollbackEvent = null)` | Adds a step with optional compensation |

### Step Navigation

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetCurrentStepIndex` | `int GetCurrentStepIndex()` | Returns index of currently executing step (-1 if none) |
| `GetCurrentStep` | `ISagaStep? GetCurrentStep()` | Returns currently executing step |
| `GetNextStepIndex` | `int GetNextStepIndex()` | Returns index of next step to execute |
| `GetNextStep` | `ISagaStep? GetNextStep()` | Returns next step to execute |
| `GetLastCompletedStepIndex` | `int GetLastCompletedStepIndex()` | Returns index of last completed step |
| `GetLastCompletedStep` | `ISagaStep? GetLastCompletedStep()` | Returns last completed step |

### Execution Control

| Method | Signature | Description |
|--------|-----------|-------------|
| `Start` | `Task Start(INostify nostify)` | Initiates saga execution |
| `StepCompleted` | `Task StepCompleted(Guid aggregateRootId, object? successData = null)` | Handles step completion |
| `Rollback` | `Task Rollback(INostify nostify, string? errorMessage = null)` | Initiates rollback |
| `RollbackStepCompleted` | `Task RollbackStepCompleted(Guid aggregateRootId, object? rollbackData = null)` | Handles rollback completion |

## Method Details

### AddStep

```csharp
void AddStep(IEvent stepEvent, IEvent? rollbackEvent = null)
```

Adds a new step to the saga. Steps are automatically ordered.

| Parameter | Type | Description |
|-----------|------|-------------|
| `stepEvent` | `IEvent` | The event to publish for this step |
| `rollbackEvent` | `IEvent?` | The compensation event (optional) |

### Start

```csharp
Task Start(INostify nostify)
```

Begins saga execution by:
1. Setting `status` to `InProgress`
2. Setting `startedAt` timestamp
3. Triggering the first step

### StepCompleted

```csharp
Task StepCompleted(Guid aggregateRootId, object? successData = null)
```

Called when a step completes successfully:
1. Marks the current step as completed
2. Stores success data for downstream steps
3. Triggers the next step (if any)
4. Marks saga complete if no more steps

### Rollback

```csharp
Task Rollback(INostify nostify, string? errorMessage = null)
```

Initiates compensating transactions:
1. Sets `status` to `RollingBack`
2. Records the error message
3. Triggers rollback events in reverse order

### RollbackStepCompleted

```csharp
Task RollbackStepCompleted(Guid aggregateRootId, object? rollbackData = null)
```

Called when a rollback step completes:
1. Marks the step's rollback as complete
2. Triggers the previous step's rollback (if any)
3. Marks saga as `RolledBack` when done

## Implementation

The primary implementation is the [Saga](Saga.spec.md) class.

## Usage Pattern

```csharp
public interface IOrderSagaService
{
    Task<ISaga> CreateOrderSaga(Guid orderId, OrderDetails details);
    Task HandleStepComplete(IEvent @event);
    Task HandleStepFailed(IEvent @event);
}

public class OrderSagaService : IOrderSagaService
{
    private readonly INostify _nostify;
    
    public async Task<ISaga> CreateOrderSaga(Guid orderId, OrderDetails details)
    {
        var saga = new Saga("ProcessOrder");
        
        saga.AddStep(
            new Event(NostifyCommand.Create("Reservation"), orderId, 
                new { Items = details.Items, SagaId = saga.id }, details.UserId),
            new Event(NostifyCommand.Delete("Reservation"), orderId, 
                new { SagaId = saga.id }, details.UserId)
        );
        
        saga.AddStep(
            new Event(NostifyCommand.Create("Payment"), orderId, 
                new { Amount = details.Total, SagaId = saga.id }, details.UserId),
            new Event(NostifyCommand.Create("Refund"), orderId, 
                new { Amount = details.Total, SagaId = saga.id }, details.UserId)
        );
        
        await _nostify.UpsertSagaAsync((Saga)saga);
        await saga.Start(_nostify);
        await _nostify.UpsertSagaAsync((Saga)saga);
        
        return saga;
    }
}
```

## SagaStatus Reference

```csharp
public enum SagaStatus
{
    Pending,               // Not started
    InProgress,            // Executing steps
    CompletedSuccessfully, // All steps completed
    RollingBack,           // Compensation in progress
    RolledBack             // Compensation completed
}
```

## Best Practices

1. **Define Compensations** - Always provide rollback events
2. **Saga ID Correlation** - Include saga ID in all events
3. **Idempotent Operations** - Make steps idempotent
4. **Monitor Status** - Track saga progress
5. **Handle Timeouts** - Implement saga expiration

## Related Types

- [Saga](Saga.spec.md) - Primary implementation
- [ISagaStep](ISagaStep.spec.md) - Step interface
- [SagaStep](SagaStep.spec.md) - Step implementation
- [Event](Event.spec.md) - Events in steps
