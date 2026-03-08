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
/// Tests the AsyncEventRequestHandler template logic, specifically the pointInTime
/// filtering behavior. Because the handler is a generated template (not compiled in
/// the library), these tests replicate its core logic:
///   1. Deserialize AsyncEventRequest
///   2. Query event store filtering by aggregateRootIds and optional pointInTime
///   3. Chunk the results into AsyncEventRequestResponse messages
///   4. Produce responses back to Kafka
/// </summary>
public class AsyncEventRequestHandlerTests : IDisposable
{
    private readonly string _savedMaxBytesEnv;

    public AsyncEventRequestHandlerTests()
    {
        _savedMaxBytesEnv = Environment.GetEnvironmentVariable("AsyncEventRequestMaxMessageBytes");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AsyncEventRequestMaxMessageBytes", _savedMaxBytesEnv);
    }

    #region Helper — Simulates the handler's core logic

    /// <summary>
    /// Replicates the AsyncEventRequestHandler template's core logic for testability.
    /// This mirrors lines 96-127 of the template:
    ///   - Query event store by aggregateRootIds
    ///   - Filter by pointInTime if provided
    ///   - Chunk events into response messages
    /// </summary>
    private async Task<List<AsyncEventRequestResponse>> SimulateHandlerLogic(
        Container eventStore,
        AsyncEventRequest request,
        IQueryExecutor queryExecutor = null)
    {
        queryExecutor ??= InMemoryQueryExecutor.Default;

        var eventsQuery = eventStore
            .GetItemLinqQueryable<Event>()
            .Where(x => request.aggregateRootIds.Contains(x.aggregateRootId));

        if (request.pointInTime.HasValue)
        {
            eventsQuery = eventsQuery.Where(e => e.timestamp <= request.pointInTime.Value);
        }

        List<Event> allEvents = await queryExecutor.ReadAllAsync(eventsQuery.OrderBy(e => e.timestamp));

        int maxBytes = int.TryParse(
            Environment.GetEnvironmentVariable("AsyncEventRequestMaxMessageBytes"),
            out int mb) ? mb : 900_000;

        var chunks = AsyncEventRequestResponse.ChunkEvents(
            allEvents,
            maxBytes,
            request.topic,
            request.subtopic ?? string.Empty,
            request.correlationId);

        return chunks;
    }

    /// <summary>
    /// Creates test events with specific timestamps relative to a reference point.
    /// </summary>
    private static List<Event> CreateTimestampedEvents(
        Guid aggregateRootId,
        params (string commandName, TimeSpan offset, DateTime reference)[] specs)
    {
        return specs.Select(s => new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = aggregateRootId,
            timestamp = s.reference.Add(s.offset),
            command = new NostifyCommand(s.commandName)
        }).ToList();
    }

    #endregion

    #region PointInTime Filtering Tests

    [Fact]
    public async Task Handler_PointInTime_FiltersOutEventsAfterCutoff()
    {
        // Arrange
        var pointInTime = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var aggId = Guid.NewGuid();

        var events = CreateTimestampedEvents(aggId,
            ("Create_Item", TimeSpan.FromHours(-24), pointInTime),   // before — included
            ("Update_Item", TimeSpan.FromHours(-1), pointInTime),    // before — included
            ("Update_Item2", TimeSpan.FromMinutes(1), pointInTime),  // after — excluded
            ("Delete_Item", TimeSpan.FromDays(1), pointInTime));     // after — excluded

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { aggId },
            pointInTime = pointInTime,
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert — only 2 pre-cutoff events
        var allEvents = chunks.SelectMany(c => c.events).ToList();
        Assert.Equal(2, allEvents.Count);
        Assert.All(allEvents, e => Assert.True(e.timestamp <= pointInTime,
            $"Event at {e.timestamp} should be <= {pointInTime}"));
    }

    [Fact]
    public async Task Handler_NullPointInTime_ReturnsAllEvents()
    {
        // Arrange
        var reference = DateTime.UtcNow;
        var aggId = Guid.NewGuid();

        var events = CreateTimestampedEvents(aggId,
            ("Create_Item", TimeSpan.FromDays(-30), reference),
            ("Update_Item", TimeSpan.FromDays(-10), reference),
            ("Update_Item2", TimeSpan.FromHours(-1), reference),
            ("Recent_Item", TimeSpan.FromMinutes(-5), reference));

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { aggId },
            pointInTime = null, // No cutoff
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert — all 4 events returned
        var allEvents = chunks.SelectMany(c => c.events).ToList();
        Assert.Equal(4, allEvents.Count);
    }

    [Fact]
    public async Task Handler_PointInTime_EventExactlyAtBoundaryIsIncluded()
    {
        // Arrange — event with timestamp == pointInTime should pass <= filter
        var pointInTime = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var aggId = Guid.NewGuid();

        var eventExactly = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = aggId,
            timestamp = pointInTime,
            command = new NostifyCommand("Update_Item")
        };
        var eventBefore = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = aggId,
            timestamp = pointInTime.AddSeconds(-1),
            command = new NostifyCommand("Create_Item")
        };
        var eventAfter = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = aggId,
            timestamp = pointInTime.AddSeconds(1),
            command = new NostifyCommand("Delete_Item")
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(
            new List<Event> { eventBefore, eventExactly, eventAfter });

        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { aggId },
            pointInTime = pointInTime,
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert
        var allEvents = chunks.SelectMany(c => c.events).ToList();
        Assert.Equal(2, allEvents.Count);
        Assert.Contains(allEvents, e => e.id == eventBefore.id);
        Assert.Contains(allEvents, e => e.id == eventExactly.id);
        Assert.DoesNotContain(allEvents, e => e.id == eventAfter.id);
    }

    [Fact]
    public async Task Handler_PointInTime_AllEventsAfterCutoff_ReturnsEmptyComplete()
    {
        // Arrange — all events in the store are after pointInTime
        var pointInTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var aggId = Guid.NewGuid();

        var events = CreateTimestampedEvents(aggId,
            ("Create_Item", TimeSpan.FromDays(30), pointInTime),
            ("Update_Item", TimeSpan.FromDays(60), pointInTime));

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { aggId },
            pointInTime = pointInTime,
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert — empty events, single chunk with complete = true
        Assert.Single(chunks);
        Assert.True(chunks[0].complete);
        Assert.Empty(chunks[0].events);
    }

    [Fact]
    public async Task Handler_PointInTime_MultipleAggregateRoots_FiltersEachByTimestamp()
    {
        // Arrange — two aggregate roots, events mixed before/after cutoff
        var pointInTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var aggId1 = Guid.NewGuid();
        var aggId2 = Guid.NewGuid();

        var events = new List<Event>
        {
            new Event { id = Guid.NewGuid(), aggregateRootId = aggId1, timestamp = pointInTime.AddDays(-5), command = new NostifyCommand("Create_1") },
            new Event { id = Guid.NewGuid(), aggregateRootId = aggId1, timestamp = pointInTime.AddDays(1), command = new NostifyCommand("Update_1") },   // after
            new Event { id = Guid.NewGuid(), aggregateRootId = aggId2, timestamp = pointInTime.AddDays(-10), command = new NostifyCommand("Create_2") },
            new Event { id = Guid.NewGuid(), aggregateRootId = aggId2, timestamp = pointInTime.AddDays(-2), command = new NostifyCommand("Update_2") },
            new Event { id = Guid.NewGuid(), aggregateRootId = aggId2, timestamp = pointInTime.AddHours(1), command = new NostifyCommand("Delete_2") },  // after
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { aggId1, aggId2 },
            pointInTime = pointInTime,
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert — 3 events total: 1 for aggId1, 2 for aggId2
        var allEvents = chunks.SelectMany(c => c.events).ToList();
        Assert.Equal(3, allEvents.Count);
        Assert.Single(allEvents.Where(e => e.aggregateRootId == aggId1));
        Assert.Equal(2, allEvents.Count(e => e.aggregateRootId == aggId2));
        Assert.All(allEvents, e => Assert.True(e.timestamp <= pointInTime));
    }

    [Fact]
    public async Task Handler_PointInTime_EventsReturnedInChronologicalOrder()
    {
        // Arrange
        var pointInTime = DateTime.UtcNow;
        var aggId = Guid.NewGuid();

        // Events in non-chronological order
        var events = new List<Event>
        {
            new Event { id = Guid.NewGuid(), aggregateRootId = aggId, timestamp = pointInTime.AddDays(-1), command = new NostifyCommand("Cmd2") },
            new Event { id = Guid.NewGuid(), aggregateRootId = aggId, timestamp = pointInTime.AddDays(-3), command = new NostifyCommand("Cmd1") },
            new Event { id = Guid.NewGuid(), aggregateRootId = aggId, timestamp = pointInTime.AddHours(-6), command = new NostifyCommand("Cmd3") },
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { aggId },
            pointInTime = pointInTime,
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert — events should be ordered by timestamp ascending
        var allEvents = chunks.SelectMany(c => c.events).ToList();
        Assert.Equal(3, allEvents.Count);
        for (int i = 1; i < allEvents.Count; i++)
        {
            Assert.True(allEvents[i].timestamp >= allEvents[i - 1].timestamp,
                $"Events not in order: {allEvents[i - 1].timestamp} should be before {allEvents[i].timestamp}");
        }
    }

    [Fact]
    public async Task Handler_PointInTime_IgnoresEventsForUnrequestedAggregates()
    {
        // Arrange — event store contains events for multiple aggregates,
        // but request only asks for one
        var pointInTime = DateTime.UtcNow;
        var requestedId = Guid.NewGuid();
        var unrequestedId = Guid.NewGuid();

        var events = new List<Event>
        {
            new Event { id = Guid.NewGuid(), aggregateRootId = requestedId, timestamp = pointInTime.AddDays(-2), command = new NostifyCommand("Create_Req") },
            new Event { id = Guid.NewGuid(), aggregateRootId = unrequestedId, timestamp = pointInTime.AddDays(-1), command = new NostifyCommand("Create_Unreq") },
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { requestedId }, // only requesting one
            pointInTime = pointInTime,
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert — only events for requestedId returned
        var allEvents = chunks.SelectMany(c => c.events).ToList();
        Assert.Single(allEvents);
        Assert.Equal(requestedId, allEvents[0].aggregateRootId);
    }

    [Fact]
    public async Task Handler_PointInTime_ChunkedResponse_AllChunksRespectCutoff()
    {
        // Arrange — force chunking with small maxBytes, verify all chunks respect pointInTime
        Environment.SetEnvironmentVariable("AsyncEventRequestMaxMessageBytes", "500");
        var pointInTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var aggId = Guid.NewGuid();

        // 20 events before cutoff, 5 after cutoff
        var events = new List<Event>();
        for (int i = 0; i < 20; i++)
        {
            events.Add(new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId,
                timestamp = pointInTime.AddMinutes(-(20 - i)),
                command = new NostifyCommand($"Cmd_{i}")
            });
        }
        for (int i = 0; i < 5; i++)
        {
            events.Add(new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId,
                timestamp = pointInTime.AddMinutes(i + 1), // after cutoff
                command = new NostifyCommand($"PostCmd_{i}")
            });
        }

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { aggId },
            pointInTime = pointInTime,
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert — should have multiple chunks, all events <= pointInTime
        Assert.True(chunks.Count > 1, $"Expected multiple chunks but got {chunks.Count}");
        Assert.True(chunks.Last().complete, "Last chunk should be marked complete");

        var allEvents = chunks.SelectMany(c => c.events).ToList();
        Assert.Equal(20, allEvents.Count);
        Assert.All(allEvents, e => Assert.True(e.timestamp <= pointInTime));
    }

    [Fact]
    public async Task Handler_PointInTime_CorrelationIdPreservedInAllChunks()
    {
        // Arrange
        Environment.SetEnvironmentVariable("AsyncEventRequestMaxMessageBytes", "300");
        var pointInTime = DateTime.UtcNow;
        var aggId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var events = new List<Event>();
        for (int i = 0; i < 15; i++)
        {
            events.Add(new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId,
                timestamp = pointInTime.AddMinutes(-(15 - i)),
                command = new NostifyCommand($"Cmd_{i}")
            });
        }

        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var request = new AsyncEventRequest
        {
            topic = "TestSvc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { aggId },
            pointInTime = pointInTime,
            correlationId = correlationId
        };

        // Act
        var chunks = await SimulateHandlerLogic(mockContainer.Object, request);

        // Assert — all chunks have the same correlationId
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(correlationId, c.correlationId));
    }

    #endregion

    #region Same-Service Path PointInTime Edge Cases

    [Fact]
    public async Task SameService_PointInTime_NullableSelector_FiltersCorrectly()
    {
        // Arrange — same-service path with nullable selector and pointInTime
        var pointInTime = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var foreignId = Guid.NewGuid();

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
                externalId = foreignId // nullable Guid?
            }
        };

        var eventBefore = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = pointInTime.AddDays(-5),
            command = new NostifyCommand("Create_External")
        };
        var eventAfter = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = foreignId,
            timestamp = pointInTime.AddDays(2),
            command = new NostifyCommand("Update_External")
        };

        var allEvents = new List<Event> { eventBefore, eventAfter };
        var (mockNostify, _) = CreateMockNostifyWithEvents(allEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient: null,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        var events = result.SelectMany(r => r.events).ToList();
        Assert.Single(events);
        Assert.Equal(eventBefore.id, events[0].id);
    }

    [Fact]
    public async Task SameService_PointInTime_ListSelector_FiltersCorrectly()
    {
        // Arrange — list selector with pointInTime filtering
        var pointInTime = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var tagId1 = Guid.NewGuid();
        var tagId2 = Guid.NewGuid();

        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Proj1",
                siteId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid> { tagId1, tagId2 },
                externalId = Guid.NewGuid()
            }
        };

        var events = new List<Event>
        {
            new Event { id = Guid.NewGuid(), aggregateRootId = tagId1, timestamp = pointInTime.AddDays(-10), command = new NostifyCommand("Create_Tag1") },
            new Event { id = Guid.NewGuid(), aggregateRootId = tagId1, timestamp = pointInTime.AddDays(1), command = new NostifyCommand("Update_Tag1") },  // after
            new Event { id = Guid.NewGuid(), aggregateRootId = tagId2, timestamp = pointInTime.AddDays(-3), command = new NostifyCommand("Create_Tag2") },
            new Event { id = Guid.NewGuid(), aggregateRootId = tagId2, timestamp = pointInTime.AddDays(5), command = new NostifyCommand("Update_Tag2") },  // after
        };

        var (mockNostify, _) = CreateMockNostifyWithEvents(events);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient: null,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceListIdSelectors(p => p.tagIds);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert — only 2 events (one per tag, both before cutoff)
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(2, allEvents.Count);
        Assert.All(allEvents, e => Assert.True(e.timestamp <= pointInTime));
    }

    [Fact]
    public async Task SameService_PointInTime_MultipleSelectorTypes_AllRespectCutoff()
    {
        // Arrange — mix of single ID and list selectors, all should respect pointInTime
        var pointInTime = new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var siteId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Proj1",
                siteId = siteId,
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid> { tagId },
                externalId = Guid.NewGuid()
            }
        };

        var events = new List<Event>
        {
            // siteId events
            new Event { id = Guid.NewGuid(), aggregateRootId = siteId, timestamp = pointInTime.AddDays(-5), command = new NostifyCommand("Create_Site") },
            new Event { id = Guid.NewGuid(), aggregateRootId = siteId, timestamp = pointInTime.AddDays(1), command = new NostifyCommand("Update_Site") }, // after
            // tagId events
            new Event { id = Guid.NewGuid(), aggregateRootId = tagId, timestamp = pointInTime.AddDays(-2), command = new NostifyCommand("Create_Tag") },
            new Event { id = Guid.NewGuid(), aggregateRootId = tagId, timestamp = pointInTime.AddDays(3), command = new NostifyCommand("Update_Tag") },  // after
        };

        var (mockNostify, _) = CreateMockNostifyWithEvents(events);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient: null,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceListIdSelectors(p => p.tagIds);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(2, allEvents.Count);
        Assert.All(allEvents, e => Assert.True(e.timestamp <= pointInTime));
    }

    [Fact]
    public async Task SameService_NullPointInTime_ReturnsAllEvents()
    {
        // Arrange — no pointInTime filter, all events should be returned
        var siteId = Guid.NewGuid();

        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Proj1",
                siteId = siteId,
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid>(),
                externalId = Guid.NewGuid()
            }
        };

        var events = new List<Event>
        {
            new Event { id = Guid.NewGuid(), aggregateRootId = siteId, timestamp = DateTime.UtcNow.AddDays(-30), command = new NostifyCommand("Create_Site") },
            new Event { id = Guid.NewGuid(), aggregateRootId = siteId, timestamp = DateTime.UtcNow.AddDays(-10), command = new NostifyCommand("Update_Site") },
            new Event { id = Guid.NewGuid(), aggregateRootId = siteId, timestamp = DateTime.UtcNow.AddMinutes(-5), command = new NostifyCommand("Recent_Site") },
        };

        var (mockNostify, _) = CreateMockNostifyWithEvents(events);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient: null,
            pointInTime: null,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert — all 3 events returned
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(3, allEvents.Count);
    }

    #endregion

    #region Helper to create mock INostify with events

    private static (Mock<INostify> mockNostify, Mock<Container> mockContainer) CreateMockNostifyWithEvents(List<Event> events)
    {
        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        var mockNostify = new Mock<INostify>();
        mockNostify.Setup(n => n.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(mockContainer.Object);
        return (mockNostify, mockContainer);
    }

    #endregion
}
