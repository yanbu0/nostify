# Nostify Class Specification

## Overview

`Nostify` is the default implementation of `INostify`. It provides the core event-sourcing functionality including event persistence to Cosmos DB, event publishing to Kafka, aggregate rehydration, and projection management.

## Class Definition

```csharp
public class Nostify : INostify, IDisposable
```

## Constructor

The primary public constructor is for development (no Kafka credentials):

```csharp
public Nostify(
    string primaryKey,
    string dbName,
    string cosmosEndpointUri,
    string kafkaUrl,
    IHttpClientFactory httpClientFactory,
    string defaultPartitionKeyPath = "/tenantId",
    Guid defaultTenantId = default
)
```

The framework-internal constructor (used by `NostifyFactory.Build`) additionally accepts `ILogger?`, `ConsumerConfig?`, and `RetryOptions? defaultRetryOptions`. When `defaultRetryOptions` is `null`, it defaults to `new RetryOptions()`.

| Parameter | Type | Description |
|-----------|------|-------------|
| `primaryKey` / `repository` | `string` / `NostifyCosmosClient` | Cosmos DB credentials or pre-built client |
| `kafkaUrl` | `string` | Kafka broker URL |
| `httpClientFactory` | `IHttpClientFactory` | Factory for outbound HTTP calls |
| `defaultPartitionKeyPath` | `string` | Partition key path for containers (default `/tenantId`) |
| `defaultTenantId` | `Guid` | Default tenant ID for multi-tenant scenarios |
| `logger` | `ILogger?` | Optional structured logger |
| `defaultRetryOptions` | `RetryOptions?` | Default retry configuration; falls back to `new RetryOptions()` |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Repository` | `NostifyCosmosClient` | Cosmos DB client wrapper |
| `BulkPublisher` | `IProducer<string, string>` | Kafka producer for event publishing |
| `DefaultUserId` | `Guid` | Default user for anonymous operations |
| `kafkaUrl` | `string` | Kafka connection URL |
| `eventStoreContainerName` | `string` | Event store container name |
| `Logger` | `ILogger?` | Optional structured logger for diagnostic output and retry logging. Set via `NostifyFactory.WithLogger()`. Falls back to `Console.WriteLine` when null. |
| `DefaultRetryOptions` | `RetryOptions` | Default retry configuration applied by all default handlers when `allowRetry = true`. Configured via `NostifyFactory.WithCosmos(defaultRetryOptions)`. Defaults to `new RetryOptions()` (3 retries, 1 s exponential backoff, `RetryWhenNotFound = false`). |

## Kafka Consumer Cache

The `Nostify` class maintains a `ConcurrentDictionary<string, IConsumer<string, string>>` for singleton Kafka consumers keyed by consumer group ID.

### GetOrCreateKafkaConsumer

```csharp
public IConsumer<string, string> GetOrCreateKafkaConsumer(string consumerGroup)
```

Returns a cached Kafka consumer for the given consumer group. Creates one lazily on first call using the base `ConsumerConfig` (built by `NostifyFactory`) with the specified `GroupId`. Uses `ConcurrentDictionary.GetOrAdd` for thread safety.

### Dispose

```csharp
public void Dispose()
```

Disposes all cached Kafka consumers (calls `Close()` then `Dispose()` on each). Implements `IDisposable`.

## Key Method Implementations

### PersistEventAsync

Persists a single event to the Cosmos DB event store with failure observability.

Two overloads exist:

- **`PersistEventAsync(IEvent)`** — Backwards-compatible overload. Creates a `RetryOptions` instance wired with the configured logger and delegates to `PersistEventAsync(IEvent, RetryOptions?)`.
- **`PersistEventAsync(IEvent, RetryOptions?)`** — Primary implementation:

```csharp
public async Task PersistEventAsync(IEvent eventToPersist, RetryOptions? retryOptions)
{
    try
    {
        var eventContainer = await GetEventStoreContainerAsync();
        if (retryOptions != null)
        {
            // Clone options, wire in logger if not already set, then retry
            var effectiveOptions = new RetryOptions(retryOptions) { Logger = retryOptions.Logger ?? Logger };
            await eventContainer
                .WithRetry(effectiveOptions)
                .CreateItemAsync(eventToPersist, eventToPersist.aggregateRootId.ToPartitionKey());
        }
        else
        {
            await eventContainer.CreateItemAsync(eventToPersist, eventToPersist.aggregateRootId.ToPartitionKey());
        }
    }
    catch (Exception ex)
    {
        Logger?.LogError(ex, "Failed to persist event in PersistEventAsync. Event: {Event}", JsonConvert.SerializeObject(eventToPersist));
        try
        {
            await HandleUndeliverableAsync(nameof(PersistEventAsync), ex.Message, eventToPersist);
        }
        catch (Exception undeliverableEx)
        {
            // Log but do not rethrow: the original persistence exception is re-thrown below,
            // ensuring the HTTP caller receives the actual failure, not a secondary write error.
            Logger?.LogError(undeliverableEx, "Failed to write undeliverable event in PersistEventAsync");
        }
        throw;
    }
}
```

On any exception from the Cosmos write:
1. Logs the error via `Logger.LogError` (if a logger is configured).
2. Attempts to write the event to the undeliverable events container via `HandleUndeliverableAsync`. If this write also fails, the error is logged and swallowed so it cannot mask the original persistence exception.
3. Re-throws the original exception so the calling HTTP handler still returns a failure response.

### PublishEventAsync

Persists an event to Cosmos DB and publishes to Kafka:

```csharp
public async Task PublishEventAsync(Event eventToPublish)
{
    // 1. Get or create event store container
    var container = await CreateEventStoreContainerAsync();
    
    // 2. Persist event to Cosmos DB
    await container.CreateItemAsync(eventToPublish, new PartitionKey(eventToPublish.partitionKey));
    
    // 3. Publish to Kafka topic (topic name = command name)
    var kafkaMessage = new Message<string, string>
    {
        Key = eventToPublish.aggregateRootId.ToString(),
        Value = JsonConvert.SerializeObject(eventToPublish)
    };
    
    await BulkPublisher.ProduceAsync(eventToPublish.command.name, kafkaMessage);
}
```

### RehydrateAsync

Rebuilds aggregate state by replaying events:

```csharp
public async Task<A> RehydrateAsync<A>(Guid aggregateRootId, DateTime? asOfDate = null) 
    where A : NostifyObject, IAggregate, IApplyable, new()
{
    // 1. Get all events for this aggregate
    var events = await GetAllEventsAsync<A>(aggregateRootId);
    
    // 2. Filter by date if specified
    if (asOfDate.HasValue)
    {
        events = events.Where(e => e.timestamp <= asOfDate.Value).ToList();
    }
    
    // 3. Order by timestamp
    events = events.OrderBy(e => e.timestamp).ToList();
    
    // 4. Create new aggregate and apply events
    var aggregate = new A();
    foreach (var @event in events)
    {
        aggregate.Apply(@event);
    }
    
    return aggregate;
}
```

### GetContainerAsync

Gets or creates a Cosmos DB container:

```csharp
public async Task<Container> GetContainerAsync(
    string containerName, 
    string partitionKeyPath = "/tenantId", 
    bool isSagaContainer = false)
{
    var database = Repository.GetDatabase();
    
    var containerProperties = new ContainerProperties
    {
        Id = containerName,
        PartitionKeyPath = partitionKeyPath
    };
    
    // Saga containers need conflict resolution for compensation
    if (isSagaContainer)
    {
        containerProperties.ConflictResolutionPolicy = new ConflictResolutionPolicy
        {
            Mode = ConflictResolutionMode.LastWriterWins,
            ResolutionPath = "/_ts"
        };
    }
    
    var response = await database.CreateContainerIfNotExistsAsync(containerProperties);
    return response.Container;
}
```

### GetNextSequenceValueAsync

Atomically increments a sequence:

```csharp
public async Task<long> GetNextSequenceValueAsync(string sequenceName, string partitionKey)
{
    var container = await GetContainerAsync("sequences", "/partitionKey");
    var id = Sequence.GenerateId(sequenceName, partitionKey);
    
    try
    {
        // Attempt to increment existing sequence
        var response = await container.PatchItemAsync<Sequence>(
            id,
            new PartitionKey(partitionKey),
            new[] { PatchOperation.Increment("/currentValue", 1) }
        );
        return response.Resource.currentValue;
    }
    catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
        // Create new sequence starting at 1
        var sequence = new Sequence(sequenceName, partitionKey);
        await container.CreateItemAsync(sequence);
        return 1;
    }
}
```

### BulkApplyAndPersistAsync

Applies and persists a bulk array of Kafka events to projections:

- **`bool allowRetry` overload** — Backwards-compatible. Converts `allowRetry = true` to `new RetryOptions()` (defaults: maxRetries: 3, delay: 1s, exponential backoff 2x) and delegates to the `RetryOptions?` overload. When `false`, passes `null`.
- **`RetryOptions?` overload** — Primary implementation. Deserializes events, groups by partition key, resolves target projection IDs from `idPropertyName` (supports both single `Guid` and `List<Guid>` properties), and uses `CreateApplyAndPersistTask` for each item.
- Returns up to 1000 successfully applied projections.

### BulkPersistEventAsync

Persists a list of events to the event store in configurable batches:

- **`bool allowRetry` overload** — Backwards-compatible. Converts `allowRetry = true` to `new RetryOptions()` (defaults: maxRetries: 3, delay: 1s, exponential backoff 2x) and delegates to the `RetryOptions?` overload. When `false`, passes `null`.
- **`RetryOptions?` overload** — Primary implementation. When `retryOptions` is provided, wraps the event store container with `RetryableContainer` via `container.WithRetry(retryOptions)` and uses `retryable.DoBulkCreateEventAsync` for batched event persistence. When `null`, falls through to direct `CreateItemAsync` with try/catch error handling.
- Both overloads write failed events to the undeliverable events container via `await HandleUndeliverableAsync` (awaited, not fire-and-forget, so errors in undeliverable handling are propagated to the caller).
- When `publishErrorEvents = true`, error commands (`ErrorCommand.BulkPersistEvent`) are also published to Kafka.

Both `BulkApplyAndPersistAsync` and `BulkPersistEventAsync` follow the same delegation pattern: `bool` overloads create default `RetryOptions` and delegate to the `RetryOptions?` overload.

### PersistEventAsync

Persists a single event to the event store:

- **`PersistEventAsync(IEvent)`** — Backwards-compatible overload. Creates a default `RetryOptions` instance, wires logger settings, and delegates to `PersistEventAsync(IEvent, RetryOptions?)`.
- **`PersistEventAsync(IEvent, RetryOptions?)`** — Primary implementation. When `retryOptions` is provided, wraps the event store container with `RetryableContainer` for retries while still re-throwing failures after writing to the undeliverable container. When `retryOptions` is `null`, writes directly with no retry.

## Event Store Container Configuration

The event store container is created with:

- **Partition Key**: `/partitionKey` (aggregate type name)
- **TTL**: Disabled (events are permanent)
- **Indexing**: Default policy for query flexibility

## Error Handling

| Exception | Condition |
|-----------|-----------|
| `CosmosException` | Database connectivity or operation failures |
| `ProduceException<string, string>` | Kafka publishing failures |
| `NostifyException` | Invalid aggregate or event configuration |

## Thread Safety

`Nostify` is designed to be thread-safe and can be registered as a singleton in dependency injection. The underlying `CosmosClient` and Kafka `IProducer` are both thread-safe.

## Usage with Dependency Injection

```csharp
// Use NostifyFactory for proper configuration
services.AddSingleton<INostify>(sp => 
    NostifyFactory.Build(
        cosmosConnectionString: "...",
        kafkaUrl: "...",
        databaseName: "MyEventStore"
    )
);
```

## Related Types

- [INostify](INostify.spec.md) - Interface definition
- [NostifyFactory](NostifyFactory.spec.md) - Factory for creating instances
- [NostifyCosmosClient](NostifyCosmosClient.spec.md) - Cosmos DB wrapper
- [Event](Event.spec.md) - Event structure
