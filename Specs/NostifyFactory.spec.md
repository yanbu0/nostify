# NostifyFactory Class Specification

## Overview

`NostifyFactory` is a static factory class that creates properly configured `INostify` instances using a fluent API. It provides methods for configuring Azure Cosmos DB (or Azure DocumentDB), Kafka/Event Hubs messaging, and HTTP client factories.

## Class Definition

```csharp
public static class NostifyFactory
```

## Configuration Class

### NostifyConfig

The configuration object used by all factory methods.

```csharp
public class NostifyConfig
{
    public string cosmosApiKey { get; set; }
    public string cosmosDbName { get; set; }
    public string cosmosEndpointUri { get; set; }
    public string kafkaUrl { get; set; }
    public int kafkaTopicAutoCreatePartitions { get; set; } = 2;
    public string kafkaUserName { get; set; }
    public string kafkaPassword { get; set; }
    public string defaultPartitionKeyPath { get; set; }
    public Guid defaultTenantId { get; set; }
    public ProducerConfig producerConfig { get; set; }
    public bool createContainers { get; set; }
    public int? containerThroughput { get; set; }
    public bool useGatewayConnection { get; set; }
    public IHttpClientFactory? httpClientFactory { get; set; }
}
```

## Factory Methods

### WithCosmos

Configures nostify to use Azure Cosmos DB for data storage.

```csharp
public static NostifyConfig WithCosmos(
    string cosmosApiKey, 
    string cosmosDbName, 
    string cosmosEndpointUri, 
    bool? createContainers = false, 
    int? containerThroughput = null, 
    bool useGatewayConnection = false
)

public static NostifyConfig WithCosmos(
    this NostifyConfig config,
    string cosmosApiKey, 
    string cosmosDbName, 
    string cosmosEndpointUri, 
    bool? createContainers = false, 
    int? containerThroughput = null, 
    bool useGatewayConnection = false
)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `cosmosApiKey` | `string` | Required | Azure Cosmos DB API key |
| `cosmosDbName` | `string` | Required | Database name |
| `cosmosEndpointUri` | `string` | Required | Endpoint URI (e.g., "https://myaccount.documents.azure.com:443/") |
| `createContainers` | `bool?` | `false` | Whether to auto-create containers |
| `containerThroughput` | `int?` | `null` | Throughput for containers (RU/s) |
| `useGatewayConnection` | `bool` | `false` | Use gateway mode instead of direct connection |

**Returns:** `NostifyConfig` - Configuration object for method chaining

### WithDocumentDB

Configures nostify to use Azure DocumentDB (legacy name for Cosmos DB). This method is functionally equivalent to `WithCosmos` and is provided for backwards compatibility.

```csharp
public static NostifyConfig WithDocumentDB(
    string cosmosApiKey, 
    string cosmosDbName, 
    string cosmosEndpointUri, 
    bool? createContainers = false, 
    int? containerThroughput = null, 
    bool useGatewayConnection = false
)

public static NostifyConfig WithDocumentDB(
    this NostifyConfig config,
    string cosmosApiKey, 
    string cosmosDbName, 
    string cosmosEndpointUri, 
    bool? createContainers = false, 
    int? containerThroughput = null, 
    bool useGatewayConnection = false
)
```

Parameters are identical to `WithCosmos`. Azure DocumentDB was the original name for what is now Azure Cosmos DB, and both methods use the same underlying implementation.

**Returns:** `NostifyConfig` - Configuration object for method chaining

### WithKafka

Configures nostify to use Apache Kafka for event messaging.

```csharp
public static NostifyConfig WithKafka(ProducerConfig producerConfig)

public static NostifyConfig WithKafka(
    this NostifyConfig config, 
    ProducerConfig producerConfig
)

public static NostifyConfig WithKafka(
    string kafkaUrl, 
    string kafkaUserName = null, 
    string kafkaPassword = null, 
    int kafkaTopicAutoCreatePartitions = 2
)

public static NostifyConfig WithKafka(
    this NostifyConfig config,
    string kafkaUrl, 
    string kafkaUserName = null, 
    string kafkaPassword = null, 
    int kafkaTopicAutoCreatePartitions = 2
)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `kafkaUrl` | `string` | Required | Kafka broker URL (e.g., "localhost:9092") |
| `kafkaUserName` | `string` | `null` | Username for SASL authentication |
| `kafkaPassword` | `string` | `null` | Password for SASL authentication |
| `kafkaTopicAutoCreatePartitions` | `int` | `2` | Number of partitions for auto-created topics |
| `producerConfig` | `ProducerConfig` | Required | Confluent Kafka producer configuration |

**Returns:** `NostifyConfig` - Configuration object for method chaining

### WithEventHubs

Configures nostify to use Azure Event Hubs for event messaging (using Kafka protocol).

```csharp
public static NostifyConfig WithEventHubs(
    string eventHubsConnectionString, 
    bool diagnosticLogging = false, 
    int kafkaTopicAutoCreatePartitions = 2
)

public static NostifyConfig WithEventHubs(
    this NostifyConfig config,
    string eventHubsConnectionString, 
    bool diagnosticLogging = false, 
    int kafkaTopicAutoCreatePartitions = 2
)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `eventHubsConnectionString` | `string` | Required | Event Hubs connection string |
| `diagnosticLogging` | `bool` | `false` | Enable Kafka debug logging |
| `kafkaTopicAutoCreatePartitions` | `int` | `2` | Number of partitions for auto-created topics |

**Returns:** `NostifyConfig` - Configuration object for method chaining

### WithHttp

Configures nostify to use an IHttpClientFactory for making HTTP requests.

```csharp
public static NostifyConfig WithHttp(
    this NostifyConfig config, 
    IHttpClientFactory httpClientFactory
)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `httpClientFactory` | `IHttpClientFactory` | Factory for creating HTTP clients |

**Returns:** `NostifyConfig` - Configuration object for method chaining

### Build

Builds the final `INostify` instance from the configuration.

```csharp
public static INostify Build(this NostifyConfig config)

public static INostify Build<T>(
    this NostifyConfig config, 
    bool verbose = false
) where T : IAggregate
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `verbose` | `bool` | `false` | Output detailed build information to console |
| `T` | `IAggregate` | Required | Aggregate type to scan for commands (generic version only) |

The generic version auto-creates Kafka topics for all commands found in the assembly of type `T` and optionally creates Cosmos DB containers.

**Returns:** `INostify` - Fully configured nostify instance

## Usage Examples

### Basic Setup with Cosmos DB

```csharp
var nostify = NostifyFactory
    .WithCosmos("api-key", "database-name", "https://myaccount.documents.azure.com:443/")
    .WithKafka("localhost:9092")
    .Build();
```

### Using DocumentDB (Legacy Azure DocumentDB)

```csharp
var nostify = NostifyFactory
    .WithDocumentDB("api-key", "database-name", "https://myaccount.documents.azure.com:443/")
    .WithKafka("localhost:9092")
    .Build();
```

### With Auto-Create Containers and Topics

```csharp
var nostify = NostifyFactory
    .WithCosmos(
        cosmosApiKey: apiKey,
        cosmosDbName: "ProductionDB",
        cosmosEndpointUri: "https://myaccount.documents.azure.com:443/",
        createContainers: true,
        containerThroughput: 1000
    )
    .WithKafka("localhost:9092")
    .Build<MyAggregate>(verbose: true);
```

### With Event Hubs

```csharp
var eventHubsConnectionString = "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=...";

var nostify = NostifyFactory
    .WithCosmos(apiKey, dbName, endpointUri)
    .WithEventHubs(eventHubsConnectionString)
    .Build();
```

### With SASL Authentication for Kafka

```csharp
var nostify = NostifyFactory
    .WithCosmos(apiKey, dbName, endpointUri)
    .WithKafka(
        kafkaUrl: "my-kafka-broker.com:9092",
        kafkaUserName: "my-user",
        kafkaPassword: "my-password"
    )
    .Build();
```

### With Custom HTTP Client Factory

```csharp
var nostify = NostifyFactory
    .WithCosmos(apiKey, dbName, endpointUri)
    .WithKafka(kafkaUrl)
    .WithHttp(httpClientFactory)
    .Build();
```

### Dependency Injection Registration

```csharp
// In Program.cs or Startup.cs
services.AddSingleton<INostify>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    
    return NostifyFactory
        .WithCosmos(
            config["Cosmos:ApiKey"],
            config["Cosmos:DatabaseName"],
            config["Cosmos:EndpointUri"]
        )
        .WithKafka(config["Kafka:Url"])
        .WithHttp(httpFactory)
        .Build();
});
```

## Internal Behavior

The fluent API allows method chaining to build up a configuration. The `Build` method performs these steps:

1. **Create Cosmos Client** - Initializes `NostifyCosmosClient` with API key, database name, and endpoint
2. **Create Kafka Producer** - Initializes Kafka producer with the provided configuration
3. **Auto-Create Topics** (generic `Build<T>` only) - Scans assembly for commands and creates Kafka topics
4. **Auto-Create Containers** (when `createContainers = true`) - Creates database and container infrastructure
5. **Return Nostify Instance** - Returns configured `Nostify` implementation

## DocumentDB vs Cosmos DB

Azure DocumentDB was rebranded to Azure Cosmos DB in 2017. The `WithDocumentDB` methods are provided for:
- **Backwards compatibility** with existing code
- **Legacy endpoint support** (documents.azure.com, documents.azure.us)
- **Naming preference** for teams still using DocumentDB terminology

Both `WithCosmos` and `WithDocumentDB` use identical underlying implementation and are fully interchangeable.

## Error Handling

| Exception | Condition |
|-----------|-----------|
| `NostifyException` | Error during Build with auto-create topics |
| `ArgumentNullException` | Required parameters are null or empty |
| `CosmosException` | Invalid Cosmos DB credentials or endpoint |
| `KafkaException` | Invalid Kafka URL or broker unavailable |

## Best Practices

1. **Use Singleton Pattern** - Create one `INostify` instance per application
2. **Secure Credentials** - Use environment variables or Azure Key Vault for secrets
3. **Choose Connection Mode** - Use direct mode (default) for lower latency
4. **Auto-Create in Development** - Use `createContainers: true` for local development
5. **Manual Create in Production** - Pre-create containers with proper throughput settings
6. **Method Chaining** - Use fluent API for clean, readable configuration

## Related Types

- [INostify](INostify.spec.md) - Interface returned by factory
- [Nostify](Nostify.spec.md) - Implementation class
- [NostifyCosmosClient](NostifyCosmosClient.spec.md) - Cosmos DB wrapper
- [NewtonsoftJsonCosmosSerializer](NewtonsoftJsonCosmosSerializer.spec.md) - Custom serializer
