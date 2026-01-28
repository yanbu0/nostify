# NostifyKafkaTriggerEvent Class Specification

## Overview

`NostifyKafkaTriggerEvent` is a wrapper class that represents an event received from a Kafka trigger in Azure Functions. It provides access to the deserialized event and the raw Kafka event metadata.

## Class Definition

```csharp
public class NostifyKafkaTriggerEvent
```

## Constructor

```csharp
public NostifyKafkaTriggerEvent(string kafkaEventJson)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `kafkaEventJson` | `string` | JSON string from Kafka trigger containing the event |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Event` | `Event` | The deserialized nostify event |
| `KafkaEvent` | `KafkaEventData<string>` | Raw Kafka event metadata |
| `Key` | `string` | Message key (aggregate root ID) |
| `Offset` | `long` | Kafka partition offset |
| `Partition` | `int` | Kafka partition number |
| `Topic` | `string` | Kafka topic name (command name) |
| `Timestamp` | `DateTime` | Kafka message timestamp |

## Usage Examples

### Azure Functions Kafka Trigger

```csharp
[Function("HandleCreateOrder")]
public async Task HandleCreateOrder(
    [KafkaTrigger(
        "KafkaBroker",
        "CreateOrder",
        ConsumerGroup = "order-service")] string kafkaEventJson,
    FunctionContext context)
{
    var triggerEvent = new NostifyKafkaTriggerEvent(kafkaEventJson);
    
    // Access the nostify event
    var @event = triggerEvent.Event;
    var orderId = @event.aggregateRootId;
    var payload = @event.GetPayload<CreateOrderPayload>();
    
    // Access Kafka metadata
    var offset = triggerEvent.Offset;
    var partition = triggerEvent.Partition;
    
    _logger.LogInformation(
        "Processing order {OrderId} from partition {Partition} offset {Offset}",
        orderId, partition, offset
    );
    
    // Handle the event
    await ProcessCreateOrder(@event);
}
```

### Event Handler Pattern

```csharp
public class OrderEventHandlers
{
    private readonly INostify _nostify;
    
    [Function("HandleOrderEvents")]
    public async Task HandleOrderEvents(
        [KafkaTrigger("KafkaBroker", "CreateOrder|UpdateOrder|DeleteOrder", 
            ConsumerGroup = "order-handlers")] string[] kafkaEvents)
    {
        foreach (var kafkaJson in kafkaEvents)
        {
            var trigger = new NostifyKafkaTriggerEvent(kafkaJson);
            await RouteEvent(trigger.Event);
        }
    }
    
    private async Task RouteEvent(Event @event)
    {
        switch (@event.command.name)
        {
            case "CreateOrder":
                await HandleCreate(@event);
                break;
            case "UpdateOrder":
                await HandleUpdate(@event);
                break;
            case "DeleteOrder":
                await HandleDelete(@event);
                break;
        }
    }
}
```

### Projection Updates

```csharp
[Function("UpdateOrderProjection")]
public async Task UpdateProjection(
    [KafkaTrigger("KafkaBroker", "CreateOrder", ConsumerGroup = "projections")] 
    string kafkaEventJson)
{
    var trigger = new NostifyKafkaTriggerEvent(kafkaEventJson);
    var @event = trigger.Event;
    
    // Get or create projection
    var projection = await _nostify.GetProjectionAsync<OrderSummary>(@event.aggregateRootId)
        ?? new OrderSummary(@event.aggregateRootId);
    
    // Apply event
    projection.Apply(@event);
    
    // Save projection
    await _nostify.UpsertProjectionAsync(projection);
}
```

### With Saga Orchestration

```csharp
[Function("HandleSagaStep")]
public async Task HandleSagaStep(
    [KafkaTrigger("KafkaBroker", "OrderStepComplete", ConsumerGroup = "saga-handler")] 
    string kafkaEventJson)
{
    var trigger = new NostifyKafkaTriggerEvent(kafkaEventJson);
    var @event = trigger.Event;
    
    // Extract saga ID from event
    var sagaId = @event.GetPayload<dynamic>().sagaId;
    
    // Get and update saga
    var saga = await _nostify.GetSagaAsync(Guid.Parse(sagaId));
    await saga.StepCompleted(@event.aggregateRootId);
    await _nostify.UpsertSagaAsync(saga);
}
```

## Kafka Event Structure

The expected JSON structure from Kafka:

```json
{
    "Key": "550e8400-e29b-41d4-a716-446655440000",
    "Value": "{\"id\":\"event-id\",\"timestamp\":\"2024-01-15T10:30:00Z\",\"command\":{...},\"payload\":{...}}",
    "Offset": 12345,
    "Partition": 0,
    "Topic": "CreateOrder",
    "Timestamp": "2024-01-15T10:30:00.123Z"
}
```

## Internal Behavior

The constructor:

1. Deserializes the Kafka envelope JSON
2. Extracts the `Value` field containing the event JSON
3. Deserializes the event JSON to an `Event` object
4. Stores both the raw Kafka data and the parsed event

## Error Handling

| Exception | Condition |
|-----------|-----------|
| `JsonException` | Invalid JSON format |
| `NullReferenceException` | Missing required properties |

## Best Practices

1. **Use for Kafka Triggers** - Only use with Azure Functions Kafka triggers
2. **Access Event Directly** - Use `.Event` for nostify operations
3. **Log Metadata** - Include partition/offset in logs for debugging
4. **Handle Failures** - Implement retry logic for transient failures

## Related Types

- [Event](Event.spec.md) - The deserialized event
- [IEvent](IEvent.spec.md) - Event interface
- [NostifyCommand](NostifyCommand.spec.md) - Command routing
