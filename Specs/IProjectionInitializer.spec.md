# IProjectionInitializer Interface Specification

## Overview

`IProjectionInitializer` defines the contract for initializing projections with external data and managing projection containers.

## Interface Definition

```csharp
public interface IProjectionInitializer<P> where P : NostifyObject, IProjection, IApplyable, new()
```

## Type Constraints

- `P : NostifyObject` - Must extend NostifyObject
- `P : IProjection` - Must implement IProjection
- `P : IApplyable` - Must implement IApplyable
- `P : new()` - Must have parameterless constructor

## Methods

### InitializeAsync (Single ID)

```csharp
Task<P> InitializeAsync(Guid projectionId)
```

Initializes a single projection by ID.

| Parameter | Type | Description |
|-----------|------|-------------|
| `projectionId` | `Guid` | ID of projection to initialize |

**Returns:** `Task<P>` - Initialized projection

### InitializeAsync (Multiple IDs)

```csharp
Task<List<P>> InitializeAsync(List<Guid> projectionIds)
```

Initializes multiple projections by their IDs.

| Parameter | Type | Description |
|-----------|------|-------------|
| `projectionIds` | `List<Guid>` | IDs of projections to initialize |

**Returns:** `Task<List<P>>` - Initialized projections

### InitializeAsync (Existing Objects)

```csharp
Task<List<P>> InitializeAsync(List<P> projections)
```

Initializes existing projection objects.

| Parameter | Type | Description |
|-----------|------|-------------|
| `projections` | `List<P>` | Projection objects to initialize |

**Returns:** `Task<List<P>>` - Initialized projections

### RebuildAsync

```csharp
Task RebuildAsync(DateTime? asOfDate = null)
```

Deletes and recreates the container, rebuilding all projections.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `asOfDate` | `DateTime?` | `null` | Point-in-time for projection state |

**Note:** This is a destructive operation.

### InitializeUninitializedAsync

```csharp
Task<List<P>> InitializeUninitializedAsync()
```

Finds and initializes only uninitialized projections.

**Returns:** `Task<List<P>>` - Newly initialized projections

## Usage Examples

### Basic Initialization

```csharp
public class OrderProjectionInitializer : IProjectionInitializer<OrderWithCustomer>
{
    private readonly INostify _nostify;
    private readonly ICustomerService _customerService;
    
    public async Task<OrderWithCustomer> InitializeAsync(Guid projectionId)
    {
        // Get or create projection
        var projection = await _nostify.GetProjectionAsync<OrderWithCustomer>(projectionId)
            ?? new OrderWithCustomer { id = projectionId };
        
        if (projection.initialized)
        {
            return projection;
        }
        
        // Fetch external data
        var customer = await _customerService.GetCustomerAsync(projection.CustomerId);
        projection.CustomerName = customer.Name;
        projection.CustomerEmail = customer.Email;
        
        // Mark initialized and save
        projection.initialized = true;
        await _nostify.UpsertProjectionAsync(projection);
        
        return projection;
    }
}
```

### With ExternalDataEventFactory

```csharp
public async Task<List<OrderWithCustomer>> InitializeAsync(List<OrderWithCustomer> projections)
{
    foreach (var projection in projections.Where(p => !p.initialized))
    {
        // Build external data events using factory
        var events = await ExternalDataEventFactory<OrderWithCustomer>
            .Build(_nostify)
            .WithSameServiceIdSelectors(p => p.CustomerId)
            .GetEventsAsync(projection);
        
        // Apply external data events
        foreach (var @event in events)
        {
            projection.Apply(@event);
        }
        
        projection.initialized = true;
    }
    
    // Bulk save
    await _nostify.UpsertProjectionAsync(projections);
    
    return projections;
}
```

### Container Rebuild

```csharp
// Rebuild all projections (destructive!)
await projectionInitializer.RebuildAsync();

// Rebuild as of specific date
await projectionInitializer.RebuildAsync(asOfDate: DateTime.UtcNow.AddDays(-7));
```

### Initialize Uninitialized Only

```csharp
// Useful for catch-up processing
var newlyInitialized = await projectionInitializer.InitializeUninitializedAsync();
Console.WriteLine($"Initialized {newlyInitialized.Count} projections");
```

## Initialization Flow

```
┌─────────────────────┐
│ InitializeAsync(id) │
└──────────┬──────────┘
           │
           ▼
    ┌──────────────┐
    │ Get/Create   │
    │  Projection  │
    └──────┬───────┘
           │
           ▼
    ┌──────────────┐
    │ initialized? │
    │    = true    │──────────▶ Return Existing
    └──────┬───────┘
           │ No
           ▼
    ┌──────────────┐
    │ Fetch External│
    │    Data       │
    └──────┬───────┘
           │
           ▼
    ┌──────────────┐
    │ Apply to     │
    │ Projection   │
    └──────┬───────┘
           │
           ▼
    ┌──────────────┐
    │ Set          │
    │ initialized  │
    │ = true       │
    └──────┬───────┘
           │
           ▼
    ┌──────────────┐
    │ Upsert       │
    │ Projection   │
    └──────┬───────┘
           │
           ▼
       Return
```

## Best Practices

1. **Check initialized** - Skip if already initialized
2. **Batch Operations** - Use list overloads for efficiency
3. **Error Handling** - Handle external service failures
4. **Idempotent** - Make initialization repeatable

## Related Types

- [IProjection](IProjection.spec.md) - Projection interface
- [ExternalDataEventFactory](ExternalDataEventFactory.spec.md) - External data handling
- [ExternalDataEvent](ExternalDataEvent.spec.md) - Event container
