# IQueryExecutor Interface Specification

## Overview

`IQueryExecutor` abstracts LINQ query execution to enable testability. It decouples query logic from Cosmos DB, allowing unit tests to use mock implementations.

## Interface Definition

```csharp
public interface IQueryExecutor
```

## Methods

### ToListAsync

```csharp
Task<List<T>> ToListAsync<T>(IQueryable<T> query)
```

Executes a query and returns all matching items.

| Parameter | Type | Description |
|-----------|------|-------------|
| `query` | `IQueryable<T>` | LINQ query to execute |

**Returns:** `Task<List<T>>` - All matching items

### FirstOrDefaultAsync

```csharp
Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query)
```

Returns the first matching item or null.

| Parameter | Type | Description |
|-----------|------|-------------|
| `query` | `IQueryable<T>` | LINQ query to execute |

**Returns:** `Task<T?>` - First item or null

### FirstOrNewAsync

```csharp
Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new()
```

Returns the first matching item or a new instance.

| Parameter | Type | Description |
|-----------|------|-------------|
| `query` | `IQueryable<T>` | LINQ query to execute |

**Returns:** `Task<T>` - First item or new instance

### CountAsync

```csharp
Task<int> CountAsync<T>(IQueryable<T> query)
```

Returns the count of matching items.

| Parameter | Type | Description |
|-----------|------|-------------|
| `query` | `IQueryable<T>` | LINQ query to execute |

**Returns:** `Task<int>` - Number of matching items

## Usage Examples

### With Production Executor

```csharp
var executor = CosmosQueryExecutor.Instance;

var orders = await executor.ToListAsync(
    container.GetItemLinqQueryable<Order>()
        .Where(o => o.Status == OrderStatus.Pending)
);
```

### With Mock Executor (Testing)

```csharp
public class MockQueryExecutor : IQueryExecutor
{
    private readonly List<object> _data;
    
    public MockQueryExecutor(params object[] data)
    {
        _data = data.ToList();
    }
    
    public Task<List<T>> ToListAsync<T>(IQueryable<T> query)
        => Task.FromResult(query.ToList());
    
    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query)
        => Task.FromResult(query.FirstOrDefault());
    
    public Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new()
        => Task.FromResult(query.FirstOrDefault() ?? new T());
    
    public Task<int> CountAsync<T>(IQueryable<T> query)
        => Task.FromResult(query.Count());
}

// In tests
var mockExecutor = new MockQueryExecutor(
    new Order { id = Guid.NewGuid(), Status = OrderStatus.Pending },
    new Order { id = Guid.NewGuid(), Status = OrderStatus.Shipped }
);

var result = await service.GetPendingOrders(mockExecutor);
```

### With Paged Queries

```csharp
// PagedQueryExtensions accepts optional IQueryExecutor
var result = await container.GetPagedAsync<Order>(
    tableState, 
    tenantId,
    executor: mockExecutor  // Optional, defaults to CosmosQueryExecutor.Instance
);
```

## Purpose

1. **Testability** - Mock database operations in unit tests
2. **Abstraction** - Decouple from Cosmos DB FeedIterator
3. **Consistency** - Single interface for all query operations
4. **Flexibility** - Swap implementations (Cosmos, EF Core, in-memory)

## Implementation Pattern

Classes that need query execution should accept `IQueryExecutor`:

```csharp
public class OrderService
{
    private readonly Container _container;
    private readonly IQueryExecutor _executor;
    
    public OrderService(Container container, IQueryExecutor executor = null)
    {
        _container = container;
        _executor = executor ?? CosmosQueryExecutor.Instance;
    }
    
    public async Task<List<Order>> GetPendingOrders()
    {
        var query = _container.GetItemLinqQueryable<Order>()
            .Where(o => o.Status == OrderStatus.Pending);
            
        return await _executor.ToListAsync(query);
    }
}
```

## Best Practices

1. **Default to Singleton** - Use `CosmosQueryExecutor.Instance` as default
2. **Accept in Constructor** - Allow injection for testing
3. **Use Async Methods** - All methods are async
4. **Simple Mocks** - Create test-specific mock implementations

## Related Types

- [CosmosQueryExecutor](CosmosQueryExecutor.spec.md) - Production implementation
- [PagedQuery](PagedQuery.spec.md) - Uses this interface
