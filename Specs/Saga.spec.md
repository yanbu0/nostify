# Saga Class Specification

## Overview

`Saga` orchestrates long-running distributed transactions as a series of compensatable steps. It implements the Saga pattern for maintaining data consistency across microservices without distributed transactions.

## Class Definition

```csharp
public class Saga : ISaga
```

## Constructors

### Full Constructor

```csharp
public Saga(string name, List<SagaStep> steps)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | Human-readable saga name |
| `steps` | `List<SagaStep>` | Ordered list of saga steps |

### Name-Only Constructor

```csharp
public Saga(string name)
```

Creates a saga with an empty step list. Steps can be added via `AddStep()`.

### Parameterless Constructor

```csharp
public Saga()
```

For JSON deserialization from Cosmos DB.

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

## SagaStatus Enum

```csharp
public enum SagaStatus
{
    Pending,              // Not started
    InProgress,           // Executing steps
    CompletedSuccessfully,// All steps completed
    RollingBack,          // Compensation in progress
    RolledBack            // Compensation completed
}
```

## Usage Examples

### Creating a Saga

```csharp
// Create saga for order processing
var saga = new Saga("ProcessOrder");

// Add steps with compensation
saga.AddStep(
    new Event(NostifyCommand.Create("Reservation"), orderId, 
        new { OrderId = orderId, Items = items }, userId),
    new Event(NostifyCommand.Delete("Reservation"), orderId, 
        new { OrderId = orderId }, userId)
);

saga.AddStep(
    new Event(NostifyCommand.Create("Payment"), orderId, 
        new { OrderId = orderId, Amount = total }, userId),
    new Event(NostifyCommand.Create("Refund"), orderId, 
        new { OrderId = orderId, Amount = total }, userId)
);

saga.AddStep(
    new Event(NostifyCommand.Update("Order"), orderId, 
        new { Status = "Confirmed" }, userId)
);

// Save and start
await nostify.UpsertSagaAsync(saga);
await saga.Start(nostify);
```

### Handling Step Completion

```csharp
[Function("HandleReservationComplete")]
public async Task HandleReservationComplete(
    [KafkaTrigger("KafkaBroker", "ReservationCreated", ConsumerGroup = "saga")] 
    string kafkaEvent)
{
    var trigger = new NostifyKafkaTriggerEvent(kafkaEvent);
    var @event = trigger.Event;
    
    // Get saga ID from event payload
    var sagaId = @event.GetPayload<dynamic>().sagaId;
    var saga = await _nostify.GetSagaAsync(Guid.Parse(sagaId));
    
    // Mark step complete and trigger next
    await saga.StepCompleted(@event.aggregateRootId);
    await _nostify.UpsertSagaAsync(saga);
}
```

### Handling Failures

```csharp
[Function("HandlePaymentFailed")]
public async Task HandlePaymentFailed(
    [KafkaTrigger("KafkaBroker", "PaymentFailed", ConsumerGroup = "saga")] 
    string kafkaEvent)
{
    var trigger = new NostifyKafkaTriggerEvent(kafkaEvent);
    var @event = trigger.Event;
    
    var sagaId = @event.GetPayload<dynamic>().sagaId;
    var saga = await _nostify.GetSagaAsync(Guid.Parse(sagaId));
    
    // Initiate rollback
    await saga.Rollback(_nostify, "Payment failed");
    await _nostify.UpsertSagaAsync(saga);
}
```

### Monitoring Saga Progress

```csharp
public async Task<SagaStatus> CheckSagaStatus(Guid sagaId)
{
    var saga = await _nostify.GetSagaAsync(sagaId);
    
    Console.WriteLine($"Saga: {saga.name}");
    Console.WriteLine($"Status: {saga.status}");
    Console.WriteLine($"Current Step: {saga.GetCurrentStepIndex() + 1} of {saga.steps.Count}");
    
    foreach (var step in saga.steps)
    {
        Console.WriteLine($"  Step {step.order}: {step.status}");
    }
    
    return saga.status;
}
```

## State Machine

```
        ┌─────────┐
        │ Pending │
        └────┬────┘
             │ Start()
             ▼
      ┌────────────┐
      │ InProgress │◄────────────┐
      └─────┬──────┘             │
            │                    │ StepCompleted()
            ▼                    │ (more steps)
    ┌───────┴───────┐            │
    │  StepComplete │────────────┘
    └───────┬───────┘
            │ StepCompleted() (no more steps)
            ▼
┌─────────────────────┐
│ CompletedSuccessfully│
└─────────────────────┘

        │ Rollback()
        ▼
   ┌─────────────┐
   │ RollingBack │◄────────────┐
   └──────┬──────┘             │
          │                    │ RollbackStepCompleted()
          ▼                    │ (more steps)
  ┌───────┴───────┐            │
  │RollbackComplete│───────────┘
  └───────┬───────┘
          │ RollbackStepCompleted() (no more steps)
          ▼
    ┌────────────┐
    │ RolledBack │
    └────────────┘
```

## Cosmos DB Storage

Sagas are stored in a dedicated container with this structure:

```json
{
    "id": "saga-guid",
    "name": "ProcessOrder",
    "status": "InProgress",
    "createdAt": "2024-01-15T10:00:00Z",
    "startedAt": "2024-01-15T10:00:01Z",
    "steps": [
        {
            "order": 1,
            "status": "CompletedSuccessfully",
            "stepEvent": { ... },
            "rollbackEvent": { ... }
        },
        {
            "order": 2,
            "status": "Triggered",
            "stepEvent": { ... }
        }
    ]
}
```

## Best Practices

1. **Define Compensations** - Always provide rollback events for steps that can fail
2. **Idempotent Steps** - Make steps idempotent for retry safety
3. **Track Saga ID** - Include saga ID in event payloads for correlation
4. **Monitor Progress** - Log state transitions for observability
5. **Timeout Handling** - Implement saga timeout mechanisms

## Related Types

- [ISaga](ISaga.spec.md) - Saga interface
- [SagaStep](SagaStep.spec.md) - Individual saga step
- [ISagaStep](ISagaStep.spec.md) - Step interface
- [Event](Event.spec.md) - Events used in steps
