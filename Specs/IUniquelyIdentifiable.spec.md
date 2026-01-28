# IUniquelyIdentifiable Interface Specification

## Overview

`IUniquelyIdentifiable` is the base interface for all domain entities that have a unique identifier in the nostify framework.

## Interface Definition

```csharp
public interface IUniquelyIdentifiable
{
    Guid id { get; set; }
}
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `id` | `Guid` | Unique identifier for the entity |

## Purpose

This interface ensures:

1. **Unique Identity** - All entities have a GUID identifier
2. **Cosmos DB Compatibility** - Maps to document `id` field
3. **Event Correlation** - Links events to aggregate roots
4. **Query Support** - Enables lookup by ID

## Implementation

The primary implementation is through `NostifyObject`:

```csharp
public abstract class NostifyObject : IUniquelyIdentifiable
{
    public Guid id { get; set; }
    
    protected NostifyObject()
    {
        id = Guid.NewGuid();
    }
    
    protected NostifyObject(Guid id)
    {
        this.id = id;
    }
}
```

## Inheritance Hierarchy

```
IUniquelyIdentifiable
    │
    └── NostifyObject (abstract)
            │
            ├── IAggregate implementations
            │       ├── Order
            │       ├── Customer
            │       └── Product
            │
            └── IProjection implementations
                    ├── OrderSummary
                    ├── CustomerDashboard
                    └── InventoryView
```

## Usage Examples

### Generic Operations

```csharp
public async Task<T?> GetByIdAsync<T>(Guid id) where T : IUniquelyIdentifiable
{
    var container = GetContainer<T>();
    var response = await container.ReadItemAsync<T>(
        id.ToString(), 
        new PartitionKey(id.ToString())
    );
    return response.Resource;
}
```

### Collection Operations

```csharp
public async Task<List<T>> GetByIdsAsync<T>(List<Guid> ids) where T : IUniquelyIdentifiable
{
    var container = GetContainer<T>();
    var results = new List<T>();
    
    foreach (var id in ids)
    {
        var item = await container.ReadItemAsync<T>(
            id.ToString(), 
            new PartitionKey(id.ToString())
        );
        results.Add(item.Resource);
    }
    
    return results;
}
```

### Type Constraint

```csharp
// Methods can constrain to IUniquelyIdentifiable
public void ProcessEntity<T>(T entity) where T : IUniquelyIdentifiable
{
    Console.WriteLine($"Processing entity: {entity.id}");
}
```

## Cosmos DB Document Structure

The `id` property becomes the document ID:

```json
{
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "customerId": "...",
    "total": 99.99,
    "_ts": 1705312200
}
```

## Best Practices

1. **Use NostifyObject** - Extend `NostifyObject` for default behavior
2. **Don't Change IDs** - IDs should be immutable after creation
3. **Use GUIDs** - Provides globally unique identifiers
4. **Lowercase Property** - Use lowercase `id` for Cosmos DB

## Related Types

- [NostifyObject](NostifyObject.spec.md) - Primary implementation
- [IAggregate](IAggregate.spec.md) - Extends this interface
