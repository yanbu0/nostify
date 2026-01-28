# IAggregate Interface Specification

## Overview

`IAggregate` defines the contract for aggregate root entities in the nostify event-sourcing framework. Aggregates represent domain entities that handle commands and maintain state through event sourcing.

## Interface Definition

```csharp
public interface IAggregate : IUniquelyIdentifiable
```

## Properties

| Property | Type | Modifier | Description |
|----------|------|----------|-------------|
| `isDeleted` | `bool` | Instance | Soft delete flag |
| `aggregateType` | `string` | `static abstract` | Name/type identifier for the aggregate |
| `currentStateContainerName` | `string` | `static abstract` | Cosmos DB container name for current state |

## Inherited from IUniquelyIdentifiable

| Property | Type | Description |
|----------|------|-------------|
| `id` | `Guid` | Unique identifier |

## Implementation Requirements

Aggregates must:

1. Extend `NostifyObject`
2. Implement `IAggregate`
3. Implement `IApplyable` (for event application)
4. Define static abstract properties

## Usage Examples

### Basic Implementation

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    // Static abstract properties (C# 11+)
    public static string aggregateType => "Order";
    public static string currentStateContainerName => "orders-current";
    
    // Instance property from IAggregate
    public bool isDeleted { get; set; }
    
    // Domain properties
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Constructors
    public Order() : base() { }
    public Order(Guid id) : base(id) { }
    
    // IApplyable implementation
    public void Apply(Event @event)
    {
        @event.ApplyTo(this);
    }
}
```

### With Tenant Support

```csharp
public class TenantOrder : NostifyObject, IAggregate, IApplyable, ITenantFilterable
{
    public static string aggregateType => "TenantOrder";
    public static string currentStateContainerName => "tenant-orders-current";
    
    public bool isDeleted { get; set; }
    public Guid tenantId { get; set; }
    
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    
    public void Apply(Event @event)
    {
        @event.ApplyTo(this);
    }
}
```

### With Validation

```csharp
public class Order : NostifyObject, IAggregate, IApplyable
{
    public static string aggregateType => "Order";
    public static string currentStateContainerName => "orders-current";
    
    public bool isDeleted { get; set; }
    
    [RequiredFor("CreateOrder")]
    public Guid CustomerId { get; set; }
    
    [RequiredFor("CreateOrder", "UpdateOrder")]
    [Range(0.01, double.MaxValue)]
    public decimal Total { get; set; }
    
    [RequiredFor("ShipOrder")]
    public string ShippingAddress { get; set; }
    
    public void Apply(Event @event)
    {
        @event.ApplyTo(this);
    }
}
```

## Static Abstract Members

C# 11 static abstract members enable type-level configuration:

```csharp
// Used for partition key and event routing
public static string aggregateType => "Order";

// Used for current state container access
public static string currentStateContainerName => "orders-current";
```

These are accessed via generic constraints:

```csharp
public async Task<A> GetCurrentStateAsync<A>(Guid id) where A : IAggregate
{
    var containerName = A.currentStateContainerName;
    var container = Repository.GetContainer(containerName);
    // ...
}
```

## Container Organization

| Container | Purpose | Partition Key |
|-----------|---------|---------------|
| Event Store | All events | `/partitionKey` (aggregate type) |
| Current State | Latest aggregate state | `/tenantId` or `/id` |

## Soft Delete Pattern

```csharp
// Mark as deleted (don't actually delete)
var deleteEvent = new Event(
    NostifyCommand.Delete("Order"),
    orderId,
    new { isDeleted = true },
    userId
);

await nostify.PublishEventAsync(deleteEvent);
```

## Best Practices

1. **Use Descriptive Names** - `aggregateType` should match class name
2. **Container Naming** - Use `{type}-current` pattern for containers
3. **Implement IApplyable** - Required for event application
4. **Use Soft Deletes** - Set `isDeleted` instead of removing
5. **Add ITenantFilterable** - For multi-tenant applications

## Related Types

- [IUniquelyIdentifiable](IUniquelyIdentifiable.spec.md) - Base interface
- [NostifyObject](NostifyObject.spec.md) - Base class
- [IApplyable](IApplyable.spec.md) - Event application
- [IProjection](IProjection.spec.md) - Projection interface
- [ITenantFilterable](ITenantFilterable.spec.md) - Tenant support
