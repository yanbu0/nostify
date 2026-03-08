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

### Kafka Consumer Base Configuration

The `Build` method also creates a base `ConsumerConfig` used by `Nostify.GetOrCreateKafkaConsumer()`:

```csharp
var baseConsumerConfig = new ConsumerConfig
{
    BootstrapServers = kafkaUrl,
    AutoOffsetReset = AutoOffsetReset.Latest,
    EnableAutoCommit = true
};
// SASL settings (SecurityProtocol, SaslMechanism, SaslUsername, SaslPassword)
// are mirrored from the ProducerConfig when present.
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

### With Structured Logging

The `.WithLogger()` fluent method enables structured logging throughout the nostify framework, replacing `Console.WriteLine` calls with `ILogger` output:

```csharp
var logger = services.BuildServiceProvider()
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("nostify");

var nostify = NostifyFactory.WithCosmos(
        cosmosApiKey: apiKey,
        cosmosDbName: dbName,
        cosmosEndpointUri: endPoint,
        createContainers: true)
    .WithKafka(kafka)
    .WithHttp(httpClientFactory)
    .WithLogger(logger)      // Enables structured logging
    .Build<MyAggregate>(verbose: true);
```

When a logger is provided:
- All verbose/debug output uses `ILogger.LogDebug()` instead of `Console.WriteLine`
- Error output uses `ILogger.LogError()` instead of `Console.Error.WriteLine`
- The logger is automatically propagated to `RetryOptions` in `DefaultEventHandlers` and internal retry sites
- If no logger is set, the framework falls back to `Console.WriteLine` for backwards compatibility

### WithAsyncEventRequest

The `.WithAsyncEventRequest()` fluent method opts-in to automatic creation of `{aggregateType}_EventRequest` Kafka topics when `Build<T>()` is called. Required when using `AsyncEventRequester<T>` / `ExternalDataEventFactory` Kafka-based async event request/response.

```csharp
var nostify = NostifyFactory.WithCosmos(apiKey, dbName, endPoint)
    .WithKafka(kafka)
    .WithAsyncEventRequest()   // opt-in: creates _EventRequest topics
    .WithHttp(httpClientFactory)
    .Build<MyAggregate>(verbose: true);
```

- Sets `NostifyConfig.autoCreateEventRequestTopics = true`
- Without this call, `Build<T>()` creates only command topics (no `_EventRequest` topics)
- Position in the fluent chain does not matter

## Internal Behavior

The `Build` method performs these steps:

1. **Create JSON Settings** - Merges custom settings with defaults
2. **Create Cosmos Client** - Initializes `CosmosClient` with connection string and options
3. **Create NostifyCosmosClient** - Wraps the Cosmos client for nostify operations
4. **Create Kafka Producer** - Initializes Kafka producer with idempotence enabled
5. **Create Kafka Consumer Config** - Creates base `ConsumerConfig` with SASL settings mirrored from producer
6. **Return Nostify Instance** - Returns configured `Nostify` implementation (with consumer config for lazy consumer creation)

### Build<T>() Auto-Topic Creation

The generic `Build<T>()` method (where `T : IAggregate`) performs all steps above plus automatic topic creation:

1. **Scan for NostifyCommand types** - Finds all `NostifyCommand` subclasses in the assembly of `T` and creates a Kafka topic for each static command field.
2. **Scan for IAggregate types (opt-in)** - When `config.autoCreateEventRequestTopics` is `true` (set via `.WithAsyncEventRequest()`), finds all concrete `IAggregate` implementations in the same assembly and creates an `{aggregateType}_EventRequest` topic for each. Without `.WithAsyncEventRequest()`, this step is skipped entirely.
3. **Filter existing topics** - Queries the Kafka AdminClient for existing topics and only creates new ones.
4. **Create topics** - Calls `CreateTopicsAsync` for all new topics.

The `_EventRequest` topics use the same partition count (`kafkaTopicAutoCreatePartitions`) and replication factor as command topics. For Event Hubs, this requires `WithEventHubsManagement()` credentials (same as command topic auto-creation).

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
