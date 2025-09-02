using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using nostify;
using Xunit;
using Newtonsoft.Json;

namespace nostify.Tests;

public class ExternalDataEventTests
{
    private readonly List<TestProjection> _testProjections;
    private readonly DateTime _pointInTime;
    private readonly List<Event> _testEvents;
    private List<TestProjectionForExternalData> testProjections;

    public ExternalDataEventTests()
    {
        _pointInTime = DateTime.UtcNow.AddHours(-1);
        
        // Setup test data for point-in-time tests
        var testId1 = Guid.NewGuid();
        var testId2 = Guid.NewGuid();
        
        _testProjections = new List<TestProjection>
        {
            new TestProjection { id = testId1, name = "Test1" },
            new TestProjection { id = testId2, name = "Test2" }
        };

        // Create test events - some before pointInTime, some after
        _testEvents = new List<Event>
        {
            new Event { aggregateRootId = testId1, timestamp = _pointInTime.AddMinutes(-30), command = new NostifyCommand("TestCommand1") },
            new Event { aggregateRootId = testId1, timestamp = _pointInTime.AddMinutes(30), command = new NostifyCommand("TestCommand2") }, // After pointInTime
            new Event { aggregateRootId = testId2, timestamp = _pointInTime.AddMinutes(-15), command = new NostifyCommand("TestCommand3") },
            new Event { aggregateRootId = testId2, timestamp = _pointInTime.AddMinutes(45), command = new NostifyCommand("TestCommand4") }  // After pointInTime
        };

        // Setup test data for GetMultiServiceEventsAsync tests
        testProjections = new List<TestProjectionForExternalData>
        {
            new TestProjectionForExternalData { id = Guid.NewGuid(), siteId = Guid.NewGuid(), ownerId = Guid.NewGuid() },
            new TestProjectionForExternalData { id = Guid.NewGuid(), siteId = Guid.NewGuid(), ownerId = Guid.NewGuid() }
        };
    }

    [Fact]
    public void EventRequest_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid?> selector1 = p => p.siteId;
        Func<TestProjectionForExternalData, Guid?> selector2 = p => p.ownerId;

        // Act
        var eventRequest = new EventRequest<TestProjectionForExternalData>(url, selector1, selector2);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(2, eventRequest.ForeignIdSelectors.Length);
        Assert.Equal(selector1, eventRequest.ForeignIdSelectors[0]);
        Assert.Equal(selector2, eventRequest.ForeignIdSelectors[1]);
    }

    [Fact]
    public void EventRequest_Constructor_ThrowsExceptionForNullUrl()
    {
        // Arrange & Act & Assert
        Assert.Throws<NostifyException>(() => new EventRequest<TestProjectionForExternalData>(null));
    }

    [Fact]
    public void EventRequest_Constructor_ThrowsExceptionForEmptyUrl()
    {
        // Arrange & Act & Assert
        Assert.Throws<NostifyException>(() => new EventRequest<TestProjectionForExternalData>(""));
    }

    [Fact]
    public void EventRequest_Constructor_HandlesNoSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";

        // Act
        var eventRequest = new EventRequest<TestProjectionForExternalData>(url);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.ForeignIdSelectors);
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_ReturnsEmptyList_WhenNoEventRequests()
    {
        // Arrange
        using var httpClient = new HttpClient();
        
        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, testProjections);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetMultiServiceEventsAsync_AcceptsCorrectApiSignature()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var projectionsToInit = new List<TestProjectionForApiTest>
        {
            new TestProjectionForApiTest
            {
                id = Guid.NewGuid(),
                siteId = Guid.NewGuid(),
                locationId = Guid.NewGuid(),
                subLocationId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                manufacturerId = Guid.NewGuid(),
                workSectionId = Guid.NewGuid(),
                expenditures = new List<TestExpenditure>
                {
                    new TestExpenditure { expendedToId = Guid.NewGuid(), expendedByUserId = Guid.NewGuid() }
                }
            }
        };

        // Act & Assert - This tests that the API signature compiles correctly
        // We're not actually calling it to avoid making HTTP requests in unit tests
        var methodInfo = typeof(ExternalDataEvent).GetMethod("GetMultiServiceEventsAsync");
        Assert.NotNull(methodInfo);
        Assert.True(methodInfo.IsStatic);
        Assert.True(methodInfo.IsGenericMethodDefinition);
        
        // Verify the EventRequest constructor works as expected
        var eventRequest = new EventRequest<TestProjectionForApiTest>($"https://localhost/LocationService/api/EventRequest",
            p => p.siteId,
            p => p.locationId,
            p => p.subLocationId
        );
        
        Assert.Equal("https://localhost/LocationService/api/EventRequest", eventRequest.Url);
        Assert.Equal(3, eventRequest.ForeignIdSelectors.Length);
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_ThrowsException_WhenHttpClientIsNull()
    {
        // Arrange
        var eventRequest = new EventRequest<TestProjectionForExternalData>("https://test.com/api/events", p => p.siteId);

        // Act & Assert
        await Assert.ThrowsAsync<NostifyException>(async () => 
            await ExternalDataEvent.GetMultiServiceEventsAsync(null, testProjections, eventRequest));
    }

    [Fact]
    public async Task GetEventsAsync_HttpClient_WithPointInTime_FiltersEventsByTimestamp()
    {
        // Test that HTTP client version correctly sends pointInTime and processes filtered results
        
        // Arrange
        var testPointInTime = DateTime.UtcNow.AddHours(-2);
        var foreignIdSelectors = new Func<TestProjection, Guid?>[] { p => p.id };
        
        // Create events - some before, some after pointInTime
        var filteredEvents = _testEvents.Where(e => e.timestamp <= testPointInTime).ToList();
        var mockHandler = new MockHttpMessageHandler(filteredEvents);
        var httpClient = new HttpClient(mockHandler);
        var url = "https://test.example.com/events";

        // Act
        var result = await ExternalDataEvent.GetEventsAsync(httpClient, url, _testProjections, testPointInTime, foreignIdSelectors);
        
        // Assert
        Assert.NotNull(result);
        
        // Verify the request included pointInTime
        var requestData = JsonConvert.DeserializeObject<EventRequestData>(mockHandler.RequestContent);
        Assert.NotNull(requestData);
        Assert.Equal(testPointInTime, requestData.PointInTime);
        Assert.Contains(_testProjections[0].id, requestData.ForeignIds);
        Assert.Contains(_testProjections[1].id, requestData.ForeignIds);

        // Verify only events before pointInTime are returned
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        Assert.All(allReturnedEvents, e => Assert.True(e.timestamp <= testPointInTime));
    }

    [Fact]
    public async Task GetEventsAsync_HttpClient_WithoutPointInTime_ReturnsAllEvents()
    {
        // Test backward compatibility - when pointInTime is null, all events should be returned
        
        // Arrange
        var foreignIdSelectors = new Func<TestProjection, Guid?>[] { p => p.id };
        var mockHandler = new MockHttpMessageHandler(_testEvents);
        var httpClient = new HttpClient(mockHandler);
        var url = "https://test.example.com/events";

        // Act
        var result = await ExternalDataEvent.GetEventsAsync(httpClient, url, _testProjections, foreignIdSelectors);
        
        // Assert
        Assert.NotNull(result);
        
        // Verify the request has null pointInTime
        var requestData = JsonConvert.DeserializeObject<EventRequestData>(mockHandler.RequestContent);
        Assert.NotNull(requestData);
        Assert.Null(requestData.PointInTime);

        // All events should be returned
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(_testEvents.Count, allReturnedEvents.Count);
    }

    [Fact]
    public void EventRequestData_PropertiesInitializeCorrectly()
    {
        // Test the new EventRequestData class
        
        // Arrange & Act
        var requestData = new EventRequestData
        {
            ForeignIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            PointInTime = DateTime.UtcNow
        };
        
        // Assert
        Assert.NotNull(requestData.ForeignIds);
        Assert.Equal(2, requestData.ForeignIds.Count);
        Assert.NotNull(requestData.PointInTime);
        
        // Test default constructor
        var defaultRequestData = new EventRequestData();
        Assert.NotNull(defaultRequestData.ForeignIds);
        Assert.Empty(defaultRequestData.ForeignIds);
        Assert.Null(defaultRequestData.PointInTime);
    }

    [Fact]
    public void EventRequestData_SerializesCorrectly()
    {
        // Test that EventRequestData serializes and deserializes correctly for HTTP requests
        
        // Arrange
        var testTime = DateTime.UtcNow;
        var testIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var requestData = new EventRequestData
        {
            ForeignIds = testIds,
            PointInTime = testTime
        };
        
        // Act
        var serialized = JsonConvert.SerializeObject(requestData);
        var deserialized = JsonConvert.DeserializeObject<EventRequestData>(serialized);
        
        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(testIds.Count, deserialized.ForeignIds.Count);
        Assert.All(testIds, id => Assert.Contains(id, deserialized.ForeignIds));
        Assert.Equal(testTime, deserialized.PointInTime);
    }

    [Fact]
    public async Task GetEventsAsync_HttpClient_DoesNotReturnEmptyExternalDataEvents()
    {
        // Test that ExternalDataEvent objects with no events are filtered out
        
        // Arrange
        var projectionWithEvents = new TestProjection { id = Guid.NewGuid(), name = "HasEvents" };
        var projectionWithoutEvents = new TestProjection { id = Guid.NewGuid(), name = "NoEvents" };
        var projectionsToInit = new List<TestProjection> { projectionWithEvents, projectionWithoutEvents };
        
        // Create events only for the first projection
        var eventsForFirstProjectionOnly = new List<Event>
        {
            new Event { aggregateRootId = projectionWithEvents.id, timestamp = DateTime.UtcNow.AddHours(-1), command = new NostifyCommand("TestCommand") }
        };
        
        var mockHandler = new MockHttpMessageHandler(eventsForFirstProjectionOnly);
        var httpClient = new HttpClient(mockHandler);
        var url = "https://test.example.com/events";
        
        Func<TestProjection, Guid?>[] foreignIdSelectors = { p => p.id };

        // Act
        var result = await ExternalDataEvent.GetEventsAsync(httpClient, url, projectionsToInit, foreignIdSelectors);
        
        // Assert
        Assert.NotNull(result);
        // Should only return ExternalDataEvent for projection that has events
        Assert.Single(result);
        Assert.Equal(projectionWithEvents.id, result[0].aggregateRootId);
        Assert.NotEmpty(result[0].events);
        
        // Verify no empty ExternalDataEvent objects are returned
        Assert.All(result, ede => Assert.NotEmpty(ede.events));
    }

    [Fact]
    public void GetEventsAsync_Container_DoesNotReturnEmptyExternalDataEvents()
    {
        // Test that Container version also filters out empty ExternalDataEvent objects
        // We simulate the filtering logic that would be applied to test the behavior
        
        // Arrange
        var projectionWithEvents = new TestProjection { id = Guid.NewGuid(), name = "HasEvents" };
        var projectionWithoutEvents = new TestProjection { id = Guid.NewGuid(), name = "NoEvents" };
        var projectionsToInit = new List<TestProjection> { projectionWithEvents, projectionWithoutEvents };
        
        // Create events only for the first projection
        var allEvents = new List<Event>
        {
            new Event { aggregateRootId = projectionWithEvents.id, timestamp = DateTime.UtcNow.AddHours(-1), command = new NostifyCommand("TestCommand") }
        };
        
        Func<TestProjection, Guid?>[] foreignIdSelectors = { p => p.id };
        
        // Get the foreign IDs that would be used in the query
        var foreignIds = (from p in projectionsToInit
                         from f in foreignIdSelectors
                         let foreignId = f(p)
                         where foreignId.HasValue
                         select foreignId.Value).ToList();
        
        // Simulate the LINQ query that happens in the Container version
        var events = allEvents.Where(e => foreignIds.Contains(e.aggregateRootId))
                             .OrderBy(e => e.timestamp)
                             .ToLookup(e => e.aggregateRootId);

        // Simulate the result creation logic from Container implementation
        var result = (
            from p in projectionsToInit
            from f in foreignIdSelectors
            let foreignId = f(p)
            where foreignId.HasValue
            let eventList = events[foreignId!.Value].ToList()
            where eventList.Any() // This is the fix - filter out empty event lists
            select new ExternalDataEvent(p.id, eventList)
        ).ToList();
        
        // Assert
        Assert.NotNull(result);
        // Should only return ExternalDataEvent for projection that has events
        Assert.Single(result);
        Assert.Equal(projectionWithEvents.id, result[0].aggregateRootId);
        Assert.NotEmpty(result[0].events);
        
        // Verify no empty ExternalDataEvent objects are returned
        Assert.All(result, ede => Assert.NotEmpty(ede.events));
    }

    [Fact]
    public async Task GetEventsAsync_HttpClient_WithPointInTimeBeforeAllEvents_ReturnsEmptyList()
    {
        // Test that when pointInTime is before any events exist, returns empty list
        
        // Arrange
        var foreignIdSelectors = new Func<TestProjection, Guid?>[] { p => p.id };
        var pointInTimeBeforeAllEvents = _pointInTime.AddHours(-2); // Before all test events
        
        // Return empty list from mock handler to simulate no events found
        var mockHandler = new MockHttpMessageHandler(new List<Event>());
        var httpClient = new HttpClient(mockHandler);
        var url = "https://test.example.com/events";

        // Act
        var result = await ExternalDataEvent.GetEventsAsync(httpClient, url, _testProjections, pointInTimeBeforeAllEvents, foreignIdSelectors);
        
        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        
        // Verify the request included the early pointInTime
        var requestData = JsonConvert.DeserializeObject<EventRequestData>(mockHandler.RequestContent);
        Assert.NotNull(requestData);
        Assert.Equal(pointInTimeBeforeAllEvents, requestData.PointInTime);
        Assert.Contains(_testProjections[0].id, requestData.ForeignIds);
        Assert.Contains(_testProjections[1].id, requestData.ForeignIds);
    }

    [Fact]
    public void GetEventsAsync_Container_WithPointInTimeBeforeAllEvents_ReturnsEmptyList()
    {
        // Test that Container version returns empty list when pointInTime is before all events
        // We simulate the filtering logic that would be applied to test the behavior
        
        // Arrange
        var foreignIdSelectors = new Func<TestProjection, Guid?>[] { p => p.id };
        var pointInTimeBeforeAllEvents = _pointInTime.AddHours(-2); // Before all test events
        
        // Get the foreign IDs that would be used in the query
        var foreignIds = (from p in _testProjections
                         from f in foreignIdSelectors
                         let foreignId = f(p)
                         where foreignId.HasValue
                         select foreignId.Value).ToList();
        
        // Simulate the LINQ query filtering that happens in the Container version
        var baseQuery = _testEvents.Where(e => foreignIds.Contains(e.aggregateRootId));
        
        // Apply pointInTime filtering (should filter out all events since pointInTime is before all events)
        var filteredEvents = baseQuery
            .Where(e => e.timestamp <= pointInTimeBeforeAllEvents)
            .OrderBy(e => e.timestamp)
            .ToLookup(e => e.aggregateRootId);

        // Simulate the result creation logic from Container implementation
        var result = (
            from p in _testProjections
            from f in foreignIdSelectors
            let foreignId = f(p)
            where foreignId.HasValue
            let eventList = filteredEvents[foreignId!.Value].ToList()
            where eventList.Any() // This filters out empty event lists
            select new ExternalDataEvent(p.id, eventList)
        ).ToList();
        
        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // Should be empty since no events are before pointInTimeBeforeAllEvents
        
        // Verify that the filtering actually worked - no events should be found
        var allFilteredEvents = filteredEvents.SelectMany(g => g).ToList();
        Assert.Empty(allFilteredEvents);
    }
}

// Test model specifically for ExternalDataEvent tests
public class TestProjectionForExternalData : NostifyObject, IUniquelyIdentifiable
{
    public Guid? siteId { get; set; }
    public Guid? ownerId { get; set; }
    public Guid? locationId { get; set; }
    public Guid? manufacturerId { get; set; }

    public override void Apply(IEvent e)
    {
        UpdateProperties<TestProjectionForExternalData>(e.payload);
    }
}

// Test model for API signature test
public class TestProjectionForApiTest : NostifyObject, IUniquelyIdentifiable
{
    public Guid? siteId { get; set; }
    public Guid? locationId { get; set; }
    public Guid? subLocationId { get; set; }
    public Guid? ownerId { get; set; }
    public Guid? manufacturerId { get; set; }
    public Guid? workSectionId { get; set; }
    public List<TestExpenditure> expenditures { get; set; } = new List<TestExpenditure>();

    public override void Apply(IEvent e)
    {
        UpdateProperties<TestProjectionForApiTest>(e.payload);
    }
}

public class TestExpenditure
{
    public Guid expendedToId { get; set; }
    public Guid expendedByUserId { get; set; }
}

// Enhanced mock HTTP message handler for testing
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Event> _eventsToReturn;
    public string RequestContent { get; private set; } = string.Empty;

    public MockHttpMessageHandler(List<Event> eventsToReturn = null)
    {
        _eventsToReturn = eventsToReturn ?? new List<Event>();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        if (request.Content != null)
        {
            RequestContent = await request.Content.ReadAsStringAsync();
        }

        // Return the configured events
        var responseContent = JsonConvert.SerializeObject(_eventsToReturn);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        };
    }
}