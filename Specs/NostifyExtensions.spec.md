# NostifyExtensions Specification

## Overview

`NostifyExtensions` provides static extension methods for common operations in nostify applications, including JSON manipulation, Cosmos DB partition key handling, and HTTP request processing.

## Class Definition

```csharp
public static class NostifyExtensions
```

## Methods

### TryGetValue (object)

```csharp
public static bool TryGetValue<T>(this object obj, string propertyName, out T value)
```

Attempts to get a property value from any object.

| Parameter | Type | Description |
|-----------|------|-------------|
| `obj` | `object` | Source object |
| `propertyName` | `string` | Property name to find |
| `value` | `out T` | Output value if found |

**Returns:** `bool` - True if property found

### TryGetValue (JObject)

```csharp
public static bool TryGetValue<T>(this JObject obj, string propertyName, out T value)
```

Attempts to get a property value from a JObject.

| Parameter | Type | Description |
|-----------|------|-------------|
| `obj` | `JObject` | Source JObject |
| `propertyName` | `string` | Property name to find |
| `value` | `out T` | Output value if found |

**Returns:** `bool` - True if property found

### GetValue

```csharp
public static T? GetValue<T>(this JObject obj, string propertyName)
```

Gets a typed value from a JObject.

| Parameter | Type | Description |
|-----------|------|-------------|
| `obj` | `JObject` | Source JObject |
| `propertyName` | `string` | Property name |

**Returns:** `T?` - Value if found, null otherwise

### ToCosmosPartitionKey (string)

```csharp
public static PartitionKey ToCosmosPartitionKey(this string value)
```

Converts a string to a Cosmos DB partition key.

| Parameter | Type | Description |
|-----------|------|-------------|
| `value` | `string` | String value |

**Returns:** `PartitionKey` - Cosmos DB partition key

### ToCosmosPartitionKey (Guid)

```csharp
public static PartitionKey ToCosmosPartitionKey(this Guid value)
```

Converts a Guid to a Cosmos DB partition key.

| Parameter | Type | Description |
|-----------|------|-------------|
| `value` | `Guid` | Guid value |

**Returns:** `PartitionKey` - Cosmos DB partition key

### ToGuidOrThrow

```csharp
public static Guid ToGuidOrThrow(this string value, string errorMessage = null)
```

Parses a string to Guid, throwing on failure.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `value` | `string` | Required | String to parse |
| `errorMessage` | `string` | `null` | Custom error message |

**Returns:** `Guid` - Parsed Guid

**Throws:** `NostifyException` if parsing fails

### GetRequestBodyAsDynamic

```csharp
public static async Task<dynamic> GetRequestBodyAsDynamic(this HttpRequest request)
```

Reads HTTP request body to dynamic object.

| Parameter | Type | Description |
|-----------|------|-------------|
| `request` | `HttpRequest` | HTTP request |

**Returns:** `Task<dynamic>` - Request body as dynamic

**Throws:** `NostifyException` if body is null/empty or missing `id` property

## Usage Examples

### Property Access

```csharp
var order = new Order { CustomerId = Guid.NewGuid(), Total = 99.99m };

if (order.TryGetValue("Total", out decimal total))
{
    Console.WriteLine($"Total: {total}");
}
```

### JObject Operations

```csharp
var json = JObject.Parse("{\"name\": \"John\", \"age\": 30}");

// Get value with TryGetValue
if (json.TryGetValue("name", out string name))
{
    Console.WriteLine($"Name: {name}");
}

// Get value directly
var age = json.GetValue<int?>("age");
```

### Partition Key Conversion

```csharp
// String to PartitionKey
var pk1 = "tenant-123".ToCosmosPartitionKey();

// Guid to PartitionKey
var pk2 = tenantId.ToCosmosPartitionKey();

// Use with Cosmos DB
await container.ReadItemAsync<Order>(orderId.ToString(), pk2);
```

### Guid Parsing

```csharp
// Basic parsing
var id = "550e8400-e29b-41d4-a716-446655440000".ToGuidOrThrow();

// With custom error
var customerId = request.Query["customerId"]
    .ToGuidOrThrow("Invalid customer ID format");
```

### HTTP Request Body

```csharp
[Function("CreateOrder")]
public async Task<IActionResult> CreateOrder(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
{
    var body = await req.GetRequestBodyAsDynamic();
    
    var orderId = (Guid)body.id;
    var customerId = (Guid)body.customerId;
    var total = (decimal)body.total;
    
    // Process order...
}
```

## Cosmos DB PartitionKey Notes

The `ToCosmosPartitionKey()` extension provides a workaround for the Cosmos SDK's `PartitionKey` class name conflict:

```csharp
// Instead of
new Microsoft.Azure.Cosmos.PartitionKey(tenantId.ToString())

// Use
tenantId.ToCosmosPartitionKey()
```

## JSON Handling

Uses Newtonsoft.Json internally:

- `JObject.FromObject()` - Convert objects to JObject
- `JToken.ToObject<T>()` - Convert tokens to types
- `JsonConvert.DeserializeObject<T>()` - JSON parsing

## Error Handling

| Exception | Condition |
|-----------|-----------|
| `NostifyException` | `GetRequestBodyAsDynamic` with null/empty body |
| `NostifyException` | `GetRequestBodyAsDynamic` without `id` property |
| `NostifyException` | `ToGuidOrThrow` with invalid format |

## Best Practices

1. **Use TryGetValue** - For optional properties
2. **Use GetValue** - When null is acceptable
3. **Partition Key Extension** - For cleaner Cosmos DB code
4. **Validate IDs** - Use `ToGuidOrThrow` for required IDs

## Related Types

- [NostifyException](NostifyException.spec.md) - Thrown on errors
- [NostifyCosmosClient](NostifyCosmosClient.spec.md) - Uses partition keys
