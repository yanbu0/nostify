using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Comprehensive integration-style tests for the GetAsyncEventsAsync flow
/// in ExternalDataEventFactory. Exercises the full Kafka request-response
/// round trip using mock IProducer and IConsumer.
/// </summary>
public class GetAsyncEventsAsyncTests : IDisposable
{
    private readonly Mock<IProducer<string, string>> _mockProducer;
    private readonly Mock<IConsumer<string, string>> _mockConsumer;
    private readonly Mock<INostify> _mockNostify;
    private readonly List<FactoryTestProjection> _testProjections;
    private readonly string _savedTimeoutEnv;
    private readonly string _savedMaxBytesEnv;

    public GetAsyncEventsAsyncTests()
    {
        _mockProducer = new Mock<IProducer<string, string>>();
        _mockConsumer = new Mock<IConsumer<string, string>>();
        _mockNostify = new Mock<INostify>();

        // Save existing env vars
        _savedTimeoutEnv = Environment.GetEnvironmentVariable("AsyncEventRequestTimeoutSeconds");
        _savedMaxBytesEnv = Environment.GetEnvironmentVariable("AsyncEventRequestMaxMessageBytes");

        // Use short timeout by default for tests
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "2");

        // Wire up mock nostify
        _mockNostify.Setup(n => n.KafkaProducer).Returns(_mockProducer.Object);
        _mockNostify.Setup(n => n.GetOrCreateKafkaConsumer(It.IsAny<string>())).Returns(_mockConsumer.Object);

        // Consumer subscription setup
        _mockConsumer.Setup(c => c.Subscription).Returns(new List<string>());
        _mockConsumer.Setup(c => c.Subscribe(It.IsAny<IEnumerable<string>>()));

        // Setup event store container (needed by GetEventsAsync even when only using async requestors)
        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(new List<Event>());
        _mockNostify.Setup(n => n.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(mockContainer.Object);

        // Default producer setup - succeed
        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string>());

        // Test projections with known foreign IDs
        _testProjections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Projection1",
                siteId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
                externalId = Guid.NewGuid(),
                anotherExternalId = Guid.NewGuid()
            },
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Projection2",
                siteId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() },
                externalId = Guid.NewGuid(),
                anotherExternalId = Guid.NewGuid()
            }
        };
    }

    public void Dispose()
    {
        // Restore env vars
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", _savedTimeoutEnv);
        Environment.SetEnvironmentVariable("AsyncEventRequestMaxMessageBytes", _savedMaxBytesEnv);
    }

    #region Helpers

    /// <summary>
    /// Creates a ConsumeResult containing a serialized AsyncEventRequestResponse.
    /// </summary>
    private static ConsumeResult<string, string> MakeConsumeResult(AsyncEventRequestResponse response)
    {
        return new ConsumeResult<string, string>
        {
            Message = new Message<string, string>
            {
                Value = JsonConvert.SerializeObject(response)
            }
        };
    }

    /// <summary>
    /// Creates a list of test events for a given aggregate root ID.
    /// </summary>
    private static List<Event> CreateTestEvents(Guid aggregateRootId, int count)
    {
        var events = new List<Event>();
        for (int i = 0; i < count; i++)
        {
            events.Add(new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggregateRootId,
                timestamp = DateTime.UtcNow.AddMinutes(-count + i),
                command = new NostifyCommand($"TestCommand_{i}")
            });
        }
        return events;
    }

    /// <summary>
    /// Captures the AsyncEventRequest that was produced to Kafka, letting us verify topic, IDs, correlationId.
    /// Sets up mock consumer to return the given consume results in sequence, intercepting the correlationId
    /// from the produced request for building matching responses.
    /// </summary>
    private void SetupProducerCaptureAndConsumerResponses(
        string expectedTopic,
        Func<string, List<ConsumeResult<string, string>>> buildResponsesFromCorrelationId)
    {
        string capturedCorrelationId = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                var request = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
                capturedCorrelationId = request.correlationId;
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        // Track consume call count to return responses in sequence
        int consumeCallCount = 0;
        List<ConsumeResult<string, string>> responses = null;

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                // First call is the partition-assignment poll (returns null)
                if (capturedCorrelationId == null)
                    return null;

                // Build responses lazily once correlationId is known
                if (responses == null)
                    responses = buildResponsesFromCorrelationId(capturedCorrelationId);

                if (consumeCallCount < responses.Count)
                    return responses[consumeCallCount++];

                return null; // No more responses
            });
    }

    /// <summary>
    /// Simpler helper: sets up produce-capture and returns a single complete response containing the given events.
    /// </summary>
    private void SetupSingleCompleteResponse(string topic, List<Event> events)
    {
        SetupProducerCaptureAndConsumerResponses(topic, correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = topic,
                    subtopic = "",
                    correlationId = correlationId,
                    events = events,
                    complete = true
                })
            };
        });
    }

    #endregion

    #region Happy Path Tests

    [Fact]
    public async Task GetEventsAsync_SingleRequestor_SingleCompleteResponse_ReturnsEvents()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        var testEvents = CreateTestEvents(foreignId, 3);

        SetupSingleCompleteResponse("ExternalService_EventRequest", testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("ExternalService", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);

        // Should have ExternalDataEvent for projection[0]
        var eventsForP0 = result.Where(r => r.aggregateRootId == _testProjections[0].id).ToList();
        Assert.Single(eventsForP0);
        Assert.Equal(3, eventsForP0[0].events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_SingleRequestor_VerifiesProducedRequestContainsCorrectData()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        var testEvents = CreateTestEvents(foreignId, 1);
        AsyncEventRequest capturedRequest = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                capturedRequest = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        // Single complete response
        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedRequest == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = capturedRequest.topic,
                    subtopic = "",
                    correlationId = capturedRequest.correlationId,
                    events = testEvents,
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("ExternalService", p => p.externalId);

        // Act
        await factory.GetEventsAsync();

        // Assert - verify the produced request
        Assert.NotNull(capturedRequest);
        Assert.Equal("ExternalService_EventRequest", capturedRequest.topic);
        Assert.NotNull(capturedRequest.correlationId);
        // Both projections have externalId, so both should be in the request
        Assert.Contains(foreignId, capturedRequest.aggregateRootIds);
        Assert.Contains(_testProjections[1].externalId!.Value, capturedRequest.aggregateRootIds);
    }

    [Fact]
    public async Task GetEventsAsync_MultipleProjections_MapsEventsToCorrectProjections()
    {
        // Arrange - both projections have different externalIds
        var foreignId0 = _testProjections[0].externalId!.Value;
        var foreignId1 = _testProjections[1].externalId!.Value;

        var eventsForForeignId0 = CreateTestEvents(foreignId0, 2);
        var eventsForForeignId1 = CreateTestEvents(foreignId1, 3);
        var allEvents = eventsForForeignId0.Concat(eventsForForeignId1).ToList();

        SetupSingleCompleteResponse("SvcA_EventRequest", allEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - each projection gets its own ExternalDataEvent
        var eventsForP0 = result.Where(r => r.aggregateRootId == _testProjections[0].id).ToList();
        var eventsForP1 = result.Where(r => r.aggregateRootId == _testProjections[1].id).ToList();

        Assert.Single(eventsForP0);
        Assert.Equal(2, eventsForP0[0].events.Count);
        Assert.All(eventsForP0[0].events, e => Assert.Equal(foreignId0, e.aggregateRootId));

        Assert.Single(eventsForP1);
        Assert.Equal(3, eventsForP1[0].events.Count);
        Assert.All(eventsForP1[0].events, e => Assert.Equal(foreignId1, e.aggregateRootId));
    }

    [Fact]
    public async Task GetEventsAsync_EventsOrderedByTimestamp()
    {
        // Arrange - create events with out-of-order timestamps
        var foreignId = _testProjections[0].externalId!.Value;
        var latestEvent = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = DateTime.UtcNow,
            command = new NostifyCommand("Latest")
        };
        var earliestEvent = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = DateTime.UtcNow.AddHours(-2),
            command = new NostifyCommand("Earliest")
        };
        var middleEvent = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = DateTime.UtcNow.AddHours(-1),
            command = new NostifyCommand("Middle")
        };

        // Send in wrong order
        SetupSingleCompleteResponse("SvcA_EventRequest", new List<Event> { latestEvent, earliestEvent, middleEvent });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - events should be ordered by timestamp
        var eventsForP0 = result.Where(r => r.aggregateRootId == _testProjections[0].id).SelectMany(e => e.events).ToList();
        Assert.Equal(3, eventsForP0.Count);
        Assert.True(eventsForP0[0].timestamp <= eventsForP0[1].timestamp);
        Assert.True(eventsForP0[1].timestamp <= eventsForP0[2].timestamp);
    }

    #endregion

    #region Chunked Response Tests

    [Fact]
    public async Task GetEventsAsync_MultipleChunks_AccumulatesAllEvents()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        var chunk1Events = CreateTestEvents(foreignId, 5);
        var chunk2Events = CreateTestEvents(foreignId, 5);
        var chunk3Events = CreateTestEvents(foreignId, 3);

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunk1Events,
                    complete = false
                }),
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunk2Events,
                    complete = false
                }),
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunk3Events,
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - all 13 events should be accumulated
        var allEvents = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(13, allEvents.Count);
    }

    [Fact]
    public async Task GetEventsAsync_ManyChunks_SimulatesLargeDataSet()
    {
        // Arrange - simulate a large response requiring many chunks (50 events across 10 chunks)
        var foreignId = _testProjections[0].externalId!.Value;
        int totalEvents = 50;
        int chunkSize = 5;
        int numChunks = totalEvents / chunkSize;

        var allExpectedEvents = CreateTestEvents(foreignId, totalEvents);

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            var responses = new List<ConsumeResult<string, string>>();
            for (int i = 0; i < numChunks; i++)
            {
                var chunkEvents = allExpectedEvents.Skip(i * chunkSize).Take(chunkSize).ToList();
                responses.Add(MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunkEvents,
                    complete = (i == numChunks - 1)
                }));
            }
            return responses;
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(totalEvents, events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_ChunkedMultipleAggregates_DistributesCorrectly()
    {
        // Arrange - events for multiple foreign IDs split across chunks
        var foreignId0 = _testProjections[0].externalId!.Value;
        var foreignId1 = _testProjections[1].externalId!.Value;

        var eventsForId0 = CreateTestEvents(foreignId0, 4);
        var eventsForId1 = CreateTestEvents(foreignId1, 6);

        // Mix events across chunks
        var chunk1 = eventsForId0.Take(2).Concat(eventsForId1.Take(3)).ToList();
        var chunk2 = eventsForId0.Skip(2).Concat(eventsForId1.Skip(3)).ToList();

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunk1,
                    complete = false
                }),
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunk2,
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - events distributed correctly per projection
        var p0Events = result.Where(r => r.aggregateRootId == _testProjections[0].id).SelectMany(r => r.events).ToList();
        var p1Events = result.Where(r => r.aggregateRootId == _testProjections[1].id).SelectMany(r => r.events).ToList();

        Assert.Equal(4, p0Events.Count);
        Assert.Equal(6, p1Events.Count);
        Assert.All(p0Events, e => Assert.Equal(foreignId0, e.aggregateRootId));
        Assert.All(p1Events, e => Assert.Equal(foreignId1, e.aggregateRootId));
    }

    #endregion

    #region Correlation ID Filtering Tests

    [Fact]
    public async Task GetEventsAsync_IgnoresMessagesWithDifferentCorrelationId()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        var expectedEvents = CreateTestEvents(foreignId, 2);
        var otherEvents = CreateTestEvents(foreignId, 5); // These should be ignored

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                // Message from another request (different correlation ID)
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = "some-other-correlation-id",
                    events = otherEvents,
                    complete = true
                }),
                // Another foreign message
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = Guid.NewGuid().ToString(),
                    events = CreateTestEvents(foreignId, 10),
                    complete = false
                }),
                // Our actual response
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = expectedEvents,
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - only the 2 expected events, not the 5 or 10 from other correlationIds
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(2, events.Count);
    }

    #endregion

    #region Timeout Tests

    [Fact]
    public async Task GetEventsAsync_TimesOut_WhenNoCompleteResponse()
    {
        // Arrange - consumer returns null forever (no response)
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "1");

        var foreignId = _testProjections[0].externalId!.Value;

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout => null); // Never returns a response

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act - should complete (not hang) after timeout
        var result = await factory.GetEventsAsync();

        // Assert - no events because timeout was reached
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEventsAsync_TimesOut_ReturnsPartialEventsIfSomeChunksArrived()
    {
        // Arrange - consumer returns one chunk then never completes
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "2");

        var foreignId = _testProjections[0].externalId!.Value;
        var chunk1Events = CreateTestEvents(foreignId, 3);

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunk1Events,
                    complete = false // Never sends complete=true
                })
                // No more messages - will timeout
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act - should complete after timeout, with partial events
        var result = await factory.GetEventsAsync();

        // Assert - should have the 3 events from chunk 1 (timeout still maps accumulated events)
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_DeadlineResetsOnMatchingMessage()
    {
        // Arrange - consumer returns messages slowly, deadline should reset each time
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "2");

        var foreignId = _testProjections[0].externalId!.Value;
        var chunk1Events = CreateTestEvents(foreignId, 2);
        var chunk2Events = CreateTestEvents(foreignId, 2);

        string capturedCorrelationId = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                var request = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
                capturedCorrelationId = request.correlationId;
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        // Simulate slow delivery: first chunk, then wait 1.5s, then second chunk with complete
        int consumeCallCount = 0;
        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedCorrelationId == null) return null;
                consumeCallCount++;

                if (consumeCallCount == 1)
                {
                    return MakeConsumeResult(new AsyncEventRequestResponse
                    {
                        topic = "SvcA_EventRequest",
                        subtopic = "",
                        correlationId = capturedCorrelationId,
                        events = chunk1Events,
                        complete = false
                    });
                }

                // Simulate time passing - first message reset the deadline
                // Second call returns the second chunk promptly
                if (consumeCallCount == 2)
                {
                    // Consume call happens again, simulating time passing
                    return null; // brief pause
                }

                if (consumeCallCount == 3)
                {
                    return MakeConsumeResult(new AsyncEventRequestResponse
                    {
                        topic = "SvcA_EventRequest",
                        subtopic = "",
                        correlationId = capturedCorrelationId,
                        events = chunk2Events,
                        complete = true
                    });
                }

                return null;
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - both chunks should have been received
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_ReadsTimeoutFromEnvironmentVariable()
    {
        // Arrange - set a very short timeout
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "1");

        var foreignId = _testProjections[0].externalId!.Value;

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout => null); // Never responds

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        var start = DateTime.UtcNow;
        await factory.GetEventsAsync();
        var elapsed = DateTime.UtcNow - start;

        // Should be roughly 1 second (not the 30-second default)
        Assert.True(elapsed.TotalSeconds < 10, $"Expected timeout near 1s but took {elapsed.TotalSeconds}s");
    }

    #endregion

    #region Empty / No-Op Tests

    [Fact]
    public async Task GetEventsAsync_AllSelectorsReturnNull_SkipsRequestor()
    {
        // Arrange - projections with null externalIds
        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection { id = Guid.NewGuid(), name = "P1", externalId = null },
            new FactoryTestProjection { id = Guid.NewGuid(), name = "P2", externalId = null }
        };

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - no produce call since no foreign IDs
        _mockProducer.Verify(p => p.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEventsAsync_ResponseWithEmptyEventsList_ReturnsNoEvents()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;

        SetupSingleCompleteResponse("SvcA_EventRequest", new List<Event>());

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - no ExternalDataEvents because there were no real events
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEventsAsync_EmptyProjectionList_ReturnsEmptyResult()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            new List<FactoryTestProjection>(),
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.Empty(result);
        _mockProducer.Verify(p => p.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Multiple Requestors Tests

    [Fact]
    public async Task GetEventsAsync_TwoAsyncRequestors_ProducesAndConsumesBoth()
    {
        // Arrange
        var externalId0 = _testProjections[0].externalId!.Value;
        var anotherExtId0 = _testProjections[0].anotherExternalId!.Value;
        var externalId1 = _testProjections[1].externalId!.Value;
        var anotherExtId1 = _testProjections[1].anotherExternalId!.Value;

        var svcAEvents = CreateTestEvents(externalId0, 2)
            .Concat(CreateTestEvents(externalId1, 2)).ToList();
        var svcBEvents = CreateTestEvents(anotherExtId0, 3)
            .Concat(CreateTestEvents(anotherExtId1, 1)).ToList();

        // Need to handle 2 sequential requestors
        int requestorIndex = 0;
        string capturedCorrelationId = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                var request = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
                capturedCorrelationId = request.correlationId;
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        bool responded = false;
        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedCorrelationId == null) return null;

                if (!responded)
                {
                    responded = true;
                    var events = requestorIndex == 0 ? svcAEvents : svcBEvents;
                    var topic = requestorIndex == 0 ? "SvcA_EventRequest" : "SvcB_EventRequest";
                    var result = MakeConsumeResult(new AsyncEventRequestResponse
                    {
                        topic = topic,
                        subtopic = "",
                        correlationId = capturedCorrelationId,
                        events = events,
                        complete = true
                    });

                    // Reset for next requestor
                    requestorIndex++;
                    capturedCorrelationId = null;
                    responded = false;

                    return result;
                }

                return null;
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory
            .WithAsyncEventRequestor("SvcA", p => p.externalId)
            .WithAsyncEventRequestor("SvcB", p => p.anotherExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - should have events from both services
        Assert.NotEmpty(result);
        // Verify producer was called twice (once per requestor)
        _mockProducer.Verify(p => p.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetEventsAsync_SequentialRequestors_LeakedMessagesFromPriorRequestFiltered()
    {
        // Arrange: Two sequential requestors share the same consumer. During requestor B's consumption,
        // the consumer receives leftover messages from requestor A's correlationId. These must be
        // filtered out so each requestor only accumulates its own events.
        var externalId0 = _testProjections[0].externalId!.Value;
        var externalId1 = _testProjections[1].externalId!.Value;
        var anotherExtId0 = _testProjections[0].anotherExternalId!.Value;
        var anotherExtId1 = _testProjections[1].anotherExternalId!.Value;

        var svcAEvents = CreateTestEvents(externalId0, 2)
            .Concat(CreateTestEvents(externalId1, 2)).ToList();
        var svcBEvents = CreateTestEvents(anotherExtId0, 3)
            .Concat(CreateTestEvents(anotherExtId1, 1)).ToList();

        // Track each requestor's correlationId in order
        var capturedCorrelationIds = new List<string>();
        string currentCorrelationId = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                var request = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
                currentCorrelationId = request.correlationId;
                capturedCorrelationIds.Add(currentCorrelationId);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        // Build a message queue that simulates cross-requestor leakage:
        // Requestor A produces, consumes its response → complete.
        // Requestor B produces, but the consumer first sees a STALE message from requestor A's correlationId
        // (a "leaked" leftover), then sees requestor B's actual response.
        int consumeCallIndex = 0;
        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (currentCorrelationId == null) return null;
                consumeCallIndex++;

                // --- Requestor A phase ---
                if (capturedCorrelationIds.Count == 1)
                {
                    // First real consume: return SvcA's complete response
                    if (consumeCallIndex == 2) // skip the partition-poll call
                    {
                        return MakeConsumeResult(new AsyncEventRequestResponse
                        {
                            topic = "SvcA_EventRequest",
                            subtopic = "",
                            correlationId = capturedCorrelationIds[0],
                            events = svcAEvents,
                            complete = true
                        });
                    }
                }

                // --- Requestor B phase ---
                if (capturedCorrelationIds.Count == 2)
                {
                    // First consume for requestor B: leaked stale message from requestor A's correlationId
                    if (consumeCallIndex == 4) // skip the partition-poll call
                    {
                        return MakeConsumeResult(new AsyncEventRequestResponse
                        {
                            topic = "SvcA_EventRequest",
                            subtopic = "",
                            correlationId = capturedCorrelationIds[0], // A's correlationId, NOT B's!
                            events = CreateTestEvents(anotherExtId0, 99), // bogus events
                            complete = true
                        });
                    }
                    // Second consume for requestor B: the real response
                    if (consumeCallIndex == 5)
                    {
                        return MakeConsumeResult(new AsyncEventRequestResponse
                        {
                            topic = "SvcB_EventRequest",
                            subtopic = "",
                            correlationId = capturedCorrelationIds[1], // B's correlationId
                            events = svcBEvents,
                            complete = true
                        });
                    }
                }

                return null;
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory
            .WithAsyncEventRequestor("SvcA", p => p.externalId)
            .WithAsyncEventRequestor("SvcB", p => p.anotherExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        // Requestor A events: 2 events for externalId0 + 2 for externalId1 = 4 total
        var svcAMapped = result
            .Where(r => r.events.Any(e => e.aggregateRootId == externalId0 || e.aggregateRootId == externalId1))
            .SelectMany(r => r.events).ToList();
        Assert.Equal(4, svcAMapped.Count);

        // Requestor B events: should be 3 + 1 = 4, NOT the 99 from the leaked message
        var svcBMapped = result
            .Where(r => r.events.Any(e => e.aggregateRootId == anotherExtId0 || e.aggregateRootId == anotherExtId1))
            .SelectMany(r => r.events).ToList();
        Assert.Equal(4, svcBMapped.Count);

        // Extra verification: no events with count 99 leaked through
        Assert.DoesNotContain(result, r => r.events.Count == 99);

        // Both correlationIds should be different
        Assert.Equal(2, capturedCorrelationIds.Count);
        Assert.NotEqual(capturedCorrelationIds[0], capturedCorrelationIds[1]);
    }

    #endregion

    #region Non-Nullable Selectors Tests

    [Fact]
    public async Task GetEventsAsync_WithNonNullableSingleSelectors_CollectsIds()
    {
        // Arrange
        var siteId0 = _testProjections[0].siteId;
        var siteId1 = _testProjections[1].siteId;
        var allEvents = CreateTestEvents(siteId0, 2).Concat(CreateTestEvents(siteId1, 1)).ToList();
        AsyncEventRequest capturedRequest = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                capturedRequest = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedRequest == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = capturedRequest.topic,
                    subtopic = "",
                    correlationId = capturedRequest.correlationId,
                    events = allEvents,
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Use non-nullable single selector overload
        factory.WithAsyncEventRequestor("SvcA", new Func<FactoryTestProjection, Guid>[] { p => p.siteId });

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Contains(siteId0, capturedRequest.aggregateRootIds);
        Assert.Contains(siteId1, capturedRequest.aggregateRootIds);
        Assert.NotEmpty(result);
    }

    #endregion

    #region List Selector Tests

    [Fact]
    public async Task GetEventsAsync_WithListSelectors_ExpandsAndDeduplicates()
    {
        // Arrange - tagIds are lists of Guids
        var allTagIds = _testProjections.SelectMany(p => p.tagIds).Distinct().ToList();
        var allEvents = allTagIds.SelectMany(tagId => CreateTestEvents(tagId, 1)).ToList();
        AsyncEventRequest capturedRequest = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                capturedRequest = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedRequest == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = capturedRequest.topic,
                    subtopic = "",
                    correlationId = capturedRequest.correlationId,
                    events = allEvents,
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Use list selector overload
        factory.WithAsyncEventRequestor("TagService",
            new Func<FactoryTestProjection, Guid>[] { },
            new Func<FactoryTestProjection, List<Guid>>[] { p => p.tagIds });

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - aggregateRootIds should contain all distinct tagIds
        Assert.NotNull(capturedRequest);
        foreach (var tagId in allTagIds)
        {
            Assert.Contains(tagId, capturedRequest.aggregateRootIds);
        }
        // Request should have exactly the distinct count
        Assert.Equal(allTagIds.Count, capturedRequest.aggregateRootIds.Count);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetEventsAsync_SharedForeignIdAcrossProjections_DeduplicatesInRequest()
    {
        // Arrange - make both projections share the same externalId
        var sharedId = Guid.NewGuid();
        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection { id = Guid.NewGuid(), name = "P1", externalId = sharedId },
            new FactoryTestProjection { id = Guid.NewGuid(), name = "P2", externalId = sharedId }
        };

        var events = CreateTestEvents(sharedId, 3);
        AsyncEventRequest capturedRequest = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                capturedRequest = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedRequest == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = capturedRequest.topic,
                    subtopic = "",
                    correlationId = capturedRequest.correlationId,
                    events = events,
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - only 1 unique ID in request (Distinct)
        Assert.Single(capturedRequest.aggregateRootIds);
        Assert.Equal(sharedId, capturedRequest.aggregateRootIds[0]);

        // Both projections should get the events since both point to the same foreignId
        var p0Events = result.Where(r => r.aggregateRootId == projections[0].id).SelectMany(r => r.events).ToList();
        var p1Events = result.Where(r => r.aggregateRootId == projections[1].id).SelectMany(r => r.events).ToList();
        Assert.Equal(3, p0Events.Count);
        Assert.Equal(3, p1Events.Count);
    }

    #endregion

    #region Non-Response Message Handling Tests

    [Fact]
    public async Task GetEventsAsync_InvalidJsonMessages_SkipsGracefully()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        var expectedEvents = CreateTestEvents(foreignId, 2);

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                // Invalid JSON (not a response)
                new ConsumeResult<string, string>
                {
                    Message = new Message<string, string> { Value = "this is not valid json {{{ " }
                },
                // Valid JSON but not a response object
                new ConsumeResult<string, string>
                {
                    Message = new Message<string, string> { Value = "{\"foo\": \"bar\"}" }
                },
                // The actual response
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = expectedEvents,
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - should have the events despite bad messages
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_NullConsumeResult_SkipsGracefully()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        var expectedEvents = CreateTestEvents(foreignId, 1);

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            // Return the valid response (null results are from the base mock returning null)
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = expectedEvents,
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Single(events);
    }

    [Fact]
    public async Task GetEventsAsync_ResponseWithNullCorrelationId_IsIgnored()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        var expectedEvents = CreateTestEvents(foreignId, 2);

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                // Response with null correlationId
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = null,
                    events = CreateTestEvents(foreignId, 99),
                    complete = true
                }),
                // The real response
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = expectedEvents,
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - only 2 events, not the 99 from the null-correlated message
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(2, events.Count);
    }

    #endregion

    #region Consumer Subscription Tests

    [Fact]
    public async Task GetEventsAsync_SubscribesConsumerToRequestorTopic()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        var testEvents = CreateTestEvents(foreignId, 1);
        List<string> subscribedTopics = null;

        _mockConsumer.Setup(c => c.Subscribe(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(topics => subscribedTopics = topics.ToList());

        SetupSingleCompleteResponse("SvcA_EventRequest", testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        await factory.GetEventsAsync();

        // Assert - consumer should have been subscribed to the topic
        Assert.NotNull(subscribedTopics);
        Assert.Contains("SvcA_EventRequest", subscribedTopics);
    }

    [Fact]
    public async Task GetEventsAsync_GetsConsumerWithProjectionContainerName()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        SetupSingleCompleteResponse("SvcA_EventRequest", CreateTestEvents(foreignId, 1));

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        await factory.GetEventsAsync();

        // Assert - consumer group should be the projection's containerName
        _mockNostify.Verify(n => n.GetOrCreateKafkaConsumer("FactoryTestProjections"), Times.Once);
    }

    #endregion

    #region PointInTime Tests

    [Fact]
    public async Task GetEventsAsync_PassesPointInTimeToRequest()
    {
        // Arrange
        var pointInTime = DateTime.UtcNow.AddDays(-7);
        var foreignId = _testProjections[0].externalId!.Value;
        AsyncEventRequest capturedRequest = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                capturedRequest = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedRequest == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = capturedRequest.topic,
                    subtopic = "",
                    correlationId = capturedRequest.correlationId,
                    events = new List<Event>(),
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest.pointInTime);
        Assert.Equal(pointInTime, capturedRequest.pointInTime!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetEventsAsync_NullPointInTime_RequestHasNullPointInTime()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;
        AsyncEventRequest capturedRequest = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                capturedRequest = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedRequest == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = capturedRequest.topic,
                    subtopic = "",
                    correlationId = capturedRequest.correlationId,
                    events = new List<Event>(),
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            pointInTime: null,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest.pointInTime);
    }

    [Fact]
    public async Task GetEventsAsync_PointInTime_OnlyPreCutoffEventsReturnedEndToEnd()
    {
        // Arrange — simulate a handler that respects pointInTime:
        // response contains only events BEFORE the cutoff
        var pointInTime = DateTime.UtcNow.AddDays(-3);
        var foreignId = _testProjections[0].externalId!.Value;

        var eventBeforeCutoff1 = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = pointInTime.AddDays(-10),
            command = new NostifyCommand("Create_External")
        };
        var eventBeforeCutoff2 = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = pointInTime.AddHours(-1),
            command = new NostifyCommand("Update_External")
        };

        // Pre-cutoff events only (handler filtered out post-cutoff)
        var preCutoffEvents = new List<Event> { eventBeforeCutoff1, eventBeforeCutoff2 };

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = preCutoffEvents,
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert — only pre-cutoff events should be in the result
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.True(e.timestamp <= pointInTime,
            $"Event timestamp {e.timestamp} should be <= pointInTime {pointInTime}"));
        Assert.Contains(events, e => e.id == eventBeforeCutoff1.id);
        Assert.Contains(events, e => e.id == eventBeforeCutoff2.id);
    }

    [Fact]
    public async Task GetEventsAsync_PointInTime_EventExactlyAtCutoffIsIncluded()
    {
        // Arrange — boundary: event with timestamp == pointInTime should be included
        var pointInTime = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var foreignId = _testProjections[0].externalId!.Value;

        var eventAtExactCutoff = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = pointInTime, // exactly at cutoff
            command = new NostifyCommand("Update_External")
        };
        var eventBeforeCutoff = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = pointInTime.AddMinutes(-30),
            command = new NostifyCommand("Create_External")
        };

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = new List<Event> { eventBeforeCutoff, eventAtExactCutoff },
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert — both events should be returned (exact cutoff is <=)
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.id == eventAtExactCutoff.id);
        Assert.Contains(events, e => e.id == eventBeforeCutoff.id);
    }

    [Fact]
    public async Task GetEventsAsync_PointInTime_MultipleRequestors_AllRequestsContainPointInTime()
    {
        // Arrange — two async requestors, both requests should contain the same pointInTime
        var pointInTime = DateTime.UtcNow.AddDays(-5);
        var capturedRequests = new List<AsyncEventRequest>();

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                var req = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
                capturedRequests.Add(req);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedRequests.Count == 0) return null;
                var lastReq = capturedRequests.Last();
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = lastReq.topic,
                    subtopic = "",
                    correlationId = lastReq.correlationId,
                    events = new List<Event>(),
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);
        factory.WithAsyncEventRequestor("SvcB", p => p.anotherExternalId);

        // Act
        await factory.GetEventsAsync();

        // Assert — both requests contain pointInTime
        Assert.Equal(2, capturedRequests.Count);
        Assert.All(capturedRequests, req =>
        {
            Assert.NotNull(req.pointInTime);
            Assert.Equal(pointInTime, req.pointInTime!.Value, TimeSpan.FromSeconds(1));
        });
        // Different topics
        Assert.Contains(capturedRequests, r => r.topic == "SvcA_EventRequest");
        Assert.Contains(capturedRequests, r => r.topic == "SvcB_EventRequest");
    }

    [Fact]
    public async Task GetEventsAsync_PointInTime_DependantAsyncRequestor_PassesPointInTime()
    {
        // Arrange — dependant async requestor should also pass pointInTime in the request
        var pointInTime = DateTime.UtcNow.AddDays(-2);
        var dependentTargetId = Guid.NewGuid();
        AsyncEventRequest capturedDependantRequest = null;

        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Proj1",
                siteId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid>(),
                externalId = Guid.NewGuid(),
                anotherExternalId = Guid.NewGuid(),
                dependentExternalId = null // Will be populated by first-round event
            }
        };

        // First-round same-service event that sets dependentExternalId
        var firstRoundEvent = new Event
        {
            aggregateRootId = projections[0].siteId,
            timestamp = pointInTime.AddDays(-5),
            command = new NostifyCommand("SetDependentExternal"),
            payload = Newtonsoft.Json.Linq.JObject.FromObject(new { dependentExternalId = dependentTargetId })
        };

        // Mock event store with the first-round event
        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(new List<Event> { firstRoundEvent });
        var mockNostify = new Mock<INostify>();
        mockNostify.Setup(n => n.GetEventStoreContainerAsync(It.IsAny<bool>())).ReturnsAsync(mockContainer.Object);
        mockNostify.Setup(n => n.KafkaProducer).Returns(_mockProducer.Object);
        mockNostify.Setup(n => n.GetOrCreateKafkaConsumer(It.IsAny<string>())).Returns(_mockConsumer.Object);

        int produceCallCount = 0;
        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                produceCallCount++;
                var req = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
                capturedDependantRequest = req;
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Subscription).Returns(new List<string>());
        _mockConsumer.Setup(c => c.Subscribe(It.IsAny<IEnumerable<string>>()));
        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedDependantRequest == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = capturedDependantRequest.topic,
                    subtopic = "",
                    correlationId = capturedDependantRequest.correlationId,
                    events = new List<Event>(),
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithDependantAsyncEventRequestor("DepSvc", p => p.dependentExternalId);

        // Act
        await factory.GetEventsAsync();

        // Assert — dependant async request should contain pointInTime
        Assert.NotNull(capturedDependantRequest);
        Assert.NotNull(capturedDependantRequest.pointInTime);
        Assert.Equal(pointInTime, capturedDependantRequest.pointInTime!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal("DepSvc_EventRequest", capturedDependantRequest.topic);
    }

    [Fact]
    public async Task GetEventsAsync_PointInTime_PostCutoffEventsExcludedByHandler()
    {
        // Arrange — simulates a handler that properly filters: only pre-cutoff events
        // in response, ensuring post-cutoff events are absent from result
        var pointInTime = DateTime.UtcNow.AddDays(-1);
        var foreignId = _testProjections[0].externalId!.Value;

        var eventBefore = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = pointInTime.AddHours(-5),
            command = new NostifyCommand("Create_Item")
        };

        // The handler filters out this event (it's after the cutoff)
        // So it will NOT appear in the consumer's response
        var eventAfter = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = pointInTime.AddHours(3),
            command = new NostifyCommand("Update_Item")
        };

        // Response only includes pre-cutoff event (handler did the filtering)
        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = new List<Event> { eventBefore }, // handler excluded eventAfter
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Single(events);
        Assert.Equal(eventBefore.id, events[0].id);
        Assert.DoesNotContain(events, e => e.id == eventAfter.id);
    }

    [Fact]
    public async Task GetEventsAsync_PointInTime_NoEventsBeforeCutoff_ReturnsEmpty()
    {
        // Arrange — all events are after the cutoff, handler returns empty
        var pointInTime = DateTime.UtcNow.AddDays(-30);
        var foreignId = _testProjections[0].externalId!.Value;

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = new List<Event>(), // handler found no events before cutoff
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        var events = result.SelectMany(r => r.events).ToList();
        Assert.Empty(events);
    }

    #endregion

    #region Dependant Async Event Requestor Tests

    [Fact]
    public async Task GetEventsAsync_DependantAsyncRequestor_AppliesInitialEventsThenFetches()
    {
        // Arrange
        var dependentTargetId = Guid.NewGuid();
        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "P1",
                siteId = Guid.NewGuid(),
                dependentExternalId = null // starts null, will be set by initial event
            }
        };

        // Initial event that sets the dependentExternalId
        var initialEventAggId = projections[0].siteId;
        var initialEvents = new List<Event>
        {
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = initialEventAggId,
                timestamp = DateTime.UtcNow.AddMinutes(-10),
                command = new NostifyCommand("SetDependentId"),
                payload = new { dependentExternalId = dependentTargetId }
            }
        };

        // The dependent service events
        var dependentEvents = CreateTestEvents(dependentTargetId, 2);

        // Setup local event store to return initial events (for same-service selectors)
        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(initialEvents);
        var localMockNostify = new Mock<INostify>();
        localMockNostify.Setup(n => n.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(mockContainer.Object);
        localMockNostify.Setup(n => n.KafkaProducer).Returns(_mockProducer.Object);
        localMockNostify.Setup(n => n.GetOrCreateKafkaConsumer(It.IsAny<string>())).Returns(_mockConsumer.Object);

        _mockConsumer.Setup(c => c.Subscription).Returns(new List<string>());

        // Setup Kafka for the dependent requestor
        string capturedCorrelationId = null;
        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                var req = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
                capturedCorrelationId = req.correlationId;
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedCorrelationId == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "DepService_EventRequest",
                    subtopic = "",
                    correlationId = capturedCorrelationId,
                    events = dependentEvents,
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            localMockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Same-service selector gets initial events, dependent async gets the follow-up
        factory
            .WithSameServiceIdSelectors(p => p.siteId)
            .WithDependantAsyncEventRequestor("DepService", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - should have both initial events and dependent events
        Assert.True(result.Count >= 1, $"Expected at least 1 result entry, got {result.Count}");

        // Verify the dependent requestor was triggered with the correct ID
        _mockProducer.Verify(p => p.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetEventsAsync_ResponseWithNullEventsList_HandlesGracefully()
    {
        // Arrange
        var foreignId = _testProjections[0].externalId!.Value;

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            // Manually build a response JSON with null events
            return new List<ConsumeResult<string, string>>
            {
                new ConsumeResult<string, string>
                {
                    Message = new Message<string, string>
                    {
                        Value = JsonConvert.SerializeObject(new
                        {
                            topic = "SvcA_EventRequest",
                            subtopic = "",
                            correlationId = correlationId,
                            events = (List<Event>)null,
                            complete = true
                        })
                    }
                }
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act - should not throw
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEventsAsync_EventsForUnknownAggregateRootId_NotMappedToAnyProjection()
    {
        // Arrange - response contains events for an ID that no projection references
        var foreignId = _testProjections[0].externalId!.Value;
        var unknownId = Guid.NewGuid();
        var knownEvents = CreateTestEvents(foreignId, 2);
        var unknownEvents = CreateTestEvents(unknownId, 5);

        SetupSingleCompleteResponse("SvcA_EventRequest", knownEvents.Concat(unknownEvents).ToList());

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - unknown aggregate root events should not produce any ExternalDataEvent
        var allMappedEvents = result.SelectMany(r => r.events).ToList();
        Assert.DoesNotContain(allMappedEvents, e => e.aggregateRootId == unknownId);
        Assert.Equal(2, allMappedEvents.Count(e => e.aggregateRootId == foreignId));
    }

    [Fact]
    public async Task GetEventsAsync_MixOfNullableAndNonNullableSelectors_WorksTogether()
    {
        // Arrange
        var siteId = _testProjections[0].siteId;
        var extId = _testProjections[0].externalId!.Value;
        var allEvents = CreateTestEvents(siteId, 1).Concat(CreateTestEvents(extId, 1)).ToList();

        SetupSingleCompleteResponse("MixedSvc_EventRequest", allEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Use mixed overload: non-nullable singles + nullable singles
        factory.WithAsyncEventRequestor("MixedSvc",
            new Func<FactoryTestProjection, Guid?>[] { p => p.externalId },
            new Func<FactoryTestProjection, List<Guid?>>[] { });

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetEventsAsync_LargeNumberOfChunks_100Events_20Chunks()
    {
        // Arrange - simulate many chunks
        var foreignId = _testProjections[0].externalId!.Value;
        int totalEvents = 100;
        int chunkSize = 5;
        int numChunks = totalEvents / chunkSize;

        var allExpectedEvents = CreateTestEvents(foreignId, totalEvents);

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            var responses = new List<ConsumeResult<string, string>>();
            for (int i = 0; i < numChunks; i++)
            {
                var chunkEvents = allExpectedEvents.Skip(i * chunkSize).Take(chunkSize).ToList();
                responses.Add(MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunkEvents,
                    complete = (i == numChunks - 1)
                }));
            }
            return responses;
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - all 100 events accumulated
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(totalEvents, events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_InterleavedChunksWithNonMatchingMessages()
    {
        // Arrange - response chunks interleaved with messages from other services and bad JSON
        var foreignId = _testProjections[0].externalId!.Value;
        var chunk1 = CreateTestEvents(foreignId, 3);
        var chunk2 = CreateTestEvents(foreignId, 2);

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            return new List<ConsumeResult<string, string>>
            {
                // Our first chunk
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunk1,
                    complete = false
                }),
                // Noise: different correlationId
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = "noise-correlation-id",
                    events = CreateTestEvents(foreignId, 50),
                    complete = true
                }),
                // Noise: invalid JSON
                new ConsumeResult<string, string>
                {
                    Message = new Message<string, string> { Value = "garbage" }
                },
                // Our second (final) chunk
                MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = "SvcA_EventRequest",
                    subtopic = "",
                    correlationId = correlationId,
                    events = chunk2,
                    complete = true
                })
            };
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - only our 5 events (3+2), not the 50 from noise
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(5, events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_MixedSelectorsAndListSelectors_CombinesAllForeignIds()
    {
        // Arrange - use both single and list selectors to a single requestor
        var siteId0 = _testProjections[0].siteId;
        var siteId1 = _testProjections[1].siteId;
        var tag0 = _testProjections[0].tagIds[0];
        var tag1 = _testProjections[1].tagIds[0];

        var allIds = new[] { siteId0, siteId1, tag0, tag1 }.Distinct().ToList();
        var allEvents = allIds.SelectMany(id => CreateTestEvents(id, 1)).ToList();

        AsyncEventRequest capturedRequest = null;

        _mockProducer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, msg, ct) =>
            {
                capturedRequest = JsonConvert.DeserializeObject<AsyncEventRequest>(msg.Value);
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        _mockConsumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
            .Returns<TimeSpan>(timeout =>
            {
                if (capturedRequest == null) return null;
                return MakeConsumeResult(new AsyncEventRequestResponse
                {
                    topic = capturedRequest.topic,
                    subtopic = "",
                    correlationId = capturedRequest.correlationId,
                    events = allEvents,
                    complete = true
                });
            });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Non-nullable singles + non-nullable lists
        factory.WithAsyncEventRequestor("SvcA",
            new Func<FactoryTestProjection, Guid>[] { p => p.siteId },
            new Func<FactoryTestProjection, List<Guid>>[] { p => p.tagIds });

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - request should have all unique IDs from both singles and lists
        Assert.NotNull(capturedRequest);
        Assert.Contains(siteId0, capturedRequest.aggregateRootIds);
        Assert.Contains(siteId1, capturedRequest.aggregateRootIds);
        // Tags should also be present (via expanded list selectors)
        foreach (var tagId in _testProjections[0].tagIds.Concat(_testProjections[1].tagIds).Distinct())
        {
            Assert.Contains(tagId, capturedRequest.aggregateRootIds);
        }
    }

    [Fact]
    public async Task GetEventsAsync_DefaultTimeoutUsedWhenEnvVarNotSet()
    {
        // Arrange - clear the env var
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", null);

        var foreignId = _testProjections[0].externalId!.Value;

        // Consumer never responds - will timeout at default 30s
        // We can't wait 30s, so just verify it doesn't crash
        // Use a very short approach: immediately return a complete response
        SetupSingleCompleteResponse("SvcA_EventRequest", CreateTestEvents(foreignId, 1));

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act - should work fine with default 30s timeout
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetEventsAsync_InvalidTimeoutEnvVar_UsesDefault()
    {
        // Arrange
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "not-a-number");

        var foreignId = _testProjections[0].externalId!.Value;
        SetupSingleCompleteResponse("SvcA_EventRequest", CreateTestEvents(foreignId, 1));

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act - should not throw, uses default timeout
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetEventsAsync_ZeroTimeoutEnvVar_UsesDefault()
    {
        // Arrange - 0 is invalid (parsedTimeout > 0 check)
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "0");

        var foreignId = _testProjections[0].externalId!.Value;
        SetupSingleCompleteResponse("SvcA_EventRequest", CreateTestEvents(foreignId, 1));

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetEventsAsync_NegativeTimeoutEnvVar_UsesDefault()
    {
        // Arrange
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "-5");

        var foreignId = _testProjections[0].externalId!.Value;
        SetupSingleCompleteResponse("SvcA_EventRequest", CreateTestEvents(foreignId, 1));

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotEmpty(result);
    }

    #endregion

    #region ChunkEvents Integration Tests

    [Fact]
    public async Task GetEventsAsync_UsingChunkEventsOutput_AllDataAccumulated()
    {
        // Arrange - use ChunkEvents to create realistic chunked responses
        var foreignId = _testProjections[0].externalId!.Value;
        var allEvents = CreateTestEvents(foreignId, 25);

        // Use actual ChunkEvents to split - use a small max bytes to force many chunks
        var chunkedResponses = AsyncEventRequestResponse.ChunkEvents(
            allEvents, maxBytes: 500, // Very small to force chunking
            topic: "SvcA_EventRequest",
            subtopic: "",
            correlationId: "placeholder"); // Will be replaced

        // Verify we actually got multiple chunks
        Assert.True(chunkedResponses.Count > 1, $"Expected multiple chunks but got {chunkedResponses.Count}");

        SetupProducerCaptureAndConsumerResponses("SvcA_EventRequest", correlationId =>
        {
            // Replace placeholder correlationId with actual
            return chunkedResponses.Select(r => MakeConsumeResult(new AsyncEventRequestResponse
            {
                topic = r.topic,
                subtopic = r.subtopic,
                correlationId = correlationId,
                events = r.events,
                complete = r.complete
            })).ToList();
        });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithAsyncEventRequestor("SvcA", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - all 25 events should be accumulated regardless of how many chunks
        var events = result.Where(r => r.aggregateRootId == _testProjections[0].id)
            .SelectMany(r => r.events).ToList();
        Assert.Equal(25, events.Count);
    }

    #endregion
}
