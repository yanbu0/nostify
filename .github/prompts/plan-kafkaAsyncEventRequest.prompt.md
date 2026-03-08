# Plan: Kafka Async Event Request via ExternalDataEventFactory

**TL;DR**: Add Kafka-based request-response capability to `ExternalDataEventFactory` alongside the existing HTTP pattern. When `GetEventsAsync()` runs, async event requestors will produce a batch request message to `{serviceName}_EventRequest`, then use a **singleton-per-projection `IConsumer`** (keyed by `P.containerName` as consumer group) to listen on the same topic for `AsyncEventRequestResponse` messages filtered by a correlation ID. Responses may arrive in multiple chunks — the consumer keeps reading until it sees `complete = true` or the configurable timeout (default 30s, env var `AsyncEventRequestTimeoutSeconds`) expires (resetting on each matching message). The consumer reuses existing Kafka credentials from `NostifyConfig`. Subtopic is included on both request/response classes for future use but not actively used in filtering yet.

## Steps

### 1. Create `AsyncEventRequest` class
- New file `src/Projection/AsyncEventRequest.cs`
- Properties: `string topic`, `string subtopic`, `List<Guid> aggregateRootIds`, `DateTime? pointInTime`, `string correlationId` (Guid string generated per batch request for matching responses)
- Simple POCO, serializable via Newtonsoft.Json

### 2. Create `AsyncEventRequestResponse` class
- New file `src/Projection/AsyncEventRequestResponse.cs`
- Properties: `string topic`, `string subtopic`, `Guid aggregateRootId`, `bool complete`, `List<Event> events`, `string correlationId`
- The response may come in multiple messages per `correlationId`. The consumer accumulates events until `complete == true`.

### 3. Create `AsyncEventRequester<TProjection>` class
- New file `src/Projection/AsyncEventRequester.cs`
- Mirrors `EventRequester<TProjection>` but replaces `string Url` with `string ServiceName`
- Same 6 constructor overloads as `EventRequester` (Guid, Guid?, List\<Guid\>, List\<Guid?\>, mixed combos, both mixed)
- Same properties: `SingleSelectors`, `ListSelectors`, `ForeignIdSelectors`, same `GetAllForeignIdSelectors()` method
- Topic name derived as `{ServiceName}_EventRequest`

### 4. Add consumer infrastructure to `INostify` / `Nostify`
- Add to `INostify`: `IConsumer<string, string> GetOrCreateKafkaConsumer(string consumerGroup)` — returns a singleton consumer for the given consumer group, creating it on first call
- Add to `Nostify`:
  - Private `ConcurrentDictionary<string, IConsumer<string, string>> _kafkaConsumers` cache
  - Private base `ConsumerConfig _baseConsumerConfig` (built from existing producer credentials in `WithKafka()`/`WithEventHubs()`, WITHOUT `GroupId` set)
  - `GetOrCreateKafkaConsumer(string consumerGroup)`: check cache → if miss, clone base config + set `GroupId = consumerGroup`, build consumer via `ConsumerBuilder`, add to cache and return
  - Implement `IDisposable` pattern: in `Dispose()`, iterate `_kafkaConsumers` and dispose each consumer, then clear the dictionary
- In `NostifyFactory.WithKafka()` and `WithEventHubs()`: build a base `ConsumerConfig` alongside the existing `ProducerConfig`, mirroring SASL settings. Set `AutoOffsetReset = AutoOffsetReset.Latest`, `EnableAutoCommit = true`. Do NOT set `GroupId` (set dynamically per consumer)
- Callers pass `P.containerName` as the consumer group — this means one consumer per projection, stable group name, no orphaned groups

### 5. Add `WithAsyncEventRequestor` methods to `ExternalDataEventFactory<P>`
- New private fields: `AsyncEventRequester<P>[] _asyncEventRequestors`, `AsyncEventRequester<P>[] _dependantAsyncEventRequestors`
- **6 overloads** mirroring `EventRequester` constructors:
  - `WithAsyncEventRequestor(string serviceName, params Func<P, Guid?>[] foreignIdSelectors)`
  - `WithAsyncEventRequestor(string serviceName, params Func<P, Guid>[] selectors)`
  - `WithAsyncEventRequestor(string serviceName, params Func<P, List<Guid?>>[] selectors)`
  - `WithAsyncEventRequestor(string serviceName, params Func<P, List<Guid>>[] selectors)`
  - `WithAsyncEventRequestor(string serviceName, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)`
  - `WithAsyncEventRequestor(string serviceName, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)`
- Same 6 overloads for `WithDependantAsyncEventRequestor`
- Each creates an `AsyncEventRequester<P>` and appends to the respective array
- Fluent return `this`

### 6. Add `AddAsyncEventRequestors` and `AddDependantAsyncEventRequestors` methods
- Mirror existing `AddEventRequestors()` / `AddDependantEventRequestors()` pattern
- Accept `params AsyncEventRequester<P>[]`
- No HttpClient check needed (Kafka, not HTTP)

### 7. Implement Kafka request-response in `GetEventsAsync()`
- After the existing external HTTP requestor block and before dependant selectors, add a new block for async requestors:
  1. For each `AsyncEventRequester`, collect all foreign IDs using `GetAllForeignIdSelectors()`
  2. Generate a `correlationId` (Guid) per requestor
  3. **Get the singleton consumer** via `_nostify.GetOrCreateKafkaConsumer(P.containerName)` — this returns the cached consumer or creates one on first use
  4. **Subscribe to the topic** if not already subscribed: maintain a `HashSet<string>` of subscribed topics within the consumer cache logic (or check consumer's `Subscription` property). Subscribe to `{serviceName}_EventRequest` topic
  5. **Produce** the `AsyncEventRequest` message to `{serviceName}_EventRequest` via `_nostify.KafkaProducer`
     - Since the consumer is a long-lived singleton that's already subscribed and has partition assignments, there's no race condition — it will see messages produced after the subscription
  6. Poll for `AsyncEventRequestResponse` messages matching the `correlationId`
  7. Accumulate `events` from each response message
  8. Continue until `complete == true` or timeout (read from env var `AsyncEventRequestTimeoutSeconds`, default 30s, resetting on each matching message)
  9. Map accumulated events back to projections (same pattern as HTTP: group by `aggregateRootId`, match to projection via selectors)
  10. **Do NOT dispose the consumer** — it's a singleton, reused across calls
- For dependant async requestors: same pattern but in the dependant section (after applying initial events), paralleling the existing `GetDependantExternalEventsAsync` flow

### 8. Extract request-response helper method
- Create a private helper `GetAsyncEventsAsync(AsyncEventRequester<P>[] requestors, List<P> projections)` to avoid duplicating the produce-consume logic between primary and dependant requestors
- This method handles: ID collection, producing requests, consuming responses, timeout, mapping to `ExternalDataEvent`

### 9. Update `ExternalDataEventFactory` constructor
- No HttpClient requirement for async requestors (Kafka only)
- No constructor changes needed — `_nostify` already provides access to Kafka producer and the new consumer config

### 10. Read timeout from environment
- In the request-response helper, read `AsyncEventRequestTimeoutSeconds` from `Environment.GetEnvironmentVariable("AsyncEventRequestTimeoutSeconds")`
- Parse as int, default to 30 if missing or invalid
- Convert to `TimeSpan` for use with consumer poll timeout and overall deadline

### 11. Update specs
- Update `Specs/ExternalDataEventFactory.spec.md` with new methods, execution order, and Kafka request-response behavior
- Create `Specs/AsyncEventRequest.spec.md`
- Create `Specs/AsyncEventRequestResponse.spec.md`
- Create `Specs/AsyncEventRequester.spec.md`
- Create `Specs/AsyncEventRequestHandler.spec.md` — documents the template Kafka trigger handler, chunking logic, topic naming, and error handling
- Update `Specs/INostify.spec.md` for `KafkaConsumerConfig`
- Update `Specs/Nostify.spec.md` for consumer config storage

### 12. Update README
- Add 4.5.0 changelog entry documenting the new Kafka async event request feature
- Add usage examples for `WithAsyncEventRequestor` and `WithDependantAsyncEventRequestor`

### 13. Write unit tests
- New file `nostify.Tests/AsyncEventRequester.Tests.cs` — test all 6 constructor overloads, `GetAllForeignIdSelectors()`, topic name derivation
- New file `nostify.Tests/AsyncEventRequest.Tests.cs` — serialization round-trip, property defaults
- New file `nostify.Tests/AsyncEventRequestResponse.Tests.cs` — serialization, `complete` flag behavior, chunking helper (`ChunkEvents` tests with various event list sizes and max byte limits)
- Expand `nostify.Tests/ExternalDataEventFactory.Tests.cs` — test all `WithAsyncEventRequestor` / `WithDependantAsyncEventRequestor` overloads, fluent chaining, verify requestors are stored correctly
- Integration test for `GetEventsAsync()` with async requestors would require a Kafka mock — consider whether to add a `IKafkaConsumerFactory` abstraction for testability or skip integration tests for now
- Template handler (`AsyncEventRequestHandler`) is tested via template validation (generate service, build, verify compilation)

### 14. Update NostifyFactory settings
- Ensure `WithKafka()` and `WithEventHubs()` both build `ConsumerConfig` alongside `ProducerConfig`
- `Build()` / `Build<T>()` pass the `ConsumerConfig` to the `Nostify` constructor

### 15. Create `AsyncEventRequest` Kafka trigger handler in template
- New file `templates/nostify/_ReplaceMe_/Events/AsyncEventRequestHandler.cs`
- Mirrors the existing `Events/EventRequest.cs` (which handles HTTP event requests) but binds to a Kafka trigger instead
- **Kafka trigger binding**: Listens on `_ReplaceMe__EventRequest` topic (matching the `{serviceName}_EventRequest` convention)
  - Uses the same `#if (eventHubs)` / `#else` conditional compilation pattern as existing Kafka triggers (e.g., `On_ReplaceMe_Created.cs`)
  - ConsumerGroup: `_ReplaceMe__AsyncEventRequest`
- **Handler logic**:
  1. Deserialize the `NostifyKafkaTriggerEvent.Value` as `AsyncEventRequest`
  2. Extract `aggregateRootIds`, `pointInTime`, and `correlationId` from the request
  3. Query the event store container for events matching the `aggregateRootIds` (same query pattern as `EventRequest.cs`), filtered by `pointInTime` if provided
  4. **Chunk the response**: Kafka has a default max message size of ~1MB. Divide the event list into chunks that fit within a configurable max size (env var `AsyncEventRequestMaxMessageBytes`, default ~900KB to leave headroom for headers/serialization overhead)
     - Serialize events in batches, checking serialized size against the limit
     - Each chunk becomes an `AsyncEventRequestResponse` with `complete = false`
     - The final chunk sets `complete = true`
  5. Produce each `AsyncEventRequestResponse` message back to the same `_ReplaceMe__EventRequest` topic via `_nostify.KafkaProducer`
  6. If the query returns zero events, still produce a single response with `complete = true` and an empty events list (so the consumer doesn't hang waiting)
- **Chunking helper**: Add a static method `ChunkEvents(List<Event> events, int maxBytes)` to `AsyncEventRequestResponse` (or as a utility method) that splits an event list into serialization-safe chunks
- **Error handling**: If the query fails, produce a response with `complete = true` and empty events list, log the error

### 16. Version bump consideration
- Update version to 4.5.0 (new feature) if desired — or leave for user to decide

## Verification
- `dotnet build` — 0 errors
- `dotnet test nostify.Tests/` — all tests pass (existing 841 + new tests)
- Template validation: `dotnet pack`, install template, generate test service, `dotnet build` — verify `AsyncEventRequestHandler.cs` compiles with Kafka trigger binding
- Manual verification: inspect that `WithAsyncEventRequestor` correctly stores `AsyncEventRequester` instances and that `GetEventsAsync()` produces messages to the correct topic format

## Decisions
- **Singleton-per-projection consumer**: `GetOrCreateKafkaConsumer(P.containerName)` returns a cached `IConsumer` keyed by container name — one consumer per projection, stable group, no orphaned groups
- **Same topic**: Both request and response use `{serviceName}_EventRequest`, distinguished by message type (request vs response serialization)
- **Batch request**: `AsyncEventRequest` holds `List<Guid> aggregateRootIds` (not one message per ID)
- **Subtopic**: Included in both classes for future use, not actively filtered on
- **Correlation ID**: Random Guid per request, used to match responses
- **Timeout from env var**: `AsyncEventRequestTimeoutSeconds`, default 30s, resets on each matching message
- **Consumer auth**: Reuses existing Kafka credentials from `NostifyConfig`
- **Consumer lifetime**: Singleton per projection, created lazily on first use, cached in `ConcurrentDictionary`, disposed when `Nostify` is disposed
- **Full overloads**: 6 `WithAsyncEventRequestor` + 6 `WithDependantAsyncEventRequestor` overloads matching `EventRequester` constructor patterns
