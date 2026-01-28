# SagaStep Class Specification

## Overview

`SagaStep` represents an individual step in a saga, containing the forward event, optional rollback event, and execution tracking metadata.

## Class Definition

```csharp
public class SagaStep : ISagaStep
```

## Constructors

### Full Constructor

```csharp
public SagaStep(int order, IEvent stepEvent, IEvent? rollbackEvent = null)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `order` | `int` | Required | Execution order (1-based) |
| `stepEvent` | `IEvent` | Required | Event to publish for this step |
| `rollbackEvent` | `IEvent?` | `null` | Event to publish on rollback |

### Parameterless Constructor

```csharp
public SagaStep()
```

For JSON deserialization from Cosmos DB.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `order` | `int` | Execution order (1-based) |
| `stepEvent` | `IEvent` | Event to publish for this step |
| `rollbackEvent` | `IEvent?` | Event to publish on rollback |
| `status` | `SagaStepStatus` | Current step status |
| `successData` | `object?` | Data for subsequent steps |
| `rollbackData` | `object?` | Data for rollback of previous steps |
| `triggeredAt` | `DateTime?` | When step started |
| `completedAt` | `DateTime?` | When step completed |
| `rollingBackAt` | `DateTime?` | When rollback started |
| `rolledBackAt` | `DateTime?` | When rollback completed |
| `aggregateRootId` | `Guid` | Derived from stepEvent |

## Methods

### Trigger

```csharp
public async Task Trigger(INostify nostify)
```

Publishes the step event and sets status to `Triggered`.

| Parameter | Type | Description |
|-----------|------|-------------|
| `nostify` | `INostify` | Nostify instance for event publishing |

**Behavior:**
- Sets `triggeredAt` to current UTC time
- Sets `status` to `SagaStepStatus.Triggered`
- Publishes `stepEvent` via `nostify.PublishEventAsync()`

### TriggerRollback

```csharp
public async Task TriggerRollback(INostify nostify)
```

Publishes the rollback event if defined.

| Parameter | Type | Description |
|-----------|------|-------------|
| `nostify` | `INostify` | Nostify instance for event publishing |

**Behavior:**
- Sets `rollingBackAt` to current UTC time
- Sets `status` to `SagaStepStatus.RollingBack`
- If `rollbackEvent` is defined, publishes it
- If no rollback event, immediately marks as rolled back

### Complete

```csharp
public void Complete(object? successData = null)
```

Marks the step as completed successfully.

| Parameter | Type | Description |
|-----------|------|-------------|
| `successData` | `object?` | Data to pass to subsequent steps |

**Behavior:**
- Sets `completedAt` to current UTC time
- Sets `status` to `SagaStepStatus.CompletedSuccessfully`
- Stores `successData` for saga orchestration

### CompleteRollback

```csharp
public void CompleteRollback(object? rollbackData = null)
```

Marks the rollback as completed.

| Parameter | Type | Description |
|-----------|------|-------------|
| `rollbackData` | `object?` | Data for previous steps' rollback |

**Behavior:**
- Sets `rolledBackAt` to current UTC time
- Sets `status` to `SagaStepStatus.RolledBack`
- Stores `rollbackData` for compensation chain

## SagaStepStatus Enum

```csharp
public enum SagaStepStatus
{
    WaitingForTrigger,      // Not yet executed
    Triggered,              // Event published, awaiting completion
    CompletedSuccessfully,  // Step finished successfully
    RollingBack,            // Rollback event published
    RolledBack              // Rollback completed
}
```

## State Machine

```
┌───────────────────┐
│ WaitingForTrigger │
└─────────┬─────────┘
          │ Trigger()
          ▼
    ┌───────────┐
    │ Triggered │
    └─────┬─────┘
          │
    ┌─────┴─────┐
    │           │
    ▼           ▼
Complete()  TriggerRollback()
    │           │
    ▼           ▼
┌─────────────────────┐    ┌─────────────┐
│CompletedSuccessfully│    │ RollingBack │
└─────────────────────┘    └──────┬──────┘
                                  │ CompleteRollback()
                                  ▼
                           ┌────────────┐
                           │ RolledBack │
                           └────────────┘
```

## Usage Examples

### Creating Steps

```csharp
// Step with rollback
var reservationStep = new SagaStep(
    order: 1,
    stepEvent: new Event(
        NostifyCommand.Create("Reservation"),
        orderId,
        new { OrderId = orderId, Items = items, SagaId = sagaId },
        userId
    ),
    rollbackEvent: new Event(
        NostifyCommand.Delete("Reservation"),
        orderId,
        new { OrderId = orderId, SagaId = sagaId },
        userId
    )
);

// Step without rollback (final step)
var confirmStep = new SagaStep(
    order: 3,
    stepEvent: new Event(
        NostifyCommand.Update("Order"),
        orderId,
        new { Status = "Confirmed", SagaId = sagaId },
        userId
    )
);
```

### Handling Step Events

```csharp
[Function("HandleStepComplete")]
public async Task HandleStepComplete(
    [KafkaTrigger("KafkaBroker", "ReservationCreated")] string kafkaEvent)
{
    var trigger = new NostifyKafkaTriggerEvent(kafkaEvent);
    var @event = trigger.Event;
    
    // Get saga and find matching step
    var sagaId = @event.GetPayload<dynamic>().SagaId;
    var saga = await _nostify.GetSagaAsync(Guid.Parse(sagaId));
    
    // Find step by aggregate root ID
    var step = saga.steps.FirstOrDefault(s => 
        s.aggregateRootId == @event.aggregateRootId &&
        s.status == SagaStepStatus.Triggered);
    
    if (step != null)
    {
        // Pass data to next step
        step.Complete(new { ReservationId = @event.GetPayload<dynamic>().ReservationId });
        await saga.StepCompleted(@event.aggregateRootId);
        await _nostify.UpsertSagaAsync(saga);
    }
}
```

### Accessing Step Data

```csharp
// In a subsequent step handler, access data from previous steps
var previousStep = saga.GetLastCompletedStep();
var reservationId = previousStep?.successData?.ReservationId;

// During rollback, access rollback data
var currentStep = saga.GetCurrentStep();
var rollbackInfo = currentStep?.rollbackData;
```

## JSON Storage Structure

```json
{
    "order": 1,
    "status": "CompletedSuccessfully",
    "stepEvent": {
        "id": "event-guid",
        "command": { "name": "CreateReservation" },
        "aggregateRootId": "aggregate-guid",
        "payload": { ... }
    },
    "rollbackEvent": {
        "id": "rollback-event-guid",
        "command": { "name": "DeleteReservation" },
        "aggregateRootId": "aggregate-guid",
        "payload": { ... }
    },
    "successData": { "ReservationId": "res-123" },
    "triggeredAt": "2024-01-15T10:00:01Z",
    "completedAt": "2024-01-15T10:00:05Z"
}
```

## Best Practices

1. **Include Saga ID** - Always include saga ID in step event payloads
2. **Define Rollbacks** - Provide compensation for non-idempotent operations
3. **Use Success Data** - Pass IDs and context to subsequent steps
4. **Validate State** - Check step status before triggering
5. **Log Transitions** - Track step state changes for debugging

## Related Types

- [ISagaStep](ISagaStep.spec.md) - Step interface
- [Saga](Saga.spec.md) - Parent saga orchestrator
- [ISaga](ISaga.spec.md) - Saga interface
- [Event](Event.spec.md) - Events used in steps
