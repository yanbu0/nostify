# IApplyable Interface Specification

## Overview

`IApplyable` defines the contract for objects that can apply events to update their state. This is the core interface for event sourcing state management.

## Interface Definition

```csharp
public interface IApplyable
{
    void Apply(Event @event);
}
```

## Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Apply` | `void Apply(Event @event)` | Applies an event's payload to the object |

## Purpose

This interface enables:

1. **Event Sourcing** - Rebuild state by replaying events
2. **State Updates** - Apply property changes from events
3. **Projections** - Update read models from events
4. **Decoupling** - Separate event handling from business logic

## Implementation Patterns

### Basic Implementation

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    public bool isDeleted { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    
    public void Apply(Event @event)
    {
        // ApplyTo uses reflection to set matching properties
        @event.ApplyTo(this);
    }
}
```

### Custom Apply Logic

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    public bool isDeleted { get; set; }
    public decimal Total { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    
    public void Apply(Event @event)
    {
        switch (@event.command.name)
        {
            case "CreateOrder":
                @event.ApplyTo(this);
                break;
                
            case "AddItem":
                var item = @event.GetPayload<OrderItem>();
                Items.Add(item);
                Total += item.Price;
                break;
                
            case "RemoveItem":
                var itemId = @event.GetPayload<dynamic>().ItemId;
                var removed = Items.FirstOrDefault(i => i.id == itemId);
                if (removed != null)
                {
                    Items.Remove(removed);
                    Total -= removed.Price;
                }
                break;
                
            case "DeleteOrder":
                isDeleted = true;
                break;
        }
    }
}
```

### Projection Apply

```csharp
public class OrderSummary : NostifyObject, IProjection, IApplyable
{
    public bool initialized { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalRevenue { get; set; }
    
    public void Apply(Event @event)
    {
        switch (@event.command.name)
        {
            case "CreateOrder":
                OrderCount++;
                var total = @event.GetPayload<dynamic>().Total;
                TotalRevenue += (decimal)total;
                break;
                
            case "DeleteOrder":
                OrderCount--;
                var orderTotal = @event.GetPayload<dynamic>().Total;
                TotalRevenue -= (decimal)orderTotal;
                break;
        }
    }
}
```

## Event ApplyTo Extension

The `Event` class provides `ApplyTo`:

```csharp
public void ApplyTo<T>(T target) where T : IApplyable
{
    // Uses reflection to copy matching properties from payload to target
    var payloadProperties = JObject.FromObject(payload);
    var targetType = typeof(T);
    
    foreach (var prop in payloadProperties.Properties())
    {
        var targetProperty = targetType.GetProperty(prop.Name);
        if (targetProperty != null && targetProperty.CanWrite)
        {
            var value = prop.Value.ToObject(targetProperty.PropertyType);
            targetProperty.SetValue(target, value);
        }
    }
}
```

## Rehydration Pattern

```csharp
public async Task<Order> RehydrateOrder(Guid orderId)
{
    // Get all events for this aggregate
    var events = await nostify.GetAllEventsAsync<Order>(orderId);
    
    // Create new aggregate
    var order = new Order(orderId);
    
    // Apply events in order
    foreach (var @event in events.OrderBy(e => e.timestamp))
    {
        order.Apply(@event);
    }
    
    return order;
}
```

## Point-in-Time Rehydration

```csharp
public async Task<Order> GetOrderAsOf(Guid orderId, DateTime asOfDate)
{
    var events = await nostify.GetAllEventsAsync<Order>(orderId);
    
    var order = new Order(orderId);
    
    // Apply only events up to the specified date
    foreach (var @event in events
        .Where(e => e.timestamp <= asOfDate)
        .OrderBy(e => e.timestamp))
    {
        order.Apply(@event);
    }
    
    return order;
}
```

## Best Practices

1. **Use ApplyTo** - For simple property mapping
2. **Switch on Command** - For complex logic
3. **Idempotent Apply** - Handle duplicate events gracefully
4. **No Side Effects** - Apply should only modify state
5. **Order Matters** - Events must be applied in timestamp order

## Related Types

- [Event](Event.spec.md) - Events to apply
- [IAggregate](IAggregate.spec.md) - Aggregates implement this
- [IProjection](IProjection.spec.md) - Projections implement this
- [NostifyObject](NostifyObject.spec.md) - Base class
