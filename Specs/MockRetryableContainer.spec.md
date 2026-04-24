# MockRetryableContainer Specification

## Overview

`MockRetryableContainer<T>` is a test-friendly implementation of `IRetryableContainer` that does not perform actual retries or Cosmos DB calls. It allows tests to simulate success, failure, not-found, and exhausted-retry scenarios while tracking call counts and applied events.

## Class Definition

```csharp
public class MockRetryableContainer<T> : IRetryableContainer where T : NostifyObject, new()
```

## Constructors

### Success Constructor

```csharp
public MockRetryableContainer(T successResult, Container? container = null)
```

Creates a mock that returns a successful result for all operations.

### Exception Constructor

```csharp
public MockRetryableContainer(Exception exceptionToThrow, Container? container = null)
```

Creates a mock that throws the specified exception for all operations.

### Simulation Constructor

```csharp
public MockRetryableContainer(
    bool simulateNotFound = false, 
    bool simulateExhausted = false, 
    Container? container = null)
```

Creates a mock that simulates not-found or exhausted-retry conditions.

### NotFoundUntilAttempt Constructor

```csharp
public MockRetryableContainer(
    int notFoundUntilAttempt, 
    T successResult, 
    Container? container = null)
```

Creates a mock that simulates not-found for the first N attempts, then returns success.

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Container` | `Container?` | Underlying Cosmos Container (can be null in tests) |
| `Options` | `RetryOptions` | Always returns default `RetryOptions` |
| `ApplyCallCount` | `int` | Number of ApplyAndPersist calls made |
| `ReadCallCount` | `int` | Number of ReadItem calls made |
| `AppliedEvents` | `List<Event>` | List of events passed to ApplyAndPersist |

## Methods

### ApplyAndPersistAsync

```csharp
public Task<T?> ApplyAndPersistAsync<T>(
    Event @event, Container container, 
    Func<Task>? onExhausted = null, Func<Task>? onNotFound = null, 
    Func<Exception, Task>? onException = null, Guid? projectionId = null)
```

Simulates applying an event. Increments `ApplyCallCount` and records the event in `AppliedEvents`.

**Behavior by constructor mode:**
- **Success**: Returns `successResult` cast to requested type
- **Exception**: Invokes `onException` callback or throws if no callback
- **SimulateNotFound**: Invokes `onNotFound` callback, returns null
- **SimulateExhausted**: Invokes `onExhausted` callback, returns null
- **NotFoundUntilAttempt**: Returns null until call count reaches threshold, then returns result

### ReadItemAsync

```csharp
public Task<T?> ReadItemAsync<T>(
    string id, PartitionKey partitionKey, 
    Func<Task>? onExhausted = null, Func<Task>? onNotFound = null, 
    Func<Exception, Task>? onException = null)
```

Simulates reading an item. Increments `ReadCallCount`.

### CreateItemAsync / UpsertItemAsync

```csharp
public Task<T?> CreateItemAsync<T>(T item, PartitionKey? partitionKey = null, ...)
public Task<T?> UpsertItemAsync<T>(T item, PartitionKey? partitionKey = null, ...)
```

Simulate create/upsert operations with the same behavior patterns.

### DoBulkCreateAsync / DoBulkUpsertAsync / DoBulkCreateEventAsync / DoBulkUpsertEventAsync

```csharp
public Task DoBulkCreateAsync<T>(List<T> itemList, Func<T, Exception, Task>? onException = null)
public Task DoBulkUpsertAsync<T>(List<T> itemList, Func<T, Exception, Task>? onException = null)
public Task DoBulkCreateEventAsync(List<IEvent> eventList, Func<IEvent, Exception, Task>? onException = null)
public Task DoBulkUpsertEventAsync(List<IEvent> eventList, Func<IEvent, Exception, Task>? onException = null)
```

Simulate bulk create/upsert/event-create/event-upsert operations. In exception mode, invokes `onException` callback with the first item/event and exception (when callback is provided), or throws. Otherwise completes successfully.

## Usage Examples

### Testing Successful Event Application

```csharp
var expected = new MyAggregate { id = Guid.NewGuid() };
var mock = new MockRetryableContainer<MyAggregate>(expected);
var result = await mock.ApplyAndPersistAsync<MyAggregate>(someEvent, container);
Assert.Equal(expected.id, result.id);
Assert.Equal(1, mock.ApplyCallCount);
Assert.Single(mock.AppliedEvents);
```

### Testing Exception Handling

```csharp
var mock = new MockRetryableContainer<MyAggregate>(new InvalidOperationException("fail"));
bool callbackCalled = false;
await mock.ApplyAndPersistAsync<MyAggregate>(
    someEvent, container,
    onException: async (ex) => { callbackCalled = true; });
Assert.True(callbackCalled);
```

### Testing Not-Found Scenario

```csharp
var mock = new MockRetryableContainer<MyAggregate>(simulateNotFound: true);
bool notFoundCalled = false;
await mock.ApplyAndPersistAsync<MyAggregate>(
    someEvent, container,
    onNotFound: async () => { notFoundCalled = true; });
Assert.True(notFoundCalled);
```

### Testing Eventual Consistency

```csharp
var expected = new MyAggregate { id = Guid.NewGuid() };
var mock = new MockRetryableContainer<MyAggregate>(notFoundUntilAttempt: 3, successResult: expected);
// First 2 calls return null, 3rd returns expected
```

## Key Relationships

- **IRetryableContainer**: Implements this interface for test substitution
- **RetryOptions**: Exposes default `RetryOptions` (not configurable)
- **Event**: Tracks applied events via `AppliedEvents` list
- **NostifyObject**: Generic type constraint `T : NostifyObject, new()`

## Source Files

- Implementation: `src/TestClasses/MockRetryableContainer.cs`
- Tests: `nostify.Tests/MockRetryableContainer.Tests.cs`
