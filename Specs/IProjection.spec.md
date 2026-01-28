# IProjection Interface Specification

## Overview

`IProjection` defines the contract for read model projections that are built from event streams. Projections denormalize data for query optimization and can combine data from multiple aggregates.

## Interface Definition

```csharp
public interface IProjection
```

## Properties

| Property | Type | Modifier | Description |
|----------|------|----------|-------------|
| `initialized` | `bool` | Instance | Whether projection has been initialized with external data |
| `containerName` | `string` | `static abstract` | Cosmos DB container name (unique per projection per microservice) |

## Implementation Requirements

Projections must:

1. Extend `NostifyObject`
2. Implement `IProjection`
3. Implement `IApplyable` (for event application)
4. Define static abstract `containerName`

## Usage Examples

### Basic Implementation

```csharp
public class OrderSummary : NostifyObject, IProjection, IApplyable
{
    public static string containerName => "order-summaries";
    
    public bool initialized { get; set; }
    
    public string CustomerName { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime LastOrderDate { get; set; }
    
    public OrderSummary() : base() { }
    public OrderSummary(Guid id) : base(id) { }
    
    public void Apply(Event @event)
    {
        @event.ApplyTo(this);
    }
}
```

### With External Data

```csharp
public class OrderWithCustomer : NostifyObject, IProjection, IApplyable
{
    public static string containerName => "orders-with-customers";
    
    public bool initialized { get; set; }
    
    // From Order aggregate
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    
    // From external Customer service
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    
    public void Apply(Event @event)
    {
        @event.ApplyTo(this);
    }
}
```

### With Tenant Support

```csharp
public class TenantOrderDashboard : NostifyObject, IProjection, IApplyable, ITenantFilterable
{
    public static string containerName => "tenant-dashboards";
    
    public bool initialized { get; set; }
    public Guid tenantId { get; set; }
    
    public int TotalOrders { get; set; }
    public decimal Revenue { get; set; }
    public int PendingOrders { get; set; }
    
    public void Apply(Event @event)
    {
        @event.ApplyTo(this);
    }
}
```

## Initialization Pattern

The `initialized` property tracks whether external data has been fetched:

```csharp
public async Task InitializeProjection(OrderWithCustomer projection)
{
    if (projection.initialized)
    {
        return; // Already initialized
    }
    
    // Fetch external data
    var customer = await _customerService.GetCustomerAsync(projection.CustomerId);
    projection.CustomerName = customer.Name;
    projection.CustomerEmail = customer.Email;
    
    // Mark as initialized
    projection.initialized = true;
    
    await _nostify.UpsertProjectionAsync(projection);
}
```

## Using IProjectionInitializer

```csharp
// Initialize single projection
await projectionInitializer.InitializeAsync<OrderWithCustomer>(orderId);

// Initialize multiple
await projectionInitializer.InitializeAsync<OrderWithCustomer>(orderIds);

// Rebuild all projections
await projectionInitializer.RebuildAsync<OrderWithCustomer>();

// Initialize only uninitialized
await projectionInitializer.InitializeUninitializedAsync<OrderWithCustomer>();
```

## Container Organization

Each projection type gets its own container:

| Projection Type | Container Name |
|-----------------|----------------|
| OrderSummary | `order-summaries` |
| OrderWithCustomer | `orders-with-customers` |
| InventoryView | `inventory-views` |

## Static Abstract Members

C# 11 static abstract enables type-level configuration:

```csharp
public static string containerName => "my-projections";
```

Used in generic methods:

```csharp
public async Task<P?> GetProjectionAsync<P>(Guid id) where P : IProjection
{
    var container = Repository.GetContainer(P.containerName);
    // ...
}
```

## Best Practices

1. **Unique Container Names** - Each projection type needs its own container
2. **Set initialized = false** - Default to uninitialized
3. **Initialize Before Use** - Check `initialized` before serving data
4. **Combine with IApplyable** - Enable event-driven updates
5. **Use ITenantFilterable** - For multi-tenant projections

## Projection vs Aggregate

| Feature | IAggregate | IProjection |
|---------|------------|-------------|
| Purpose | Write model | Read model |
| Container | Current state | Query optimized |
| Soft delete | `isDeleted` | N/A |
| External data | No | Yes (`initialized`) |
| One per aggregate | Yes | Multiple allowed |

## Related Types

- [NostifyObject](NostifyObject.spec.md) - Base class
- [IApplyable](IApplyable.spec.md) - Event application
- [IAggregate](IAggregate.spec.md) - Aggregate interface
- [IProjectionInitializer](IProjectionInitializer.spec.md) - Initialization
- [ExternalDataEventFactory](ExternalDataEventFactory.spec.md) - External data handling
