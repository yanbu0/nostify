# Nostify Framework Specification Index

## Overview

Nostify is an event-sourcing microservices framework for .NET 10 with Azure Cosmos DB and Apache Kafka integration.

## Core Components

### Main Entry Points

| Spec | Description |
|------|-------------|
| [INostify](INostify.spec.md) | Primary interface for all nostify operations |
| [Nostify](Nostify.spec.md) | Default implementation of INostify |
| [NostifyFactory](NostifyFactory.spec.md) | Factory for creating INostify instances |

### Base Classes

| Spec | Description |
|------|-------------|
| [NostifyObject](NostifyObject.spec.md) | Abstract base class for all domain objects |
| [NostifyCommand](NostifyCommand.spec.md) | Base class for commands |
| [NostifyCosmosClient](NostifyCosmosClient.spec.md) | Cosmos DB client wrapper |

## Event Module

| Spec | Description |
|------|-------------|
| [IEvent](IEvent.spec.md) | Interface for events |
| [Event](Event.spec.md) | Event implementation |
| [NostifyKafkaTriggerEvent](NostifyKafkaTriggerEvent.spec.md) | Kafka trigger wrapper |
| [EventFactory](EventFactory.spec.md) | Factory for creating events |
| [EventRequester](EventRequester.spec.md) | External service event configuration |

## Projection Module

| Spec | Description |
|------|-------------|
| [IProjection](IProjection.spec.md) | Interface for projections |
| [IProjectionInitializer](IProjectionInitializer.spec.md) | Projection initialization interface |
| [ExternalDataEventFactory](ExternalDataEventFactory.spec.md) | Fluent builder for external data events |
| [ExternalDataEvent](ExternalDataEvent.spec.md) | Container for external data events |

## Saga Module

| Spec | Description |
|------|-------------|
| [ISaga](ISaga.spec.md) | Interface for saga orchestration |
| [Saga](Saga.spec.md) | Saga implementation |
| [ISagaStep](ISagaStep.spec.md) | Interface for saga steps |
| [SagaStep](SagaStep.spec.md) | Saga step implementation |

## Query & Paging

| Spec | Description |
|------|-------------|
| [PagedQuery](PagedQuery.spec.md) | Paged query system |
| [IQueryExecutor](IQueryExecutor.spec.md) | Query execution interface |
| [CosmosQueryExecutor](CosmosQueryExecutor.spec.md) | Cosmos DB query executor |
| [Sequence](Sequence.spec.md) | Atomic sequence generation |

## Validation

| Spec | Description |
|------|-------------|
| [RequiredForAttribute](RequiredForAttribute.spec.md) | Command-specific validation |
| [INostifyValidation](INostifyValidation.spec.md) | Validation interface |
| [NostifyValidation](NostifyValidation.spec.md) | Validation utilities |

## Error Handling

| Spec | Description |
|------|-------------|
| [NostifyException](NostifyException.spec.md) | Base exception class |
| [NostifyValidationException](NostifyValidationException.spec.md) | Validation exception |
| [NostifyValidationExceptionMiddleware](NostifyValidationExceptionMiddleware.spec.md) | Azure Functions middleware |

## Shared Interfaces

| Spec | Description |
|------|-------------|
| [IUniquelyIdentifiable](IUniquelyIdentifiable.spec.md) | Base interface with id property |
| [IAggregate](IAggregate.spec.md) | Aggregate root interface |
| [IApplyable](IApplyable.spec.md) | Event application interface |
| [ITenantFilterable](ITenantFilterable.spec.md) | Multi-tenant support |

## Extensions & Serialization

| Spec | Description |
|------|-------------|
| [NostifyExtensions](NostifyExtensions.spec.md) | Extension methods |
| [NewtonsoftJsonCosmosSerializer](NewtonsoftJsonCosmosSerializer.spec.md) | Custom JSON serializer |

## Quick Start

```csharp
// 1. Create nostify instance
var nostify = NostifyFactory.Build(
    cosmosConnectionString: "...",
    kafkaUrl: "localhost:9092",
    databaseName: "MyEventStore"
);

// 2. Define an aggregate
public class Order : NostifyObject, IAggregate, IApplyable
{
    public static string aggregateType => "Order";
    public static string currentStateContainerName => "orders";
    public bool isDeleted { get; set; }
    public decimal Total { get; set; }
    
    public void Apply(Event @event) => @event.ApplyTo(this);
}

// 3. Publish events
var @event = new Event(
    NostifyCommand.Create("Order"),
    orderId,
    new { Total = 99.99m },
    userId
);
await nostify.PublishEventAsync(@event);

// 4. Rehydrate state
var order = await nostify.RehydrateAsync<Order>(orderId);
```

## Version History

- **4.3.0** - Added nullable Guid? selector support in ExternalDataEventFactory, fluent API chaining
- **4.2.1** - Previous stable release

## Related Documentation

- [README.md](../README.md) - Project overview and getting started
- [GitHub Repository](https://github.com/yanbu0/nostify) - Source code
