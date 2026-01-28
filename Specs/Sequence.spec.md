# Sequence Class Specification

## Overview

`Sequence` provides atomic sequential number generation within Cosmos DB partitions. It's useful for generating order numbers, invoice numbers, and other sequentially incremented identifiers.

## Class Definition

```csharp
public class Sequence
```

## Constructor

```csharp
public Sequence(string name, string partitionKey, long startValue = 1)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | `string` | Required | Sequence name within partition |
| `partitionKey` | `string` | Required | Partition key value |
| `startValue` | `long` | `1` | Initial sequence value |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `id` | `string` | Document ID: `{partitionKey}:{name}` |
| `name` | `string` | Sequence name within partition |
| `currentValue` | `long` | Current sequence value |
| `partitionKey` | `string` | Partition key value |

## Static Methods

### GenerateId

```csharp
public static string GenerateId(string name, string partitionKey)
```

Creates a deterministic ID for the sequence document.

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | Sequence name |
| `partitionKey` | `string` | Partition key value |

**Returns:** `string` - Format: `{partitionKey}:{name}`

## Usage Examples

### Getting Next Value

```csharp
// Get next order number for a tenant
var orderNumber = await nostify.GetNextSequenceValueAsync(
    "orderNumber", 
    tenantId.ToString()
);

// Use the sequence value
var order = new Order
{
    id = Guid.NewGuid(),
    OrderNumber = $"ORD-{orderNumber:D6}", // ORD-000001
    tenantId = tenantId
};
```

### Reserving a Range

```csharp
// Reserve 100 sequence values for batch processing
var range = await nostify.GetSequenceRangeAsync(
    "invoiceNumber",
    tenantId.ToString(),
    100
);

// Use the range
foreach (var item in itemsToProcess)
{
    var invoiceNumber = range.ToArray()[processedCount++];
    // Process item with invoice number
}
```

### Multiple Sequences

```csharp
// Different sequences for different purposes
var orderNum = await nostify.GetNextSequenceValueAsync("order", tenantId);
var invoiceNum = await nostify.GetNextSequenceValueAsync("invoice", tenantId);
var shipmentNum = await nostify.GetNextSequenceValueAsync("shipment", tenantId);
```

## SequenceRange Struct

### Definition

```csharp
public readonly struct SequenceRange
```

### Constructor

```csharp
public SequenceRange(long start, long end)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `start` | `long` | First value (inclusive) |
| `end` | `long` | Last value (inclusive) |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Start` | `long` | First value in range (inclusive) |
| `End` | `long` | Last value in range (inclusive) |
| `Count` | `int` | Number of values in range |

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetEnumerator` | `IEnumerator<long> GetEnumerator()` | Yields all values |
| `ToArray` | `long[] ToArray()` | Returns all values as array |
| `Contains` | `bool Contains(long value)` | Checks if value is in range |
| `ToString` | `string ToString()` | Format: `[Start..End]` |

### SequenceRange Usage

```csharp
var range = await nostify.GetSequenceRangeAsync("batch", partition, 10);

// Iterate values
foreach (var value in range)
{
    Console.WriteLine(value);
}

// Get as array
long[] values = range.ToArray();

// Check bounds
if (range.Contains(5))
{
    Console.WriteLine("5 is in range");
}

// Format for display
Console.WriteLine(range.ToString()); // "[1..10]"
```

## Cosmos DB Storage

Sequences are stored in a `sequences` container:

```json
{
    "id": "tenant-123:orderNumber",
    "name": "orderNumber",
    "currentValue": 42,
    "partitionKey": "tenant-123"
}
```

## Atomic Operations

Sequence increments use Cosmos DB patch operations:

```csharp
// Internal implementation
await container.PatchItemAsync<Sequence>(
    id,
    new PartitionKey(partitionKey),
    new[] { PatchOperation.Increment("/currentValue", 1) }
);
```

This ensures atomic increments even under concurrent load.

## Best Practices

1. **Partition by Tenant** - Use tenant ID as partition key for isolation
2. **Descriptive Names** - Use clear sequence names (e.g., "orderNumber", "invoiceNumber")
3. **Batch Reservations** - Use `GetSequenceRangeAsync` for bulk operations
4. **Don't Assume Gaps** - Sequences may have gaps due to failed operations

## Thread Safety

- `Sequence` documents are thread-safe due to Cosmos DB atomic operations
- `SequenceRange` is a readonly struct, inherently thread-safe
- Multiple instances can safely request sequences concurrently

## Error Handling

| Exception | Condition |
|-----------|-----------|
| `CosmosException` | Database connectivity issues |
| `ArgumentException` | Invalid name or partition key |

## Related Types

- [INostify](INostify.spec.md) - Contains sequence operations
- [Nostify](Nostify.spec.md) - Implements sequence operations
