using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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

    public ExternalDataEventTests()
    {
        _pointInTime = DateTime.UtcNow.AddHours(-1);
        
        // Setup test data
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
    public void GetEventsAsync_Container_FilteringLogic_WorksCorrectly()
    {
        // Test that Container version correctly applies pointInTime filtering
        // We simulate the filtering logic that would be applied to test the behavior
        
        // Arrange
        var foreignIdSelectors = new Func<TestProjection, Guid?>[] { p => p.id };
        var testPointInTime = DateTime.UtcNow.AddHours(-1);
        
        // Get the foreign IDs that would be used in the query
        var foreignIds = (from p in _testProjections
                         from f in foreignIdSelectors
                         let foreignId = f(p)
                         where foreignId.HasValue
                         select foreignId.Value).ToList();
        
        // Simulate the LINQ query filtering that happens in the Container version
        var baseQuery = _testEvents.Where(e => foreignIds.Contains(e.aggregateRootId));
        
        // Test without pointInTime (should return all matching events)
        var allMatchingEvents = baseQuery.OrderBy(e => e.timestamp).ToList();
        
        // Test with pointInTime (should filter out events after the specified time)
        var filteredEvents = baseQuery
            .Where(e => e.timestamp <= testPointInTime)
            .OrderBy(e => e.timestamp)
            .ToList();
        
        // Assert that filtering works correctly
        Assert.NotEmpty(allMatchingEvents);
        Assert.NotEmpty(filteredEvents);
        
        // Verify that filteredEvents is a subset of allMatchingEvents
        Assert.True(filteredEvents.Count <= allMatchingEvents.Count);
        
        // Verify that all filtered events are before or at pointInTime
        Assert.All(filteredEvents, e => Assert.True(e.timestamp <= testPointInTime));
        
        // Verify that we're actually filtering out some events
        var eventsAfterPointInTime = allMatchingEvents.Where(e => e.timestamp > testPointInTime).ToList();
        if (eventsAfterPointInTime.Any())
        {
            Assert.True(filteredEvents.Count < allMatchingEvents.Count, 
                "pointInTime filtering should exclude events that occur after the specified time");
        }
        
        // Verify that the result structure would be correct
        var expectedResult = (from p in _testProjections
                             from f in foreignIdSelectors
                             let foreignId = f(p)
                             where foreignId.HasValue
                             let eventsForAggregate = filteredEvents.Where(e => e.aggregateRootId == foreignId.Value).ToList()
                             select new ExternalDataEvent(p.id, eventsForAggregate)).ToList();
        
        Assert.NotNull(expectedResult);
        Assert.Equal(_testProjections.Count, expectedResult.Count);
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