# ITenantFilterable Interface Specification

## Overview

`ITenantFilterable` is a marker interface for entities that support tenant-based filtering in multi-tenant applications.

## Interface Definition

```csharp
public interface ITenantFilterable
{
    Guid tenantId { get; set; }
}
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `tenantId` | `Guid` | Tenant identifier for data isolation |

## Purpose

This interface enables:

1. **Data Isolation** - Queries automatically filtered by tenant
2. **Partition Key** - Default partition key path (`/tenantId`)
3. **Paged Queries** - Tenant-aware paging support
4. **Security** - Prevent cross-tenant data access

## Implementation Examples

### Aggregate with Tenant

```csharp
public class TenantOrder : NostifyObject, IAggregate, IApplyable, ITenantFilterable
{
    public static string aggregateType => "TenantOrder";
    public static string currentStateContainerName => "tenant-orders";
    
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

### Projection with Tenant

```csharp
public class TenantDashboard : NostifyObject, IProjection, IApplyable, ITenantFilterable
{
    public static string containerName => "tenant-dashboards";
    
    public bool initialized { get; set; }
    public Guid tenantId { get; set; }
    
    public int OrderCount { get; set; }
    public decimal TotalRevenue { get; set; }
    
    public void Apply(Event @event)
    {
        @event.ApplyTo(this);
    }
}
```

## Usage with Paged Queries

```csharp
// Automatically filters by tenantId
var result = await container.GetPagedAsync<TenantOrder>(tableState, tenantId);
```

The query internally adds:

```csharp
.Where(x => x.tenantId == tenantId)
```

## Cosmos DB Partition Key

When using `ITenantFilterable`, containers use `/tenantId` as partition key:

```csharp
// Container creation
await nostify.GetContainerAsync("tenant-orders", "/tenantId");
```

This ensures:
- Tenant data is co-located
- Cross-tenant queries are prevented at the database level
- Efficient queries within tenant scope

## Security Pattern

```csharp
public async Task<TenantOrder?> GetOrderAsync(Guid orderId, Guid userTenantId)
{
    var order = await nostify.GetCurrentStateAsync<TenantOrder>(orderId);
    
    // Verify tenant access
    if (order?.tenantId != userTenantId)
    {
        throw new UnauthorizedAccessException("Access denied to this resource");
    }
    
    return order;
}
```

## Event Handling

Include tenantId in events:

```csharp
var @event = new Event(
    NostifyCommand.Create("TenantOrder"),
    orderId,
    new { 
        tenantId = currentTenantId,
        CustomerId = customerId,
        Total = total 
    },
    userId
);
```

## Best Practices

1. **Always Include tenantId** - Include in all events and projections
2. **Validate Access** - Verify tenant ownership before operations
3. **Use Partition Key** - Configure containers with `/tenantId`
4. **Filter Queries** - Use `GetPagedAsync` overload with tenantId
5. **Audit Tenant Operations** - Log tenant context for debugging

## Related Types

- [IAggregate](IAggregate.spec.md) - Often combined with
- [IProjection](IProjection.spec.md) - Often combined with
- [PagedQuery](PagedQuery.spec.md) - Tenant-aware queries
- [NostifyObject](NostifyObject.spec.md) - Base class
