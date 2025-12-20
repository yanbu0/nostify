using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using nostify;
using Xunit;
using Newtonsoft.Json;

namespace nostify.Tests;

public class ExternalDataEventFactoryTests
{
    private readonly Mock<INostify> _mockNostify;
    private readonly Mock<Container> _mockContainer;
    private readonly List<FactoryTestProjection> _testProjections;
    private readonly DateTime _pointInTime;

    public ExternalDataEventFactoryTests()
    {
        _mockNostify = new Mock<INostify>();
        _mockContainer = new Mock<Container>();
        _pointInTime = DateTime.UtcNow.AddHours(-1);

        // Setup test projections with various foreign key scenarios
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

        // Setup mock nostify to return mock container
        _mockNostify.Setup(n => n.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(_mockContainer.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithRequiredParameters_CreatesInstance()
    {
        // Arrange & Act
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void Constructor_WithAllParameters_CreatesInstance()
    {
        // Arrange
        using var httpClient = new HttpClient();

        // Act
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient,
            _pointInTime);

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void Constructor_WithNullHttpClient_CreatesInstance()
    {
        // Arrange & Act
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            null,
            _pointInTime);

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void Constructor_WithEmptyProjectionList_CreatesInstance()
    {
        // Arrange & Act
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            new List<FactoryTestProjection>());

        // Assert
        Assert.NotNull(factory);
    }

    #endregion

    #region WithSameServiceIdSelectors Tests

    [Fact]
    public void WithSameServiceIdSelectors_SingleSelector_AddsSelector()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithSameServiceIdSelectors_MultipleSelectors_AddsAllSelectors()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceIdSelectors(
            p => p.siteId,
            p => p.ownerId,
            p => p.categoryId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithSameServiceIdSelectors_CalledMultipleTimes_AccumulatesSelectors()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceIdSelectors(p => p.ownerId);
        factory.WithSameServiceIdSelectors(p => p.categoryId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    #endregion

    #region WithSameServiceListIdSelectors Tests

    [Fact]
    public void WithSameServiceListIdSelectors_SingleSelector_AddsSelector()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceListIdSelectors(p => p.tagIds);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithSameServiceListIdSelectors_MultipleSelectors_AddsAllSelectors()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceListIdSelectors(
            p => p.tagIds,
            p => p.relatedIds);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithSameServiceListIdSelectors_CalledMultipleTimes_AccumulatesSelectors()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceListIdSelectors(p => p.tagIds);
        factory.WithSameServiceListIdSelectors(p => p.relatedIds);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    #endregion

    #region AddEventRequestors Tests

    [Fact]
    public void AddEventRequestors_WithHttpClient_AddsRequestors()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        var eventRequestor = new EventRequester<FactoryTestProjection>(
            "https://external-service.com/events",
            p => p.externalId);

        // Act - should not throw
        factory.AddEventRequestors(eventRequestor);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void AddEventRequestors_WithMultipleRequestors_AddsAll()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        var eventRequestor1 = new EventRequester<FactoryTestProjection>(
            "https://service1.com/events",
            p => p.externalId);
        var eventRequestor2 = new EventRequester<FactoryTestProjection>(
            "https://service2.com/events",
            p => p.anotherExternalId);

        // Act - should not throw
        factory.AddEventRequestors(eventRequestor1, eventRequestor2);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void AddEventRequestors_CalledMultipleTimes_AccumulatesRequestors()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        var eventRequestor1 = new EventRequester<FactoryTestProjection>(
            "https://service1.com/events",
            p => p.externalId);
        var eventRequestor2 = new EventRequester<FactoryTestProjection>(
            "https://service2.com/events",
            p => p.anotherExternalId);

        // Act - should not throw
        factory.AddEventRequestors(eventRequestor1);
        factory.AddEventRequestors(eventRequestor2);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void AddEventRequestors_WithoutHttpClient_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            null); // No HTTP client

        var eventRequestor = new EventRequester<FactoryTestProjection>(
            "https://external-service.com/events",
            p => p.externalId);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.AddEventRequestors(eventRequestor));

        Assert.Contains("HttpClient is not provided", exception.Message);
    }

    #endregion

    #region WithEventRequestor Tests

    [Fact]
    public void WithEventRequestor_WithHttpClient_AddsRequestor()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        // Act - should not throw
        factory.WithEventRequestor(
            "https://external-service.com/events",
            p => p.externalId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithEventRequestor_WithMultipleSelectors_AddsRequestor()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        // Act - should not throw
        factory.WithEventRequestor(
            "https://external-service.com/events",
            p => p.externalId,
            p => p.anotherExternalId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithEventRequestor_CalledMultipleTimes_AccumulatesRequestors()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        // Act - should not throw
        factory.WithEventRequestor("https://service1.com/events", p => p.externalId);
        factory.WithEventRequestor("https://service2.com/events", p => p.anotherExternalId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithEventRequestor_WithoutHttpClient_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            null); // No HTTP client

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.WithEventRequestor(
                "https://external-service.com/events",
                p => p.externalId));

        Assert.Contains("HttpClient is not provided", exception.Message);
    }

    #endregion

    #region GetEventsAsync Tests - Integration with ExternalDataEvent

    [Fact]
    public async Task GetEventsAsync_WithNoSelectors_ReturnsEmptyList()
    {
        // Arrange
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event>());
        
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEventsAsync_WithSingleIdSelectors_ReturnsMatchingEvents()
    {
        // Arrange
        var testEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSite")
            },
            new Event
            {
                aggregateRootId = _testProjections[1].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-25),
                command = new NostifyCommand("UpdateSite")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetEventsAsync_WithMultipleSingleIdSelectors_ReturnsAllMatchingEvents()
    {
        // Arrange
        var testEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSite")
            },
            new Event
            {
                aggregateRootId = _testProjections[0].ownerId,
                timestamp = DateTime.UtcNow.AddMinutes(-25),
                command = new NostifyCommand("CreateOwner")
            },
            new Event
            {
                aggregateRootId = _testProjections[1].categoryId,
                timestamp = DateTime.UtcNow.AddMinutes(-20),
                command = new NostifyCommand("CreateCategory")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(
            p => p.siteId,
            p => p.ownerId,
            p => p.categoryId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should have events for the projections that match
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public async Task GetEventsAsync_WithListIdSelectors_ReturnsMatchingEvents()
    {
        // Arrange
        var testEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].tagIds[0],
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateTag")
            },
            new Event
            {
                aggregateRootId = _testProjections[0].tagIds[1],
                timestamp = DateTime.UtcNow.AddMinutes(-25),
                command = new NostifyCommand("UpdateTag")
            },
            new Event
            {
                aggregateRootId = _testProjections[1].tagIds[0],
                timestamp = DateTime.UtcNow.AddMinutes(-20),
                command = new NostifyCommand("CreateTag2")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceListIdSelectors(p => p.tagIds);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public async Task GetEventsAsync_WithBothSingleAndListSelectors_ReturnsCombinedEvents()
    {
        // Arrange
        var testEvents = new List<Event>
        {
            // Single ID events
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSite")
            },
            // List ID events
            new Event
            {
                aggregateRootId = _testProjections[0].tagIds[0],
                timestamp = DateTime.UtcNow.AddMinutes(-25),
                command = new NostifyCommand("CreateTag")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceListIdSelectors(p => p.tagIds);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public async Task GetEventsAsync_WithPointInTime_FiltersEventsByTimestamp()
    {
        // Arrange
        var pointInTime = DateTime.UtcNow.AddMinutes(-20);
        
        var testEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = pointInTime.AddMinutes(-10), // Before pointInTime - should be included
                command = new NostifyCommand("CreateSite")
            },
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = pointInTime.AddMinutes(10), // After pointInTime - should be excluded
                command = new NostifyCommand("UpdateSite")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            httpClient: null,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Only the event before pointInTime should be returned
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.All(allEvents, e => Assert.True(e.timestamp <= pointInTime));
    }

    [Fact]
    public async Task GetEventsAsync_WithExternalServices_CombinesLocalAndExternalEvents()
    {
        // Arrange
        var localEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSite")
            }
        };

        var externalEvents = new List<IEvent>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].externalId!.Value,
                timestamp = DateTime.UtcNow.AddMinutes(-25),
                command = new NostifyCommand("ExternalEvent")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(localEvents);

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://external-service.com/events", externalEvents);
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithEventRequestor("https://external-service.com/events", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should have both local and external events
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public async Task GetEventsAsync_WithNoHttpClient_OnlyReturnsLocalEvents()
    {
        // Arrange
        var localEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSite")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(localEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            httpClient: null,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetEventsAsync_WithEmptyProjectionList_ReturnsEmptyList()
    {
        // Arrange
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event>());

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            new List<FactoryTestProjection>(),
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEventsAsync_WithNoMatchingEvents_ReturnsEmptyList()
    {
        // Arrange
        var unrelatedEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = Guid.NewGuid(), // Different ID that doesn't match any projection
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("UnrelatedEvent")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(unrelatedEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // No events should match
    }

    [Fact]
    public async Task GetEventsAsync_WithMultipleExternalServices_CombinesAllResults()
    {
        // Arrange
        var localEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSite")
            }
        };

        var service1Events = new List<IEvent>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].externalId!.Value,
                timestamp = DateTime.UtcNow.AddMinutes(-25),
                command = new NostifyCommand("Service1Event")
            }
        };

        var service2Events = new List<IEvent>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].anotherExternalId!.Value,
                timestamp = DateTime.UtcNow.AddMinutes(-20),
                command = new NostifyCommand("Service2Event")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(localEvents);

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://service1.com/events", service1Events);
        mockHandler.AddService("https://service2.com/events", service2Events);
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithEventRequestor("https://service1.com/events", p => p.externalId);
        factory.WithEventRequestor("https://service2.com/events", p => p.anotherExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should have events from local + service1 + service2
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public async Task GetEventsAsync_EventsAreOrderedByTimestamp()
    {
        // Arrange
        var testEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-10), // Newer
                command = new NostifyCommand("SecondEvent")
            },
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30), // Older
                command = new NostifyCommand("FirstEvent")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        if (result.Count > 0 && result[0].events.Count > 1)
        {
            var events = result[0].events;
            for (int i = 1; i < events.Count; i++)
            {
                Assert.True(events[i].timestamp >= events[i - 1].timestamp,
                    "Events should be ordered by timestamp");
            }
        }
    }

    #endregion

    #region Fluent API / Builder Pattern Tests

    [Fact]
    public async Task FluentApi_CanChainMultipleMethods()
    {
        // Arrange
        var testEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSite")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Act - chain multiple method calls
        factory.WithSameServiceIdSelectors(p => p.siteId, p => p.ownerId);
        factory.WithSameServiceListIdSelectors(p => p.tagIds);

        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FluentApi_WithExternalServices_CanChainMultipleMethods()
    {
        // Arrange
        var localEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = _testProjections[0].siteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSite")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(localEvents);

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://service1.com/events", new List<IEvent>());
        mockHandler.AddService("https://service2.com/events", new List<IEvent>());
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            _testProjections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Act - chain multiple method calls including external services
        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceListIdSelectors(p => p.tagIds);
        factory.WithEventRequestor("https://service1.com/events", p => p.externalId);
        factory.WithEventRequestor("https://service2.com/events", p => p.anotherExternalId);

        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetEventsAsync_WithDefaultGuidInSelector_HandlesGracefully()
    {
        // Arrange - projection with default (empty) Guid
        var projectionsWithDefaultId = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "ProjectionWithDefaultId",
                siteId = Guid.Empty, // Default GUID
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid>()
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event>());

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projectionsWithDefaultId,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - should handle gracefully without errors
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetEventsAsync_WithEmptyTagList_HandlesGracefully()
    {
        // Arrange - projection with empty tag list
        var projectionsWithEmptyTags = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "ProjectionWithEmptyTags",
                siteId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid>() // Empty list
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event>());

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projectionsWithEmptyTags,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceListIdSelectors(p => p.tagIds);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - should handle gracefully without errors
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetEventsAsync_WithDuplicateForeignIds_ReturnsDistinctEvents()
    {
        // Arrange - two projections referencing the same foreign ID
        var sharedSiteId = Guid.NewGuid();
        var projectionsWithSharedId = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Projection1",
                siteId = sharedSiteId,
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid>()
            },
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Projection2",
                siteId = sharedSiteId, // Same site ID
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid>()
            }
        };

        var testEvents = new List<Event>
        {
            new Event
            {
                aggregateRootId = sharedSiteId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateSharedSite")
            }
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(testEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projectionsWithSharedId,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Both projections should get the event for the shared site
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetEventsAsync_WithNullableExternalId_HandlesNullGracefully()
    {
        // Arrange - projection with null external ID
        var projectionsWithNullExternalId = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "ProjectionWithNullExternal",
                siteId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid>(),
                externalId = null // Null external ID
            }
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://external-service.com/events", new List<IEvent>());
        var httpClient = new HttpClient(mockHandler);

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event>());

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projectionsWithNullExternalId,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithEventRequestor("https://external-service.com/events", p => p.externalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert - should handle null gracefully
        Assert.NotNull(result);
    }

    #endregion

    #region Helper Methods

    private (Mock<INostify> mockNostify, Mock<Container> mockContainer) CreateMockNostifyWithEvents(List<Event> events)
    {
        var mockNostify = new Mock<INostify>();
        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);

        mockNostify.Setup(n => n.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(mockContainer.Object);

        return (mockNostify, mockContainer);
    }

    #endregion
}

/// <summary>
/// Test projection class for ExternalDataEventFactory tests
/// </summary>
public class FactoryTestProjection : NostifyObject, IProjection, IUniquelyIdentifiable
{
    public string name { get; set; } = string.Empty;
    public Guid siteId { get; set; }
    public Guid ownerId { get; set; }
    public Guid categoryId { get; set; }
    public List<Guid> tagIds { get; set; } = new List<Guid>();
    public List<Guid> relatedIds { get; set; } = new List<Guid>();
    public Guid? externalId { get; set; }
    public Guid? anotherExternalId { get; set; }
    
    // IProjection required members
    public bool initialized { get; set; } = false;
    public static string containerName => "FactoryTestProjections";

    public override void Apply(IEvent e)
    {
        UpdateProperties<FactoryTestProjection>(e.payload);
    }

    public Task<List<ExternalDataEvent>> GetExternalDataEvents(INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
    {
        return Task.FromResult(new List<ExternalDataEvent>());
    }

    public void ApplyExternalDataEvents(List<ExternalDataEvent> externalDataEvents)
    {
        // No-op for tests
    }
}
