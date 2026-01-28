# NostifyFactory Class Specification

## Overview

`NostifyFactory` is a static factory class that creates properly configured `INostify` instances. It handles the setup of Cosmos DB clients, Kafka producers, and JSON serialization settings.

## Class Definition

```csharp
public static class NostifyFactory
```

## Factory Methods

### Build (Full Configuration)

```csharp
public static INostify Build(
    string cosmosConnectionString,
    string kafkaUrl,
    string databaseName,
    string eventStoreContainerName = "event-store",
    Guid? defaultUserId = null,
    JsonSerializerSettings? jsonSerializerSettings = null,
    CosmosClientOptions? cosmosClientOptions = null
)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `cosmosConnectionString` | `string` | Required | Azure Cosmos DB connection string |
| `kafkaUrl` | `string` | Required | Kafka broker URL (e.g., "localhost:9092") |
| `databaseName` | `string` | Required | Cosmos DB database name |
| `eventStoreContainerName` | `string` | `"event-store"` | Name for the event store container |
| `defaultUserId` | `Guid?` | `Guid.Empty` | Default user ID for events |
| `jsonSerializerSettings` | `JsonSerializerSettings?` | `null` | Custom JSON settings for serialization |
| `cosmosClientOptions` | `CosmosClientOptions?` | `null` | Custom Cosmos DB client options |

**Returns:** `INostify` - A fully configured nostify instance

### Build (Configuration Object)

```csharp
public static INostify Build(NostifyConfiguration config)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `config` | `NostifyConfiguration` | Configuration object with all settings |

## NostifyConfiguration Class

```csharp
public class NostifyConfiguration
{
    public string CosmosConnectionString { get; set; }
    public string KafkaUrl { get; set; }
    public string DatabaseName { get; set; }
    public string EventStoreContainerName { get; set; } = "event-store";
    public Guid DefaultUserId { get; set; } = Guid.Empty;
    public JsonSerializerSettings? JsonSerializerSettings { get; set; }
    public CosmosClientOptions? CosmosClientOptions { get; set; }
}
```

## Default Configuration

When no custom options are provided, the factory applies these defaults:

### Cosmos Client Options

```csharp
var defaultCosmosOptions = new CosmosClientOptions
{
    Serializer = new NewtonsoftJsonCosmosSerializer(jsonSettings),
    ConnectionMode = ConnectionMode.Direct,
    ConsistencyLevel = ConsistencyLevel.Session,
    AllowBulkExecution = true
};
```

### JSON Serializer Settings

```csharp
var defaultJsonSettings = new JsonSerializerSettings
{
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    NullValueHandling = NullValueHandling.Ignore,
    DateTimeZoneHandling = DateTimeZoneHandling.Utc,
    Converters = { new StringEnumConverter() }
};
```

### Kafka Producer Configuration

```csharp
var producerConfig = new ProducerConfig
{
    BootstrapServers = kafkaUrl,
    Acks = Acks.All,
    EnableIdempotence = true,
    MessageSendMaxRetries = 3,
    RetryBackoffMs = 1000
};
```

## Usage Examples

### Basic Setup

```csharp
var nostify = NostifyFactory.Build(
    cosmosConnectionString: "AccountEndpoint=https://localhost:8081/;AccountKey=...",
    kafkaUrl: "localhost:9092",
    databaseName: "MyEventStore"
);
```

### With Custom Configuration

```csharp
var config = new NostifyConfiguration
{
    CosmosConnectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION"),
    KafkaUrl = Environment.GetEnvironmentVariable("KAFKA_URL"),
    DatabaseName = "ProductionEventStore",
    EventStoreContainerName = "events",
    DefaultUserId = Guid.Parse("00000000-0000-0000-0000-000000000001")
};

var nostify = NostifyFactory.Build(config);
```

### With Custom Cosmos Options

```csharp
var cosmosOptions = new CosmosClientOptions
{
    ApplicationRegion = Regions.WestUS2,
    RequestTimeout = TimeSpan.FromSeconds(30),
    MaxRetryAttemptsOnRateLimitedRequests = 9,
    MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30)
};

var nostify = NostifyFactory.Build(
    cosmosConnectionString: "...",
    kafkaUrl: "...",
    databaseName: "MyDB",
    cosmosClientOptions: cosmosOptions
);
```

### Dependency Injection Registration

```csharp
// In Program.cs or Startup.cs
services.AddSingleton<INostify>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    
    return NostifyFactory.Build(
        cosmosConnectionString: config["Cosmos:ConnectionString"],
        kafkaUrl: config["Kafka:Url"],
        databaseName: config["Cosmos:DatabaseName"]
    );
});
```

## Internal Behavior

The `Build` method performs these steps:

1. **Create JSON Settings** - Merges custom settings with defaults
2. **Create Cosmos Client** - Initializes `CosmosClient` with connection string and options
3. **Create NostifyCosmosClient** - Wraps the Cosmos client for nostify operations
4. **Create Kafka Producer** - Initializes Kafka producer with idempotence enabled
5. **Return Nostify Instance** - Returns configured `Nostify` implementation

## Error Handling

| Exception | Condition |
|-----------|-----------|
| `ArgumentNullException` | Required parameters are null or empty |
| `CosmosException` | Invalid Cosmos DB connection string |
| `KafkaException` | Invalid Kafka URL or broker unavailable |

## Best Practices

1. **Use Singleton Pattern** - Create one `INostify` instance per application
2. **Secure Connection Strings** - Use environment variables or Azure Key Vault
3. **Configure Retries** - Use custom `CosmosClientOptions` for production resilience
4. **Enable Bulk Execution** - Keep `AllowBulkExecution = true` for high-throughput scenarios

## Related Types

- [INostify](INostify.spec.md) - Interface returned by factory
- [Nostify](Nostify.spec.md) - Implementation class
- [NostifyCosmosClient](NostifyCosmosClient.spec.md) - Cosmos DB wrapper
- [NewtonsoftJsonCosmosSerializer](NewtonsoftJsonCosmosSerializer.spec.md) - Custom serializer
