# CosmosQueryExecutor Class Specification

## Overview

`CosmosQueryExecutor` is the production implementation of `IQueryExecutor` that executes LINQ queries against Azure Cosmos DB using FeedIterator.

## Class Definition

```csharp
public class CosmosQueryExecutor : IQueryExecutor
```

## Singleton Pattern

```csharp
public static readonly CosmosQueryExecutor Instance = new CosmosQueryExecutor();
```

Access via `CosmosQueryExecutor.Instance` for convenience.

## Methods

### ToListAsync

```csharp
public async Task<List<T>> ToListAsync<T>(IQueryable<T> query)
```

Executes a LINQ query and returns all results by iterating the FeedIterator.

**Implementation:**
```csharp
public async Task<List<T>> ToListAsync<T>(IQueryable<T> query)
{
    var results = new List<T>();
    var iterator = query.ToFeedIterator();
    
    while (iterator.HasMoreResults)
    {
        var response = await iterator.ReadNextAsync();
        results.AddRange(response);
    }
    
    return results;
}
```

### FirstOrDefaultAsync

```csharp
public async Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query)
```

Returns the first matching item using `Take(1)` optimization.

**Implementation:**
```csharp
public async Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query)
{
    var iterator = query.Take(1).ToFeedIterator();
    
    if (iterator.HasMoreResults)
    {
        var response = await iterator.ReadNextAsync();
        return response.FirstOrDefault();
    }
    
    return default;
}
```

### FirstOrNewAsync

```csharp
public async Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new()
```

Returns first matching item or creates new instance.

**Implementation:**
```csharp
public async Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new()
{
    var result = await FirstOrDefaultAsync(query);
    return result ?? new T();
}
```

### CountAsync

```csharp
public async Task<int> CountAsync<T>(IQueryable<T> query)
```

Returns count using Cosmos DB's `CountAsync` extension.

**Implementation:**
```csharp
public async Task<int> CountAsync<T>(IQueryable<T> query)
{
    return await query.CountAsync();
}
```

## Usage Examples

### Basic Usage

```csharp
var container = nostify.Repository.GetContainer("orders");
var executor = CosmosQueryExecutor.Instance;

// Get all pending orders
var pendingOrders = await executor.ToListAsync(
    container.GetItemLinqQueryable<Order>()
        .Where(o => o.Status == OrderStatus.Pending)
);

// Get single order
var order = await executor.FirstOrDefaultAsync(
    container.GetItemLinqQueryable<Order>()
        .Where(o => o.id == orderId)
);

// Count orders
var orderCount = await executor.CountAsync(
    container.GetItemLinqQueryable<Order>()
        .Where(o => o.tenantId == tenantId)
);
```

### With Service Classes

```csharp
public class OrderRepository
{
    private readonly Container _container;
    private readonly IQueryExecutor _executor;
    
    public OrderRepository(Container container, IQueryExecutor executor = null)
    {
        _container = container;
        _executor = executor ?? CosmosQueryExecutor.Instance;
    }
    
    public async Task<List<Order>> GetByCustomerAsync(Guid customerId)
    {
        return await _executor.ToListAsync(
            _container.GetItemLinqQueryable<Order>()
                .Where(o => o.CustomerId == customerId)
        );
    }
    
    public async Task<Order?> GetByIdAsync(Guid orderId)
    {
        return await _executor.FirstOrDefaultAsync(
            _container.GetItemLinqQueryable<Order>()
                .Where(o => o.id == orderId)
        );
    }
}
```

### With Paged Queries

```csharp
// CosmosQueryExecutor is the default for paged queries
var result = await container.GetPagedAsync<Order>(tableState, tenantId);

// Explicitly passing executor
var result = await container.GetPagedAsync<Order>(
    tableState, 
    tenantId, 
    CosmosQueryExecutor.Instance
);
```

## FeedIterator Pattern

The executor handles Cosmos DB's pagination internally:

```csharp
// Cosmos DB returns results in pages
var iterator = query.ToFeedIterator();

while (iterator.HasMoreResults)  // Loop through pages
{
    var response = await iterator.ReadNextAsync();  // Get next page
    // Process items in response
}
```

This is abstracted away from callers who simply get a `List<T>`.

## Performance Considerations

1. **ToListAsync** - Loads all results into memory
2. **FirstOrDefaultAsync** - Uses `Take(1)` to limit results
3. **CountAsync** - Uses optimized count operation
4. **Singleton** - Reusable instance with no state

## Best Practices

1. **Use Singleton** - `CosmosQueryExecutor.Instance`
2. **Limit Results** - Use `Take()` for large datasets
3. **Filter Early** - Apply `Where()` clauses before execution
4. **Project Data** - Use `Select()` to reduce payload size

## Related Types

- [IQueryExecutor](IQueryExecutor.spec.md) - Interface definition
- [PagedQuery](PagedQuery.spec.md) - Uses this executor
- [NostifyCosmosClient](NostifyCosmosClient.spec.md) - Container access
