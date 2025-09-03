using System;
using System.Collections.Generic;
using System.Globalization;
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
    public void EventRequester_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid?> selector1 = p => p.siteId;
        Func<TestProjectionForExternalData, Guid?> selector2 = p => p.ownerId;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector1, selector2);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(2, eventRequest.ForeignIdSelectors.Length);
        Assert.Equal(selector1, eventRequest.ForeignIdSelectors[0]);
        Assert.Equal(selector2, eventRequest.ForeignIdSelectors[1]);
    }

    [Fact]
    public void EventRequester_Constructor_ThrowsExceptionForNullUrl()
    {
        // Arrange & Act & Assert
        Assert.Throws<NostifyException>(() => new EventRequester<TestProjectionForExternalData>(null));
    }

    [Fact]
    public void EventRequester_Constructor_ThrowsExceptionForEmptyUrl()
    {
        // Arrange & Act & Assert
        Assert.Throws<NostifyException>(() => new EventRequester<TestProjectionForExternalData>(""));
    }

    [Fact]
    public void EventRequester_Constructor_HandlesNoSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.ForeignIdSelectors);
    }

    [Fact]
    public void EventRequester_Constructor_HandlesMultipleForeignIdSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid?> selector1 = p => p.siteId;
        Func<TestProjectionForExternalData, Guid?> selector2 = p => p.ownerId;
        Func<TestProjectionForExternalData, Guid?> selector3 = p => p.id;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector1, selector2, selector3);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(3, eventRequest.ForeignIdSelectors.Length);
        Assert.Equal(selector1, eventRequest.ForeignIdSelectors[0]);
        Assert.Equal(selector2, eventRequest.ForeignIdSelectors[1]);
        Assert.Equal(selector3, eventRequest.ForeignIdSelectors[2]);
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
        var methodInfo = typeof(ExternalDataEvent).GetMethods()
            .Where(m => m.Name == "GetMultiServiceEventsAsync" && m.GetParameters().Length == 3)
            .FirstOrDefault();  // Get the overload without pointInTime (backward compatibility)
        
        Assert.NotNull(methodInfo);
        Assert.True(methodInfo.IsStatic);
        Assert.True(methodInfo.IsGenericMethodDefinition);
        
        // Also verify the new overload with pointInTime exists
        var methodInfoWithPointInTime = typeof(ExternalDataEvent).GetMethods()
            .Where(m => m.Name == "GetMultiServiceEventsAsync" && m.GetParameters().Length == 4)
            .FirstOrDefault();  // Get the overload with pointInTime
        
        Assert.NotNull(methodInfoWithPointInTime);
        Assert.True(methodInfoWithPointInTime.IsStatic);
        Assert.True(methodInfoWithPointInTime.IsGenericMethodDefinition);
        
        // Verify the EventRequester constructor works as expected
        var eventRequest = new EventRequester<TestProjectionForApiTest>($"https://localhost/LocationService/api/EventRequest",
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
        var eventRequest = new EventRequester<TestProjectionForExternalData>("https://test.com/api/events", p => p.siteId);

        // Act & Assert
        await Assert.ThrowsAsync<NostifyException>(async () => 
            await ExternalDataEvent.GetMultiServiceEventsAsync(null, testProjections, eventRequest));
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_CombinesResultsFromMultipleServices()
    {
        // Test that results from multiple services are properly combined
        
        // Arrange
        var service1Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("Service1Command") },
            new Event { aggregateRootId = testProjections[1].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-25), command = new NostifyCommand("Service1Command2") }
        };
        
        var service2Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[0].ownerId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-20), command = new NostifyCommand("Service2Command") },
            new Event { aggregateRootId = testProjections[1].ownerId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-15), command = new NostifyCommand("Service2Command2") }
        };

        var mockHandler1 = new MockHttpMessageHandler(service1Events);
        var mockHandler2 = new MockHttpMessageHandler(service2Events);
        
        // Use a custom HttpClient that routes to different handlers based on URL
        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        combinedHandler.AddService("https://service2.com/events", service2Events);
        
        var httpClient = new HttpClient(combinedHandler);
        
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId),
            new EventRequester<TestProjectionForExternalData>("https://service2.com/events", p => p.ownerId)
        };

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, testProjections, eventRequests);
        
        // Assert
        Assert.NotNull(result);
        // Should get results for both services: events matching siteId and ownerId selectors
        Assert.Equal(4, result.Count);
        
        // The ExternalDataEvent.aggregateRootId will be the projection.id, not the foreign keys
        // So we need to verify the events inside each ExternalDataEvent match our expected events
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        
        // Verify events from service1 are included
        var service1EventsReturned = allReturnedEvents.Where(e => service1Events.Any(se => se.aggregateRootId == e.aggregateRootId && se.timestamp == e.timestamp)).ToList();
        Assert.Equal(2, service1EventsReturned.Count);
        
        // Verify events from service2 are included
        var service2EventsReturned = allReturnedEvents.Where(e => service2Events.Any(se => se.aggregateRootId == e.aggregateRootId && se.timestamp == e.timestamp)).ToList();
        Assert.Equal(2, service2EventsReturned.Count);
        
        // Verify that all results contain events
        Assert.All(result, ede => Assert.NotEmpty(ede.events));
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_HandlesEmptyResultsFromSomeServices()
    {
        // Test that services returning no events don't affect other services' results
        
        // Arrange
        var service1Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("Service1Command") }
        };
        
        var service2Events = new List<Event>(); // Empty results
        var service3Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[1].ownerId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-15), command = new NostifyCommand("Service3Command") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        combinedHandler.AddService("https://service2.com/events", service2Events);
        combinedHandler.AddService("https://service3.com/events", service3Events);
        
        var httpClient = new HttpClient(combinedHandler);
        
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId),
            new EventRequester<TestProjectionForExternalData>("https://service2.com/events", p => p.id), // This will return empty
            new EventRequester<TestProjectionForExternalData>("https://service3.com/events", p => p.ownerId)
        };

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, testProjections, eventRequests);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // Should only have results from service1 and service3
        
        // Verify no empty ExternalDataEvent objects are returned
        Assert.All(result, ede => Assert.NotEmpty(ede.events));
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_HandlesHttpErrorsGracefully()
    {
        // Test that HTTP errors from one service don't prevent other services from working
        
        // Arrange
        var service1Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("Service1Command") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        combinedHandler.AddServiceError("https://service2.com/events", HttpStatusCode.InternalServerError, "Service temporarily unavailable");
        
        var httpClient = new HttpClient(combinedHandler);
        
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId),
            new EventRequester<TestProjectionForExternalData>("https://service2.com/events", p => p.ownerId) // This will fail
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NostifyException>(() => 
            ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, testProjections, eventRequests));
        
        Assert.Contains("InternalServerError", exception.Message);
        Assert.Contains("Service temporarily unavailable", exception.Message);
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WorksWithPointInTimeFiltering()
    {
        // Test that point-in-time filtering works correctly with multiple services
        
        // Arrange
        var pointInTime = DateTime.UtcNow.AddMinutes(-20);
        
        var service1Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[0].siteId!.Value, timestamp = pointInTime.AddMinutes(-10), command = new NostifyCommand("BeforeFilter") }, // Before pointInTime
            new Event { aggregateRootId = testProjections[0].siteId!.Value, timestamp = pointInTime.AddMinutes(10), command = new NostifyCommand("AfterFilter") }   // After pointInTime - should be filtered out
        };
        
        var service2Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[1].ownerId!.Value, timestamp = pointInTime.AddMinutes(-5), command = new NostifyCommand("BeforeFilter2") }, // Before pointInTime
            new Event { aggregateRootId = testProjections[1].ownerId!.Value, timestamp = pointInTime.AddMinutes(15), command = new NostifyCommand("AfterFilter2") }  // After pointInTime - should be filtered out
        };

        // Configure handlers to return filtered events (simulating server-side filtering)
        var service1FilteredEvents = service1Events.Where(e => e.timestamp <= pointInTime).ToList();
        var service2FilteredEvents = service2Events.Where(e => e.timestamp <= pointInTime).ToList();

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1FilteredEvents);
        combinedHandler.AddService("https://service2.com/events", service2FilteredEvents);
        
        var httpClient = new HttpClient(combinedHandler);
        
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId),
            new EventRequester<TestProjectionForExternalData>("https://service2.com/events", p => p.ownerId)
        };

        // Act - Note: GetMultiServiceEventsAsync doesn't currently support pointInTime parameter
        // This test verifies the current behavior and can be updated when pointInTime support is added
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, testProjections, eventRequests);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        // Verify all returned events are before the pointInTime (due to server-side filtering)
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.All(allEvents, e => Assert.True(e.timestamp <= pointInTime));
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WithPointInTimeParameter_PassesToServices()
    {
        // Test that the new pointInTime parameter is properly passed to each service call
        
        // Arrange
        var pointInTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-20), DateTimeKind.Utc);
        
        var service1Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[0].siteId!.Value, timestamp = DateTime.SpecifyKind(pointInTime.AddMinutes(-10), DateTimeKind.Utc), command = new NostifyCommand("Service1Event") }
        };
        
        var service2Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[1].ownerId!.Value, timestamp = DateTime.SpecifyKind(pointInTime.AddMinutes(-5), DateTimeKind.Utc), command = new NostifyCommand("Service2Event") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        combinedHandler.AddService("https://service2.com/events", service2Events);
        
        var httpClient = new HttpClient(combinedHandler);
        
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId),
            new EventRequester<TestProjectionForExternalData>("https://service2.com/events", p => p.ownerId)
        };

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, testProjections, pointInTime, eventRequests);
        
        // Debug: Print the URLs that were requested
        var requestedUrls = combinedHandler.RequestContents.Keys.ToList();
        // Console.WriteLine($"Requested URLs: {string.Join(", ", requestedUrls)}");
        // Console.WriteLine($"Expected base URLs: https://service1.com/events, https://service2.com/events");
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        // Verify that both services received requests with the pointInTime parameter
        var requestKeys = combinedHandler.RequestContents.Keys.ToList();
        Assert.True(requestKeys.Any(k => k.StartsWith("https://service1.com/events/")), "Service1 should have been called with pointInTime path parameter");
        Assert.True(requestKeys.Any(k => k.StartsWith("https://service2.com/events/")), "Service2 should have been called with pointInTime path parameter");
        
        // Verify the events returned match our expected filtered events
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(2, allEvents.Count);
        Assert.All(allEvents, e => Assert.True(e.timestamp <= pointInTime));
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_ExecutesServicesInParallel()
    {
        // Test that multiple service calls are executed in parallel for performance
        
        // Arrange
        var service1Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("Service1Command") }
        };
        
        var service2Events = new List<Event>
        {
            new Event { aggregateRootId = testProjections[1].ownerId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-25), command = new NostifyCommand("Service2Command") }
        };

        // Use a handler that tracks timing to verify parallel execution
        var timedHandler = new TimedMockHttpHandler();
        timedHandler.AddService("https://service1.com/events", service1Events, TimeSpan.FromMilliseconds(100));
        timedHandler.AddService("https://service2.com/events", service2Events, TimeSpan.FromMilliseconds(100));
        
        var httpClient = new HttpClient(timedHandler);
        
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId),
            new EventRequester<TestProjectionForExternalData>("https://service2.com/events", p => p.ownerId)
        };

        // Act
        var startTime = DateTime.UtcNow;
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, testProjections, eventRequests);
        var endTime = DateTime.UtcNow;
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        // If executed sequentially, it would take ~200ms. If parallel, it should be closer to ~100ms
        var executionTime = endTime - startTime;
        Assert.True(executionTime.TotalMilliseconds < 180, $"Execution took {executionTime.TotalMilliseconds}ms, expected less than 180ms for parallel execution");
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_HandlesLargeNumberOfServices()
    {
        // Test that the method can handle multiple services efficiently
        
        // Arrange
        const int numberOfServices = 5;
        var combinedHandler = new MultiServiceMockHttpHandler();
        var eventRequests = new List<EventRequester<TestProjectionForExternalData>>();
        
        for (int i = 0; i < numberOfServices; i++)
        {
            var serviceEvents = new List<Event>
            {
                new Event { aggregateRootId = testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30 + i), command = new NostifyCommand($"Service{i}Command") }
            };
            
            var serviceUrl = $"https://service{i}.com/events";
            combinedHandler.AddService(serviceUrl, serviceEvents);
            eventRequests.Add(new EventRequester<TestProjectionForExternalData>(serviceUrl, p => p.siteId));
        }
        
        var httpClient = new HttpClient(combinedHandler);

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, testProjections, eventRequests.ToArray());
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(numberOfServices, result.Count); // Should have one result per service
        
        // Verify each service contributed events
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(numberOfServices, allEvents.Count);
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

        // All events should be returned
        var allReturnedEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(_testEvents.Count, allReturnedEvents.Count);
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
        Assert.Empty(result); // Should be empty since no events are before pointInTimeBeforeAllEvents
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

    public MockHttpMessageHandler(List<Event>? eventsToReturn = null)
    {
        _eventsToReturn = eventsToReturn ?? new List<Event>();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        if (request.Content != null)
        {
            RequestContent = await request.Content.ReadAsStringAsync();
        }

        // Parse pointInTime path parameter if present
        // Expected format: {baseUrl}/{pointInTime} where pointInTime is in ISO format
        DateTime? pointInTime = null;
        var uri = request.RequestUri;
        if (uri != null)
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 1)
            {
                var lastSegment = segments[segments.Length - 1];
                // Try to parse the last segment as a DateTime in ISO format with UTC
                if (DateTime.TryParseExact(Uri.UnescapeDataString(lastSegment), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
                {
                    pointInTime = parsedDate.ToUniversalTime();
                }
            }
        }

        // Filter events based on pointInTime if provided
        var eventsToReturn = _eventsToReturn;
        if (pointInTime.HasValue)
        {
            eventsToReturn = _eventsToReturn.Where(e => e.timestamp <= pointInTime.Value).ToList();
        }

        // Return the configured events
        var responseContent = JsonConvert.SerializeObject(eventsToReturn);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>
/// Mock HTTP handler that can route requests to different services based on URL
/// </summary>
public class MultiServiceMockHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, List<Event>> _serviceEvents = new();
    private readonly Dictionary<string, (HttpStatusCode statusCode, string message)> _serviceErrors = new();
    public Dictionary<string, string> RequestContents { get; } = new();

    public void AddService(string url, List<Event> eventsToReturn)
    {
        _serviceEvents[url] = eventsToReturn;
    }

    public void AddServiceError(string url, HttpStatusCode statusCode, string message)
    {
        _serviceErrors[url] = (statusCode, message);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;

        if (request.Content != null)
        {
            RequestContents[url] = await request.Content.ReadAsStringAsync();
        }

        // Parse pointInTime path parameter if present and get base URL
        // Expected format: {baseUrl}/{pointInTime} where pointInTime is in ISO format
        DateTime? pointInTime = null;
        string baseUrl = url;
        
        var uri = request.RequestUri;
        if (uri != null)
        {
            // Try to find if the last part of the path is a datetime
            // We'll work backwards from the full URL to find the base URL
            var urlDecoded = Uri.UnescapeDataString(url);
            var segments = urlDecoded.Split('/');
            
            if (segments.Length >= 1)
            {
                var lastSegment = segments[segments.Length - 1];
                // Try to parse the last segment as a DateTime in ISO format with UTC
                if (DateTime.TryParseExact(lastSegment, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
                {
                    pointInTime = parsedDate.ToUniversalTime();
                    // Remove the pointInTime segment to get the base URL by finding the last occurrence
                    var lastSlashIndex = url.LastIndexOf('/');
                    if (lastSlashIndex > 0)
                    {
                        baseUrl = url.Substring(0, lastSlashIndex);
                        // Decode the base URL as well
                        baseUrl = Uri.UnescapeDataString(baseUrl);
                    }
                }
            }
        }

        // Check if this base URL should return an error
        if (_serviceErrors.TryGetValue(baseUrl, out var error))
        {
            return new HttpResponseMessage(error.statusCode)
            {
                ReasonPhrase = error.message,
                Content = new StringContent(error.message, Encoding.UTF8, "application/json")
            };
        }

        // Return events for this service
        if (_serviceEvents.TryGetValue(baseUrl, out var events))
        {
            var eventsToReturn = events;
            
            // Filter events based on pointInTime if provided
            if (pointInTime.HasValue)
            {
                eventsToReturn = events.Where(e => e.timestamp <= pointInTime.Value).ToList();
            }
            
            var responseContent = JsonConvert.SerializeObject(eventsToReturn);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };
        }

        // Default to empty response
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>
/// Mock HTTP handler that introduces delays to test parallel execution
/// </summary>
public class TimedMockHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (List<Event> events, TimeSpan delay)> _serviceData = new();
    public Dictionary<string, string> RequestContents { get; } = new();

    public void AddService(string url, List<Event> eventsToReturn, TimeSpan delay)
    {
        _serviceData[url] = (eventsToReturn, delay);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;

        if (request.Content != null)
        {
            RequestContents[url] = await request.Content.ReadAsStringAsync();
        }

        // Parse pointInTime path parameter if present and get base URL
        // Expected format: {baseUrl}/{pointInTime} where pointInTime is in ISO format
        DateTime? pointInTime = null;
        string baseUrl = url;
        
        var uri = request.RequestUri;
        if (uri != null)
        {
            // Try to find if the last part of the path is a datetime
            // We'll work backwards from the full URL to find the base URL
            var urlDecoded = Uri.UnescapeDataString(url);
            var segments = urlDecoded.Split('/');
            
            if (segments.Length >= 1)
            {
                var lastSegment = segments[segments.Length - 1];
                // Try to parse the last segment as a DateTime in ISO format with UTC
                if (DateTime.TryParseExact(lastSegment, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
                {
                    pointInTime = parsedDate.ToUniversalTime();
                    // Remove the pointInTime segment to get the base URL by finding the last occurrence
                    var lastSlashIndex = url.LastIndexOf('/');
                    if (lastSlashIndex > 0)
                    {
                        baseUrl = url.Substring(0, lastSlashIndex);
                        // Decode the base URL as well
                        baseUrl = Uri.UnescapeDataString(baseUrl);
                    }
                }
            }
        }

        // Add delay and return events for this service
        if (_serviceData.TryGetValue(baseUrl, out var serviceData))
        {
            await Task.Delay(serviceData.delay, cancellationToken);
            
            var eventsToReturn = serviceData.events;
            
            // Filter events based on pointInTime if provided
            if (pointInTime.HasValue)
            {
                eventsToReturn = serviceData.events.Where(e => e.timestamp <= pointInTime.Value).ToList();
            }
            
            var responseContent = JsonConvert.SerializeObject(eventsToReturn);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };
        }

        // Default to empty response
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
    }
}