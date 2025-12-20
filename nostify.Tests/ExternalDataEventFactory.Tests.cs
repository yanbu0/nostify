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
using Newtonsoft.Json.Linq;

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

    #region WithSameServiceDependantIdSelectors Tests

    [Fact]
    public void WithSameServiceDependantIdSelectors_SingleSelector_AddsSelector()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceDependantIdSelectors(p => p.dependentId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithSameServiceDependantIdSelectors_MultipleSelectors_AddsAllSelectors()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceDependantIdSelectors(
            p => p.dependentId,
            p => p.ownerId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithSameServiceDependantListIdSelectors_SingleSelector_AddsSelector()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act - should not throw
        factory.WithSameServiceDependantListIdSelectors(p => p.dependentListIds);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantIdSelector_WhenIdIsInitiallyNull_PopulatesAfterFirstRoundEvents()
    {
        // Arrange
        // Projection starts with dependentId = Guid.Empty
        var dependentTargetId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentId = Guid.Empty // Initially empty - will be populated by first round events
        };
        var projections = new List<FactoryTestProjection> { projection };

        // First round event that populates the dependentId
        var firstRoundEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentId"),
            payload = JObject.FromObject(new { dependentId = dependentTargetId })
        };

        // Second round event for the dependent entity (after dependentId is known)
        var dependentEntityEvent = new Event
        {
            aggregateRootId = dependentTargetId,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateDependentEntity"),
            payload = JObject.FromObject(new { name = "DependentEntityName" })
        };

        var allEvents = new List<Event> { firstRoundEvent, dependentEntityEvent };
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(allEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // First selector gets the siteId events (which will populate dependentId)
        factory.WithSameServiceIdSelectors(p => p.siteId);
        // Dependant selector gets events for the dependentId (only known after first events applied)
        factory.WithSameServiceDependantIdSelectors(p => p.dependentId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should have events: one for siteId, one for dependentId (populated after first round)
        Assert.True(result.Count >= 1, "Should have at least one ExternalDataEvent");
        
        // Verify the dependent entity event was fetched
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentTargetId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantListIdSelector_WhenListIsInitiallyEmpty_PopulatesAfterFirstRoundEvents()
    {
        // Arrange
        var dependentId1 = Guid.NewGuid();
        var dependentId2 = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentListIds = new List<Guid>() // Initially empty
        };
        var projections = new List<FactoryTestProjection> { projection };

        // First round event that populates the dependentListIds
        var firstRoundEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentListIds"),
            payload = JObject.FromObject(new { dependentListIds = new List<Guid> { dependentId1, dependentId2 } })
        };

        // Events for the dependent entities
        var dependentEvent1 = new Event
        {
            aggregateRootId = dependentId1,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateDependent1")
        };
        var dependentEvent2 = new Event
        {
            aggregateRootId = dependentId2,
            timestamp = DateTime.UtcNow.AddMinutes(-20),
            command = new NostifyCommand("CreateDependent2")
        };

        var allEvents = new List<Event> { firstRoundEvent, dependentEvent1, dependentEvent2 };
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(allEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceDependantListIdSelectors(p => p.dependentListIds);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify both dependent entity events were fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId1);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId2);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantSelector_WhenIdRemainsEmpty_DoesNotFetchExtraEvents()
    {
        // Arrange - projection where dependentId is never populated by events
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentId = Guid.Empty // Will remain empty
        };
        var projections = new List<FactoryTestProjection> { projection };

        // First round event that does NOT populate dependentId
        var firstRoundEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("UpdateName"),
            payload = JObject.FromObject(new { name = "NewName" })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { firstRoundEvent });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceDependantIdSelectors(p => p.dependentId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should only have the first round event
        Assert.Single(result);
        var returnedEvents = result[0].events;
        Assert.Single(returnedEvents);
        Assert.Equal(projection.siteId, returnedEvents[0].aggregateRootId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantSelector_DoesNotDuplicateEventsAlreadyFetched()
    {
        // Arrange - scenario where the dependentId is the same as an already-fetched ID
        var sharedId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = sharedId, // This will be fetched in first round
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentId = Guid.Empty
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Event that sets dependentId to the same value as siteId
        var firstRoundEvent = new Event
        {
            aggregateRootId = sharedId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentIdToSiteId"),
            payload = JObject.FromObject(new { dependentId = sharedId }) // Same as siteId
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { firstRoundEvent });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceDependantIdSelectors(p => p.dependentId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should only have one ExternalDataEvent (not duplicated)
        Assert.Single(result);
        // The event should only appear once
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        Assert.Single(allReturnedEvents);
    }

    [Fact]
    public async Task GetEventsAsync_WithMultipleProjections_DependantSelectorsWorkForEach()
    {
        // Arrange
        var dependent1 = Guid.NewGuid();
        var dependent2 = Guid.NewGuid();
        
        var projection1 = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "Projection1",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentId = Guid.Empty
        };
        var projection2 = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "Projection2",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentId = Guid.Empty
        };
        var projections = new List<FactoryTestProjection> { projection1, projection2 };

        // First round events that populate different dependentIds for each projection
        var firstRoundEvent1 = new Event
        {
            aggregateRootId = projection1.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependent"),
            payload = JObject.FromObject(new { dependentId = dependent1 })
        };
        var firstRoundEvent2 = new Event
        {
            aggregateRootId = projection2.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-29),
            command = new NostifyCommand("SetDependent"),
            payload = JObject.FromObject(new { dependentId = dependent2 })
        };

        // Dependent entity events
        var dependentEvent1 = new Event
        {
            aggregateRootId = dependent1,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateDependent1")
        };
        var dependentEvent2 = new Event
        {
            aggregateRootId = dependent2,
            timestamp = DateTime.UtcNow.AddMinutes(-24),
            command = new NostifyCommand("CreateDependent2")
        };

        var allEvents = new List<Event> { firstRoundEvent1, firstRoundEvent2, dependentEvent1, dependentEvent2 };
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(allEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceDependantIdSelectors(p => p.dependentId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Both dependent events should be fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependent1);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependent2);
    }

    [Fact]
    public async Task GetEventsAsync_WithPointInTime_DependantEventsRespectTimeFilter()
    {
        // Arrange
        var pointInTime = DateTime.UtcNow.AddMinutes(-20);
        var dependentId = Guid.NewGuid();
        
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentId = Guid.Empty
        };
        var projections = new List<FactoryTestProjection> { projection };

        // First round event that populates dependentId (before pointInTime)
        var firstRoundEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = pointInTime.AddMinutes(-15),
            command = new NostifyCommand("SetDependent"),
            payload = JObject.FromObject(new { dependentId = dependentId })
        };

        // Dependent event before pointInTime (should be included)
        var dependentEventBefore = new Event
        {
            aggregateRootId = dependentId,
            timestamp = pointInTime.AddMinutes(-10),
            command = new NostifyCommand("CreateDependent")
        };

        // Dependent event after pointInTime (should be excluded)
        var dependentEventAfter = new Event
        {
            aggregateRootId = dependentId,
            timestamp = pointInTime.AddMinutes(5),
            command = new NostifyCommand("UpdateDependent")
        };

        var allEvents = new List<Event> { firstRoundEvent, dependentEventBefore, dependentEventAfter };
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(allEvents);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient: null,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceDependantIdSelectors(p => p.dependentId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Should include the dependent event before pointInTime
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId && e.timestamp <= pointInTime);
        // Should NOT include the dependent event after pointInTime
        Assert.DoesNotContain(allReturnedEvents, e => e.aggregateRootId == dependentId && e.timestamp > pointInTime);
    }

    [Fact]
    public async Task GetEventsAsync_WithNoDependantSelectors_SkipsDependantProcessing()
    {
        // Arrange
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>()
        };
        var projections = new List<FactoryTestProjection> { projection };

        var testEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("CreateSite")
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { testEvent });

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Only add primary selectors, no dependant selectors
        factory.WithSameServiceIdSelectors(p => p.siteId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Single(result[0].events);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantIdSelector_WhenIdPopulatedByExternalServiceEvents_FetchesDependantEvents()
    {
        // Arrange
        // Projection starts with dependentId = Guid.Empty
        // External service event will populate the dependentId
        // Then dependent selector should fetch events for that ID from local service
        var dependentTargetId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            externalId = Guid.NewGuid(),
            dependentId = Guid.Empty // Initially empty - will be populated by external service events
        };
        var projections = new List<FactoryTestProjection> { projection };

        // External service event that populates the dependentId
        var externalEvent = new Event
        {
            aggregateRootId = projection.externalId!.Value,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentIdFromExternal"),
            payload = JObject.FromObject(new { dependentId = dependentTargetId })
        };

        // Local event for the dependent entity (after dependentId is known from external events)
        var dependentEntityEvent = new Event
        {
            aggregateRootId = dependentTargetId,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateDependentEntity"),
            payload = JObject.FromObject(new { name = "DependentEntityName" })
        };

        // Local events include only the dependent entity event
        var localEvents = new List<Event> { dependentEntityEvent };
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(localEvents);

        // External events include the event that populates dependentId
        var externalEvents = new List<IEvent> { externalEvent };
        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://external-service.com/events", externalEvents);
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        // External event requestor to get events that populate dependentId
        factory.WithEventRequestor("https://external-service.com/events", p => p.externalId);
        // Dependant selector gets events for the dependentId (only known after external events applied)
        factory.WithSameServiceDependantIdSelectors(p => p.dependentId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify the external event was fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.externalId!.Value);
        // Verify the dependent entity event was also fetched (populated by external event)
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentTargetId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantListIdSelector_WhenListPopulatedByExternalServiceEvents_FetchesDependantEvents()
    {
        // Arrange
        var dependentId1 = Guid.NewGuid();
        var dependentId2 = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            externalId = Guid.NewGuid(),
            dependentListIds = new List<Guid>() // Initially empty - populated by external events
        };
        var projections = new List<FactoryTestProjection> { projection };

        // External event that populates the dependentListIds
        var externalEvent = new Event
        {
            aggregateRootId = projection.externalId!.Value,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentListFromExternal"),
            payload = JObject.FromObject(new { dependentListIds = new List<Guid> { dependentId1, dependentId2 } })
        };

        // Local events for the dependent entities
        var dependentEvent1 = new Event
        {
            aggregateRootId = dependentId1,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateDependent1")
        };
        var dependentEvent2 = new Event
        {
            aggregateRootId = dependentId2,
            timestamp = DateTime.UtcNow.AddMinutes(-20),
            command = new NostifyCommand("CreateDependent2")
        };

        var localEvents = new List<Event> { dependentEvent1, dependentEvent2 };
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(localEvents);

        var externalEvents = new List<IEvent> { externalEvent };
        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://external-service.com/events", externalEvents);
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithEventRequestor("https://external-service.com/events", p => p.externalId);
        factory.WithSameServiceDependantListIdSelectors(p => p.dependentListIds);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify the external event was fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.externalId!.Value);
        // Verify both dependent entity events were fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId1);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId2);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantSelector_WhenIdPopulatedByBothLocalAndExternalEvents_FetchesAllDependantEvents()
    {
        // Arrange - scenario where both local and external events contribute to populating dependent IDs
        var dependentFromLocal = Guid.NewGuid();
        var dependentFromExternal = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            externalId = Guid.NewGuid(),
            dependentListIds = new List<Guid>() // Initially empty
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local event that adds first dependent ID to list
        var localFirstRoundEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-35),
            command = new NostifyCommand("AddLocalDependent"),
            payload = JObject.FromObject(new { dependentListIds = new List<Guid> { dependentFromLocal } })
        };

        // External event that adds second dependent ID to list (applied after local, overwrites)
        // In real scenario, Apply would merge - for test purposes, external event sets the final state
        var externalEvent = new Event
        {
            aggregateRootId = projection.externalId!.Value,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("AddExternalDependent"),
            payload = JObject.FromObject(new { dependentListIds = new List<Guid> { dependentFromLocal, dependentFromExternal } })
        };

        // Events for both dependent entities
        var dependentLocalEvent = new Event
        {
            aggregateRootId = dependentFromLocal,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateLocalDependent")
        };
        var dependentExternalEvent = new Event
        {
            aggregateRootId = dependentFromExternal,
            timestamp = DateTime.UtcNow.AddMinutes(-20),
            command = new NostifyCommand("CreateExternalDependent")
        };

        var localEvents = new List<Event> { localFirstRoundEvent, dependentLocalEvent, dependentExternalEvent };
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(localEvents);

        var externalEvents = new List<IEvent> { externalEvent };
        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://external-service.com/events", externalEvents);
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithEventRequestor("https://external-service.com/events", p => p.externalId);
        factory.WithSameServiceDependantListIdSelectors(p => p.dependentListIds);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Both dependent events should be fetched regardless of whether ID came from local or external
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentFromLocal);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentFromExternal);
    }

    #endregion

    #region AddDependantEventRequestors / WithDependantEventRequestor Tests

    [Fact]
    public void AddDependantEventRequestors_WithHttpClient_AddsRequestors()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        var eventRequestor = new EventRequester<FactoryTestProjection>(
            "https://dependent-service.com/events",
            p => p.externalId);

        // Act - should not throw
        factory.AddDependantEventRequestors(eventRequestor);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void AddDependantEventRequestors_WithoutHttpClient_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            null); // No HTTP client

        var eventRequestor = new EventRequester<FactoryTestProjection>(
            "https://dependent-service.com/events",
            p => p.externalId);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.AddDependantEventRequestors(eventRequestor));

        Assert.Contains("HttpClient is not provided", exception.Message);
    }

    [Fact]
    public void WithDependantEventRequestor_WithHttpClient_AddsRequestor()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        // Act - should not throw
        factory.WithDependantEventRequestor(
            "https://dependent-service.com/events",
            p => p.externalId);

        // Assert - no exception means success
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithDependantEventRequestor_WithoutHttpClient_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            null); // No HTTP client

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.WithDependantEventRequestor(
                "https://dependent-service.com/events",
                p => p.externalId));

        Assert.Contains("HttpClient is not provided", exception.Message);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_WhenIdPopulatedByLocalEvents_FetchesDependantExternalEvents()
    {
        // Arrange
        // Projection has dependentExternalId = null initially
        // Local event populates dependentExternalId
        // Then dependent external requestor should fetch events from external service for that ID
        var dependentExternalId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentExternalId = null // Initially null - will be populated by local events
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local event that populates the dependentExternalId
        var localEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentExternalId"),
            payload = JObject.FromObject(new { dependentExternalId = dependentExternalId })
        };

        var localEvents = new List<Event> { localEvent };
        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(localEvents);

        // External events from the dependent service
        var dependentExternalEvent = new Event
        {
            aggregateRootId = dependentExternalId,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateDependentExternal"),
            payload = JObject.FromObject(new { name = "DependentExternalName" })
        };
        var externalEvents = new List<IEvent> { dependentExternalEvent };
        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://dependent-external-service.com/events", externalEvents);
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        // First get local events to populate dependentExternalId
        factory.WithSameServiceIdSelectors(p => p.siteId);
        // Then use dependent external requestor to fetch from external service
        factory.WithDependantEventRequestor("https://dependent-external-service.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify local event was fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.siteId);
        // Verify dependent external event was also fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentExternalId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_WhenIdPopulatedByExternalEvents_FetchesDependantExternalEvents()
    {
        // Arrange
        // Chain: external event -> populates dependentExternalId -> fetch from another external service
        var dependentExternalId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            externalId = Guid.NewGuid(),
            dependentExternalId = null // Initially null - will be populated by first external event
        };
        var projections = new List<FactoryTestProjection> { projection };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event>());

        // First external event that populates dependentExternalId
        var firstExternalEvent = new Event
        {
            aggregateRootId = projection.externalId!.Value,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentExternalId"),
            payload = JObject.FromObject(new { dependentExternalId = dependentExternalId })
        };

        // Second external event from dependent service
        var dependentExternalEvent = new Event
        {
            aggregateRootId = dependentExternalId,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateDependentExternal")
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://first-external-service.com/events", new List<IEvent> { firstExternalEvent });
        mockHandler.AddService("https://dependent-external-service.com/events", new List<IEvent> { dependentExternalEvent });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        // First external requestor populates dependentExternalId
        factory.WithEventRequestor("https://first-external-service.com/events", p => p.externalId);
        // Dependent external requestor fetches from second service using populated ID
        factory.WithDependantEventRequestor("https://dependent-external-service.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify first external event was fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.externalId!.Value);
        // Verify dependent external event was also fetched (chained from first external)
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentExternalId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_WhenIdRemainsNull_DoesNotFetchExtraEvents()
    {
        // Arrange
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentExternalId = null // Will remain null
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local event that does NOT populate dependentExternalId
        var localEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("UpdateName"),
            payload = JObject.FromObject(new { name = "NewName" })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { localEvent });

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://dependent-external-service.com/events", new List<IEvent>());
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithDependantEventRequestor("https://dependent-external-service.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should only have the local event, no dependent external events since ID was never populated
        Assert.Single(result);
        var returnedEvents = result[0].events;
        Assert.Single(returnedEvents);
        Assert.Equal(projection.siteId, returnedEvents[0].aggregateRootId);
    }

    [Fact]
    public async Task GetEventsAsync_WithMultipleDependantEventRequestors_FetchesFromAllServices()
    {
        // Arrange
        var dependentId1 = Guid.NewGuid();
        var dependentId2 = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentExternalId = null,
            anotherDependentExternalId = null
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local event that populates both dependent external IDs
        var localEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentIds"),
            payload = JObject.FromObject(new { 
                dependentExternalId = dependentId1,
                anotherDependentExternalId = dependentId2 
            })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { localEvent });

        // Events from different external services
        var service1Event = new Event
        {
            aggregateRootId = dependentId1,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("Service1Event")
        };
        var service2Event = new Event
        {
            aggregateRootId = dependentId2,
            timestamp = DateTime.UtcNow.AddMinutes(-20),
            command = new NostifyCommand("Service2Event")
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://service1.com/events", new List<IEvent> { service1Event });
        mockHandler.AddService("https://service2.com/events", new List<IEvent> { service2Event });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithDependantEventRequestor("https://service1.com/events", p => p.dependentExternalId);
        factory.WithDependantEventRequestor("https://service2.com/events", p => p.anotherDependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify events from both dependent external services were fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId1);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId2);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_ThreeLevelChain_LocalToExternalToDependentExternal()
    {
        // Arrange - Three-level chain:
        // 1. Local event populates externalId
        // 2. External service 1 returns event that populates dependentExternalId  
        // 3. External service 2 returns event for dependent external
        var dependentExternalId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            externalId = Guid.NewGuid(),
            dependentExternalId = null // Will be populated by external event
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local events for siteId
        var localEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-40),
            command = new NostifyCommand("CreateSite"),
            payload = JObject.FromObject(new { })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { localEvent });

        // External event from service 1 that populates dependentExternalId
        var firstExternalEvent = new Event
        {
            aggregateRootId = projection.externalId!.Value,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentExternalId"),
            payload = JObject.FromObject(new { dependentExternalId = dependentExternalId })
        };

        // External event from service 2 (the dependent)
        var dependentExternalEvent = new Event
        {
            aggregateRootId = dependentExternalId,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateDependentResource"),
            payload = JObject.FromObject(new { })
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://service1.com/events", new List<IEvent> { firstExternalEvent });
        mockHandler.AddService("https://service2.com/events", new List<IEvent> { dependentExternalEvent });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithEventRequestor("https://service1.com/events", p => p.externalId);
        factory.WithDependantEventRequestor("https://service2.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify all three levels of events were fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.siteId);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.externalId!.Value);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentExternalId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_MultipleProjections_EachGetsDifferentDependentEvents()
    {
        // Arrange - Multiple projections where each gets a different dependent external event
        var dependentId1 = Guid.NewGuid();
        var dependentId2 = Guid.NewGuid();
        var projection1 = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection1",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentExternalId = null
        };
        var projection2 = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection2",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentExternalId = null
        };
        var projections = new List<FactoryTestProjection> { projection1, projection2 };

        // Local events that populate different dependentExternalIds for each projection
        var localEvent1 = new Event
        {
            aggregateRootId = projection1.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependent1"),
            payload = JObject.FromObject(new { dependentExternalId = dependentId1 })
        };
        var localEvent2 = new Event
        {
            aggregateRootId = projection2.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependent2"),
            payload = JObject.FromObject(new { dependentExternalId = dependentId2 })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { localEvent1, localEvent2 });

        // Dependent external events for each projection
        var dependentEvent1 = new Event
        {
            aggregateRootId = dependentId1,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("DependentEvent1")
        };
        var dependentEvent2 = new Event
        {
            aggregateRootId = dependentId2,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("DependentEvent2")
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://dependent-service.com/events", new List<IEvent> { dependentEvent1, dependentEvent2 });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithDependantEventRequestor("https://dependent-service.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify both projections got their respective dependent events
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId1);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId2);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_WithPointInTime_FiltersExternalEvents()
    {
        // Arrange
        var dependentExternalId = Guid.NewGuid();
        var pointInTime = DateTime.UtcNow.AddMinutes(-20);
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentExternalId = null
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local event that populates dependentExternalId (before pointInTime)
        var localEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-40),
            command = new NostifyCommand("SetDependentExternalId"),
            payload = JObject.FromObject(new { dependentExternalId = dependentExternalId })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { localEvent });

        // Two dependent external events - one before and one after pointInTime
        var eventBeforePointInTime = new Event
        {
            aggregateRootId = dependentExternalId,
            timestamp = DateTime.UtcNow.AddMinutes(-30), // Before pointInTime - should be included
            command = new NostifyCommand("BeforeEvent")
        };
        var eventAfterPointInTime = new Event
        {
            aggregateRootId = dependentExternalId,
            timestamp = DateTime.UtcNow.AddMinutes(-10), // After pointInTime - should be excluded
            command = new NostifyCommand("AfterEvent")
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://dependent-service.com/events", new List<IEvent> { eventBeforePointInTime, eventAfterPointInTime });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            pointInTime: pointInTime,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithDependantEventRequestor("https://dependent-service.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify only the event before pointInTime was included from external service
        Assert.Contains(allReturnedEvents, e => e.command.name == "BeforeEvent");
        Assert.DoesNotContain(allReturnedEvents, e => e.command.name == "AfterEvent");
    }

    [Fact]
    public async Task GetEventsAsync_WithBothDependantSelectorsAndDependantRequestors_CombinesBoth()
    {
        // Arrange - Uses both same-service dependent selectors AND dependent external requestors
        var localDependentId = Guid.NewGuid();
        var externalDependentId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentId = Guid.Empty, // Will be populated by local event
            dependentExternalId = null // Will be populated by local event
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local event that populates both local dependent ID and external dependent ID
        var localEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetBothDependents"),
            payload = JObject.FromObject(new { 
                dependentId = localDependentId,
                dependentExternalId = externalDependentId 
            })
        };

        // Local event for the local dependent entity
        var localDependentEvent = new Event
        {
            aggregateRootId = localDependentId,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("CreateLocalDependent"),
            payload = JObject.FromObject(new { })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { localEvent, localDependentEvent });

        // External event for the external dependent entity
        var externalDependentEvent = new Event
        {
            aggregateRootId = externalDependentId,
            timestamp = DateTime.UtcNow.AddMinutes(-20),
            command = new NostifyCommand("CreateExternalDependent"),
            payload = JObject.FromObject(new { })
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://dependent-service.com/events", new List<IEvent> { externalDependentEvent });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithSameServiceDependantIdSelectors(p => p.dependentId);
        factory.WithDependantEventRequestor("https://dependent-service.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify all events were fetched: local site, local dependent, external dependent
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.siteId);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == localDependentId);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == externalDependentId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_WhenIdPopulatedByInitialExternalRequestor_FetchesDependentEvents()
    {
        // Arrange - ID is populated by first external requestor, then dependent external requestor fetches
        // This tests that both _eventRequestors and _dependantEventRequestors work together
        var dependentExternalId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            externalId = Guid.NewGuid(),
            anotherExternalId = Guid.NewGuid(),
            dependentExternalId = null // Initially null, will be set by externalId's events
        };
        var projections = new List<FactoryTestProjection> { projection };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event>());

        // Initial external event that sets dependentExternalId
        var initialExternalEvent = new Event
        {
            aggregateRootId = projection.externalId!.Value,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentId"),
            payload = JObject.FromObject(new { dependentExternalId = dependentExternalId })
        };
        var anotherExternalEvent = new Event
        {
            aggregateRootId = projection.anotherExternalId!.Value,
            timestamp = DateTime.UtcNow.AddMinutes(-28),
            command = new NostifyCommand("OtherExternalEvent"),
            payload = JObject.FromObject(new { })
        };

        // Dependent external event
        var dependentEvent = new Event
        {
            aggregateRootId = dependentExternalId,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("DependentExternalEvent"),
            payload = JObject.FromObject(new { })
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://external1.com/events", new List<IEvent> { initialExternalEvent });
        mockHandler.AddService("https://external2.com/events", new List<IEvent> { anotherExternalEvent });
        mockHandler.AddService("https://dependent.com/events", new List<IEvent> { dependentEvent });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        // Setup: two initial external requestors, one dependent external requestor
        factory.WithEventRequestor("https://external1.com/events", p => p.externalId);
        factory.WithEventRequestor("https://external2.com/events", p => p.anotherExternalId);
        factory.WithDependantEventRequestor("https://dependent.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify all events were fetched including the dependent one
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.externalId!.Value);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.anotherExternalId!.Value);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentExternalId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_WhenExternalServiceReturnsEmpty_DoesNotFail()
    {
        // Arrange
        var dependentExternalId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentExternalId = null
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local event that populates dependentExternalId
        var localEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentExternalId"),
            payload = JObject.FromObject(new { dependentExternalId = dependentExternalId })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { localEvent });

        // External service returns empty list (no events for that ID)
        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://dependent-service.com/events", new List<IEvent>());
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        factory.WithDependantEventRequestor("https://dependent-service.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        // Should still have the local event even if dependent external returned nothing
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.siteId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_SameServiceForInitialAndDependent_WorksCorrectly()
    {
        // Arrange - Both initial and dependent requestors point to same service but different IDs
        var dependentExternalId = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            externalId = Guid.NewGuid(),
            dependentExternalId = null
        };
        var projections = new List<FactoryTestProjection> { projection };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event>());

        // Initial external event that sets dependentExternalId
        var initialExternalEvent = new Event
        {
            aggregateRootId = projection.externalId!.Value,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentId"),
            payload = JObject.FromObject(new { dependentExternalId = dependentExternalId })
        };

        // Dependent external event from SAME service
        var dependentEvent = new Event
        {
            aggregateRootId = dependentExternalId,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("DependentEvent")
        };

        // Same service provides both sets of events
        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://same-service.com/events", new List<IEvent> { initialExternalEvent, dependentEvent });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithEventRequestor("https://same-service.com/events", p => p.externalId);
        factory.WithDependantEventRequestor("https://same-service.com/events", p => p.dependentExternalId);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Both should be fetched even though from same service
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == projection.externalId!.Value);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentExternalId);
    }

    [Fact]
    public async Task GetEventsAsync_WithDependantEventRequestor_WithListSelector_FetchesMultipleDependentExternalEvents()
    {
        // Arrange - Tests using list selector for dependent external IDs
        var dependentId1 = Guid.NewGuid();
        var dependentId2 = Guid.NewGuid();
        var dependentId3 = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            name = "TestProjection",
            siteId = Guid.NewGuid(),
            ownerId = Guid.NewGuid(),
            categoryId = Guid.NewGuid(),
            tagIds = new List<Guid>(),
            dependentExternalIds = new List<Guid>() // Initially empty
        };
        var projections = new List<FactoryTestProjection> { projection };

        // Local event that populates multiple dependent external IDs
        var localEvent = new Event
        {
            aggregateRootId = projection.siteId,
            timestamp = DateTime.UtcNow.AddMinutes(-30),
            command = new NostifyCommand("SetDependentExternalIds"),
            payload = JObject.FromObject(new { dependentExternalIds = new List<Guid> { dependentId1, dependentId2, dependentId3 } })
        };

        var (mockNostify, mockContainer) = CreateMockNostifyWithEvents(new List<Event> { localEvent });

        // External events for each dependent ID
        var dependentEvent1 = new Event
        {
            aggregateRootId = dependentId1,
            timestamp = DateTime.UtcNow.AddMinutes(-25),
            command = new NostifyCommand("DependentEvent1")
        };
        var dependentEvent2 = new Event
        {
            aggregateRootId = dependentId2,
            timestamp = DateTime.UtcNow.AddMinutes(-24),
            command = new NostifyCommand("DependentEvent2")
        };
        var dependentEvent3 = new Event
        {
            aggregateRootId = dependentId3,
            timestamp = DateTime.UtcNow.AddMinutes(-23),
            command = new NostifyCommand("DependentEvent3")
        };

        var mockHandler = new MultiServiceMockHttpHandler();
        mockHandler.AddService("https://dependent-service.com/events", new List<IEvent> { dependentEvent1, dependentEvent2, dependentEvent3 });
        var httpClient = new HttpClient(mockHandler);

        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            mockNostify.Object,
            projections,
            httpClient,
            queryExecutor: InMemoryQueryExecutor.Default);

        factory.WithSameServiceIdSelectors(p => p.siteId);
        
        // Use AddDependantEventRequestors with EventRequester that supports list selectors
        var listRequestor = new EventRequester<FactoryTestProjection>(
            "https://dependent-service.com/events",
            new Func<FactoryTestProjection, List<Guid>>[] { p => p.dependentExternalIds });
        factory.AddDependantEventRequestors(listRequestor);

        // Act
        var result = await factory.GetEventsAsync();

        // Assert
        Assert.NotNull(result);
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify all three dependent external events were fetched
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId1);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId2);
        Assert.Contains(allReturnedEvents, e => e.aggregateRootId == dependentId3);
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
    
    /// <summary>
    /// A dependent ID that gets populated by events (e.g., parentId that is set when a parent is assigned)
    /// </summary>
    public Guid dependentId { get; set; } = Guid.Empty;
    
    /// <summary>
    /// A list of dependent IDs that gets populated by events
    /// </summary>
    public List<Guid> dependentListIds { get; set; } = new List<Guid>();
    
    /// <summary>
    /// A dependent external ID that gets populated by events and is used to fetch from another external service
    /// </summary>
    public Guid? dependentExternalId { get; set; }
    
    /// <summary>
    /// Another dependent external ID for testing multiple dependent external requestors
    /// </summary>
    public Guid? anotherDependentExternalId { get; set; }
    
    /// <summary>
    /// A list of dependent external IDs for testing list selectors with external requestors
    /// </summary>
    public List<Guid> dependentExternalIds { get; set; } = new List<Guid>();
    
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
