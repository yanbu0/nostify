# NostifyObject Class Specification

## Overview

`NostifyObject` is the abstract base class for all domain objects in the nostify framework, including aggregates and projections. It provides the common `id` property and implements `IUniquelyIdentifiable`.

## Class Definition

```csharp
public abstract class NostifyObject : IUniquelyIdentifiable
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `id` | `Guid` | Unique identifier for the object (lowercase for Cosmos DB compatibility) |

## Interface Implementation

Implements `IUniquelyIdentifiable`:

```csharp
public interface IUniquelyIdentifiable
{
    Guid id { get; set; }
}
```

## Constructors

### Default Constructor

```csharp
protected NostifyObject()
{
    id = Guid.NewGuid();
}
```

Creates a new object with an auto-generated GUID.

### ID Constructor

```csharp
protected NostifyObject(Guid id)
{
    this.id = id;
}
```

Creates a new object with a specific ID.

## Usage Examples

### Aggregate Implementation

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    public static string aggregateType => "Order";
    public static string currentStateContainerName => "orders-current";
    
    public bool isDeleted { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    
    public Order() : base() { }
    public Order(Guid id) : base(id) { }
    
    public void Apply(Event @event)
    {
        // Apply event properties to this aggregate
        @event.ApplyTo(this);
    }
}
```

### Projection Implementation

```csharp
public class OrderSummary : NostifyObject, IProjection, IApplyable
{
    public static string containerName => "order-summaries";
    
    public bool initialized { get; set; }
    public string CustomerName { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    
    public OrderSummary() : base() { }
    public OrderSummary(Guid id) : base(id) { }
    
    public void Apply(Event @event)
    {
        @event.ApplyTo(this);
    }
}
```

### With Event Sourcing

```csharp
// Rehydrate aggregate from events
public async Task<Order> RehydrateOrder(Guid orderId)
{
    var events = await nostify.GetAllEventsAsync<Order>(orderId);
    
    var order = new Order(orderId);
    foreach (var @event in events.OrderBy(e => e.timestamp))
    {
        order.Apply(@event);
    }
    
    return order;
}
```

## Cosmos DB Integration

The `id` property is:

1. **Document ID** - Used as the document's `id` field in Cosmos DB
2. **Lowercase** - Follows Cosmos DB naming conventions
3. **Guid Type** - Ensures globally unique identifiers

```csharp
// Cosmos DB document structure
{
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "tenantId": "...",
    "customerId": "...",
    "total": 99.99
}
```

## Inheritance Hierarchy

```
NostifyObject (abstract)
    ├── IAggregate implementations
    │   ├── Order
    │   ├── Customer
    │   └── Product
    │
    └── IProjection implementations
        ├── OrderSummary
        ├── CustomerDashboard
        └── InventoryView
```

## Required Interface Combinations

When implementing domain objects:

### For Aggregates

```csharp
public class MyAggregate : NostifyObject, IAggregate, IApplyable
{
    public static string aggregateType => "MyAggregate";
    public static string currentStateContainerName => "my-aggregates-current";
    public bool isDeleted { get; set; }
    
    public void Apply(Event @event) { /* ... */ }
}
```

### For Projections

```csharp
public class MyProjection : NostifyObject, IProjection, IApplyable
{
    public static string containerName => "my-projections";
    public bool initialized { get; set; }
    
    public void Apply(Event @event) { /* ... */ }
}
```

### For Tenant-Scoped Objects

```csharp
public class TenantOrder : NostifyObject, IAggregate, IApplyable, ITenantFilterable
{
    public Guid tenantId { get; set; }
    // ... rest of implementation
}
```

## Best Practices

1. **Always Call Base Constructor** - Use `: base()` or `: base(id)` in derived classes
2. **Implement Required Interfaces** - Combine with `IAggregate` or `IProjection`
3. **Add IApplyable** - Required for event sourcing
4. **Use Lowercase id** - Matches Cosmos DB conventions

## Related Types

- [IUniquelyIdentifiable](IUniquelyIdentifiable.spec.md) - Base interface
- [IAggregate](IAggregate.spec.md) - Aggregate interface
- [IProjection](IProjection.spec.md) - Projection interface
- [IApplyable](IApplyable.spec.md) - Event application interface
- [ITenantFilterable](ITenantFilterable.spec.md) - Multi-tenant support
