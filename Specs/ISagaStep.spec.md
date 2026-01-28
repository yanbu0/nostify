# ISagaStep Interface Specification

## Overview

`ISagaStep` defines the contract for individual steps within a saga. Each step contains a forward event, optional rollback event, and tracking metadata.

## Interface Definition

```csharp
public interface ISagaStep
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `order` | `int` | Execution order (1-based) |
| `stepEvent` | `IEvent` | Event to publish for this step |
| `rollbackEvent` | `IEvent?` | Event to publish on rollback (optional) |
| `status` | `SagaStepStatus` | Current step status |
| `successData` | `object?` | Data for subsequent steps |
| `rollbackData` | `object?` | Data for rollback of previous steps |
| `triggeredAt` | `DateTime?` | When step started |
| `completedAt` | `DateTime?` | When step completed |
| `rollingBackAt` | `DateTime?` | When rollback started |
| `rolledBackAt` | `DateTime?` | When rollback completed |
| `aggregateRootId` | `Guid` | Derived from stepEvent |

## Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Trigger` | `Task Trigger(INostify nostify)` | Publishes step event |
| `TriggerRollback` | `Task TriggerRollback(INostify nostify)` | Publishes rollback event |
| `Complete` | `void Complete(object? successData = null)` | Marks step completed |
| `CompleteRollback` | `void CompleteRollback(object? rollbackData = null)` | Marks rollback completed |

## Method Details

### Trigger

```csharp
Task Trigger(INostify nostify)
```

Publishes the step event and updates status:
- Sets `triggeredAt` to current UTC time
- Sets `status` to `Triggered`
- Publishes `stepEvent` via nostify

### TriggerRollback

```csharp
Task TriggerRollback(INostify nostify)
```

Initiates compensation for this step:
- Sets `rollingBackAt` to current UTC time
- Sets `status` to `RollingBack`
- If `rollbackEvent` exists, publishes it
- If no rollback event, immediately completes rollback

### Complete

```csharp
void Complete(object? successData = null)
```

Marks the step as successfully completed:
- Sets `completedAt` to current UTC time
- Sets `status` to `CompletedSuccessfully`
- Stores `successData` for use by subsequent steps

### CompleteRollback

```csharp
void CompleteRollback(object? rollbackData = null)
```

Marks the rollback as completed:
- Sets `rolledBackAt` to current UTC time
- Sets `status` to `RolledBack`
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

## Property Details

### order

- **Type**: `int`
- **Description**: The execution sequence (1-based, ascending)
- **Usage**: Steps execute in order; rollbacks execute in reverse

### stepEvent

- **Type**: `IEvent`
- **Description**: The forward event that performs this step's action
- **Usage**: Published when `Trigger()` is called

### rollbackEvent

- **Type**: `IEvent?`
- **Description**: The compensation event (optional)
- **Usage**: Published during rollback; null means no compensation needed

### aggregateRootId

- **Type**: `Guid`
- **Description**: Derived from `stepEvent.aggregateRootId`
- **Usage**: Identifies which aggregate this step affects

## Implementation

The primary implementation is the [SagaStep](SagaStep.spec.md) class.

## Usage Example

```csharp
public async Task HandleStepEvent(ISagaStep step, IEvent completionEvent)
{
    // Verify this event matches the step
    if (completionEvent.aggregateRootId != step.aggregateRootId)
    {
        throw new InvalidOperationException("Event doesn't match step");
    }
    
    // Check current status
    if (step.status != SagaStepStatus.Triggered)
    {
        throw new InvalidOperationException($"Step not triggered: {step.status}");
    }
    
    // Complete with any relevant data
    step.Complete(new { 
        ProcessedAt = DateTime.UtcNow,
        ResultId = completionEvent.GetPayload<dynamic>().ResultId
    });
}
```

## State Transitions

| Current State | Valid Transitions | Method |
|---------------|-------------------|--------|
| WaitingForTrigger | Triggered | `Trigger()` |
| Triggered | CompletedSuccessfully | `Complete()` |
| Triggered | RollingBack | `TriggerRollback()` |
| CompletedSuccessfully | RollingBack | `TriggerRollback()` |
| RollingBack | RolledBack | `CompleteRollback()` |

## Best Practices

1. **Validate State** - Check status before state transitions
2. **Include Context** - Use successData to pass IDs between steps
3. **Idempotent Events** - Design events for retry safety
4. **Saga Correlation** - Include saga ID in step events

## Related Types

- [SagaStep](SagaStep.spec.md) - Primary implementation
- [ISaga](ISaga.spec.md) - Parent saga interface
- [Saga](Saga.spec.md) - Saga implementation
- [IEvent](IEvent.spec.md) - Event interface
