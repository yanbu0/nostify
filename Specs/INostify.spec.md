# INostify Interface Specification

## Overview

`INostify` is the primary interface for the nostify event-sourcing framework. It provides the main entry point for all operations including event persistence, projection management, saga orchestration, and Cosmos DB interactions.

## Interface Definition

```csharp
public interface INostify
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Repository` | `NostifyCosmosClient` | Cosmos DB client wrapper for database operations |
| `BulkPublisher` | `IProducer<string, string>` | Kafka producer for bulk event publishing |
| `DefaultUserId` | `Guid` | Default user ID for events without explicit user |
| `kafkaUrl` | `string` | Kafka broker connection URL |
| `eventStoreContainerName` | `string` | Name of the container storing all events |

## Methods

### Event Publishing

| Method | Signature | Description |
|--------|-----------|-------------|
| `PublishEventAsync` | `Task PublishEventAsync(Event eventToPublish)` | Persists event to Cosmos DB and publishes to Kafka topic |

### Event Retrieval

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetAllEventsAsync<A>` | `Task<List<Event>> GetAllEventsAsync<A>(Guid aggregateRootId) where A : IAggregate` | Retrieves all events for an aggregate root |
| `GetAllEventsAsync<A>` | `Task<List<Event>> GetAllEventsAsync<A>(List<Guid> aggregateRootIds) where A : IAggregate` | Retrieves all events for multiple aggregate roots |
| `GetEventByIdAsync<A>` | `Task<Event?> GetEventByIdAsync<A>(Guid aggregateRootId, string eventId) where A : IAggregate` | Retrieves a specific event by ID |

### Aggregate Rehydration

| Method | Signature | Description |
|--------|-----------|-------------|
| `RehydrateAsync<A>` | `Task<A> RehydrateAsync<A>(Guid aggregateRootId, DateTime? asOfDate = null) where A : NostifyObject, IAggregate, IApplyable, new()` | Rebuilds aggregate state from event stream |
| `RehydrateAsync<A>` | `Task<List<A>> RehydrateAsync<A>(List<Guid> aggregateRootIds, DateTime? asOfDate = null) where A : NostifyObject, IAggregate, IApplyable, new()` | Rebuilds multiple aggregates from event streams |

### Container Operations

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetContainerAsync` | `Task<Container> GetContainerAsync(string containerName, string partitionKeyPath = "/tenantId", bool isSagaContainer = false)` | Gets or creates a Cosmos DB container |
| `DeleteContainerAsync` | `Task DeleteContainerAsync(string containerName)` | Deletes a Cosmos DB container |
| `CreateEventStoreContainerAsync` | `Task<Container> CreateEventStoreContainerAsync()` | Creates the event store container with appropriate configuration |

### Current State Operations

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetCurrentStateAsync<A>` | `Task<A?> GetCurrentStateAsync<A>(Guid id) where A : IAggregate` | Retrieves current state from projection container |
| `GetCurrentStateAsync<A>` | `Task<List<A>> GetCurrentStateAsync<A>(List<Guid> ids) where A : IAggregate` | Retrieves current state for multiple aggregates |
| `GetAllAsync<T>` | `Task<List<T>> GetAllAsync<T>(string containerName) where T : IUniquelyIdentifiable` | Retrieves all items from a container |
| `UpsertCurrentStateAsync<A>` | `Task<A> UpsertCurrentStateAsync<A>(A aggregateState) where A : IAggregate` | Persists current state to projection container |
| `UpsertCurrentStateAsync<A>` | `Task<List<A>> UpsertCurrentStateAsync<A>(List<A> aggregateStates) where A : IAggregate` | Persists multiple current states |

### Projection Operations

| Method | Signature | Description |
|--------|-----------|-------------|
| `UpsertProjectionAsync<P>` | `Task<P> UpsertProjectionAsync<P>(P projection) where P : IProjection` | Persists a projection to its container |
| `UpsertProjectionAsync<P>` | `Task<List<P>> UpsertProjectionAsync<P>(List<P> projections) where P : IProjection` | Persists multiple projections |
| `GetProjectionAsync<P>` | `Task<P?> GetProjectionAsync<P>(Guid id) where P : IProjection` | Retrieves a projection by ID |
| `DeleteProjectionAsync<P>` | `Task DeleteProjectionAsync<P>(Guid id) where P : IProjection` | Deletes a projection by ID |

### Saga Operations

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetSagaAsync` | `Task<Saga?> GetSagaAsync(Guid sagaId)` | Retrieves a saga by ID |
| `UpsertSagaAsync` | `Task<Saga> UpsertSagaAsync(Saga saga)` | Persists a saga to the saga container |

### Sequence Operations

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetNextSequenceValueAsync` | `Task<long> GetNextSequenceValueAsync(string sequenceName, string partitionKey)` | Atomically increments and returns next sequence value |
| `GetSequenceRangeAsync` | `Task<SequenceRange> GetSequenceRangeAsync(string sequenceName, string partitionKey, int count)` | Reserves a range of sequence values |

## Usage Example

```csharp
// Inject INostify via dependency injection
public class OrderCommandHandler
{
    private readonly INostify _nostify;
    
    public OrderCommandHandler(INostify nostify)
    {
        _nostify = nostify;
    }
    
    public async Task HandleCreateOrder(CreateOrderCommand command)
    {
        // Create and publish event
        var @event = new Event(
            command,
            command.OrderId,
            command.Payload,
            command.UserId
        );
        
        await _nostify.PublishEventAsync(@event);
    }
    
    public async Task<Order> GetOrder(Guid orderId)
    {
        // Rehydrate from event stream
        return await _nostify.RehydrateAsync<Order>(orderId);
    }
}
```

## Related Types

- [Nostify](Nostify.spec.md) - Default implementation
- [NostifyFactory](NostifyFactory.spec.md) - Factory for creating instances
- [NostifyCosmosClient](NostifyCosmosClient.spec.md) - Cosmos DB client wrapper
- [Event](Event.spec.md) - Event data structure
- [Saga](Saga.spec.md) - Saga orchestration
