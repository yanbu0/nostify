# NostifyCosmosClient Class Specification

## Overview

`NostifyCosmosClient` is a wrapper around the Azure Cosmos DB `CosmosClient` that provides simplified database and container access for the nostify framework.

## Class Definition

```csharp
public class NostifyCosmosClient
```

## Constructor

```csharp
public NostifyCosmosClient(CosmosClient cosmosClient, string databaseName)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `cosmosClient` | `CosmosClient` | The underlying Azure Cosmos DB client |
| `databaseName` | `string` | The name of the database to use |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Client` | `CosmosClient` | The underlying Cosmos DB client |
| `DatabaseName` | `string` | Name of the database |

## Methods

### GetDatabase

```csharp
public Database GetDatabase()
```

Returns the Cosmos DB `Database` object for the configured database name.

**Returns:** `Database` - The database reference

### GetContainer

```csharp
public Container GetContainer(string containerName)
```

Returns a container reference without creating it.

| Parameter | Type | Description |
|-----------|------|-------------|
| `containerName` | `string` | Name of the container |

**Returns:** `Container` - The container reference

### GetContainerAsync

```csharp
public async Task<Container> GetContainerAsync(
    string containerName, 
    string partitionKeyPath = "/tenantId"
)
```

Gets or creates a container with the specified configuration.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `containerName` | `string` | Required | Name of the container |
| `partitionKeyPath` | `string` | `"/tenantId"` | Partition key path |

**Returns:** `Task<Container>` - The container (created if needed)

### CreateDatabaseIfNotExistsAsync

```csharp
public async Task<DatabaseResponse> CreateDatabaseIfNotExistsAsync()
```

Ensures the database exists, creating it if necessary.

**Returns:** `Task<DatabaseResponse>` - The database response

## Usage Examples

### Basic Usage

```csharp
// Create client (typically done by NostifyFactory)
var cosmosClient = new CosmosClient(connectionString, options);
var nostifyClient = new NostifyCosmosClient(cosmosClient, "MyEventStore");

// Get database
var database = nostifyClient.GetDatabase();

// Get container
var container = nostifyClient.GetContainer("orders");
```

### Container Operations

```csharp
// Get or create container with custom partition key
var eventsContainer = await nostifyClient.GetContainerAsync(
    "event-store", 
    "/partitionKey"
);

// Get or create container with default partition key (/tenantId)
var projectionsContainer = await nostifyClient.GetContainerAsync("order-projections");
```

### Query Operations

```csharp
var container = nostifyClient.GetContainer("orders");

// Query using LINQ
var query = container.GetItemLinqQueryable<Order>()
    .Where(o => o.CustomerId == customerId)
    .ToFeedIterator();

var results = new List<Order>();
while (query.HasMoreResults)
{
    var response = await query.ReadNextAsync();
    results.AddRange(response);
}
```

### Item Operations

```csharp
var container = nostifyClient.GetContainer("orders");

// Create item
await container.CreateItemAsync(order, new PartitionKey(order.tenantId.ToString()));

// Read item
var response = await container.ReadItemAsync<Order>(
    orderId.ToString(), 
    new PartitionKey(tenantId.ToString())
);

// Upsert item
await container.UpsertItemAsync(order, new PartitionKey(order.tenantId.ToString()));

// Delete item
await container.DeleteItemAsync<Order>(
    orderId.ToString(), 
    new PartitionKey(tenantId.ToString())
);
```

## Container Naming Conventions

| Container Type | Naming Convention | Partition Key |
|----------------|-------------------|---------------|
| Event Store | `event-store` | `/partitionKey` |
| Current State | `{aggregate-type}-current` | `/tenantId` |
| Projections | `{projection-name}` | `/tenantId` |
| Sagas | `sagas` | `/tenantId` |
| Sequences | `sequences` | `/partitionKey` |

## Error Handling

| Exception | Condition |
|-----------|-----------|
| `CosmosException` | Database or container operations fail |
| `ArgumentNullException` | Container name is null or empty |

## Thread Safety

`NostifyCosmosClient` is thread-safe as it wraps the thread-safe `CosmosClient`. It can be used as a singleton.

## Best Practices

1. **Use Via INostify** - Access containers through `INostify` methods rather than directly
2. **Consistent Partition Keys** - Use standard partition key paths for consistency
3. **Connection Reuse** - Create one `NostifyCosmosClient` per application
4. **Indexing Policy** - Configure appropriate indexing for query patterns

## Related Types

- [INostify](INostify.spec.md) - Main interface that uses this client
- [Nostify](Nostify.spec.md) - Implementation using this client
- [NostifyFactory](NostifyFactory.spec.md) - Creates this client
