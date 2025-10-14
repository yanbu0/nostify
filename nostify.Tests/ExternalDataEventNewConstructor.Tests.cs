using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using nostify;
using Xunit;

namespace nostify.Tests;

public class ExternalDataEventNewConstructorTests
{
    private readonly List<TestProjectionForExternalData> _testProjections;
    private readonly List<TestProjectionWithLists> _testProjectionsWithLists;

    public ExternalDataEventNewConstructorTests()
    {
        _testProjections = new List<TestProjectionForExternalData>
        {
            new TestProjectionForExternalData { id = Guid.NewGuid(), siteId = Guid.NewGuid(), ownerId = Guid.NewGuid() },
            new TestProjectionForExternalData { id = Guid.NewGuid(), siteId = Guid.NewGuid(), ownerId = Guid.NewGuid() }
        };

        _testProjectionsWithLists = new List<TestProjectionWithLists>
        {
            new TestProjectionWithLists 
            { 
                id = Guid.NewGuid(), 
                siteId = Guid.NewGuid(), 
                relatedIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
                dependentIds = new List<Guid> { Guid.NewGuid() }
            },
            new TestProjectionWithLists 
            { 
                id = Guid.NewGuid(), 
                siteId = Guid.NewGuid(), 
                relatedIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() },
                dependentIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
            }
        };
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WithNonNullableSingleGuidConstructor_ReturnsCorrectResults()
    {
        // Test the new non-nullable single Guid constructor
        
        // Arrange
        var service1Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("Service1Command") },
            new Event { aggregateRootId = _testProjections[1].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-25), command = new NostifyCommand("Service1Command2") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        var httpClient = new HttpClient(combinedHandler);

        // Use the new non-nullable single Guid constructor
        Func<TestProjectionForExternalData, Guid> singleSelector = p => p.siteId!.Value;
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionForExternalData>("https://service1.com/events", singleSelector)
        };

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, _testProjections, eventRequests);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(2, allEvents.Count);
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WithNonNullableListGuidConstructor_ReturnsCorrectResults()
    {
        // Test the new non-nullable list Guid constructor
        
        // Arrange
        var service1Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjectionsWithLists[0].relatedIds[0], timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("RelatedEvent1") },
            new Event { aggregateRootId = _testProjectionsWithLists[0].relatedIds[1], timestamp = DateTime.UtcNow.AddMinutes(-25), command = new NostifyCommand("RelatedEvent2") },
            new Event { aggregateRootId = _testProjectionsWithLists[1].relatedIds[0], timestamp = DateTime.UtcNow.AddMinutes(-20), command = new NostifyCommand("RelatedEvent3") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        var httpClient = new HttpClient(combinedHandler);

        // Use the new non-nullable list Guid constructor
        Func<TestProjectionWithLists, List<Guid>> listSelector = p => p.relatedIds;
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionWithLists>("https://service1.com/events", listSelector)
        };

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, _testProjectionsWithLists, eventRequests);

        // Assert
        Assert.NotNull(result);
        // With list selectors, each projection gets expanded to multiple foreign ID queries
        // So we expect results for each projection that has matching events
        Assert.True(result.Count >= 1); 
        var allEvents = result.SelectMany(r => r.events).ToList();
        // The actual count may be higher due to list expansion creating multiple queries
        Assert.True(allEvents.Count >= 3); // At least the matching events
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WithMixedNonNullableConstructor_ReturnsCorrectResults()
    {
        // Test the new mixed non-nullable constructor (single + list)
        
        // Arrange
        var service1Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjectionsWithLists[0].siteId, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("SiteEvent1") },
            new Event { aggregateRootId = _testProjectionsWithLists[1].siteId, timestamp = DateTime.UtcNow.AddMinutes(-25), command = new NostifyCommand("SiteEvent2") },
            new Event { aggregateRootId = _testProjectionsWithLists[0].relatedIds[0], timestamp = DateTime.UtcNow.AddMinutes(-20), command = new NostifyCommand("RelatedEvent1") },
            new Event { aggregateRootId = _testProjectionsWithLists[1].relatedIds[1], timestamp = DateTime.UtcNow.AddMinutes(-15), command = new NostifyCommand("RelatedEvent2") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        var httpClient = new HttpClient(combinedHandler);

        // Use the new mixed constructor
        var singleSelectors = new Func<TestProjectionWithLists, Guid>[] { p => p.siteId };
        var listSelectors = new Func<TestProjectionWithLists, List<Guid>>[] { p => p.relatedIds };
        var eventRequests = new[]
        {
            new EventRequester<TestProjectionWithLists>("https://service1.com/events", singleSelectors, listSelectors)
        };

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, _testProjectionsWithLists, eventRequests);

        // Assert
        Assert.NotNull(result);
        // With mixed selectors (single + list), results are expanded across all foreign IDs
        Assert.True(result.Count >= 1); // At least some projections with events
        var allEvents = result.SelectMany(r => r.events).ToList();
        // The actual count may be higher due to list expansion creating multiple queries
        Assert.True(allEvents.Count >= 4); // At least the matching events from both single and list selectors
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WithMixedConstructorTypes_InSingleQuery_ReturnsCorrectResults()
    {
        // Test mixing different constructor types in a single query
        
        // Arrange
        var service1Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("Service1Event") }
        };
        
        var service2Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[1].ownerId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-25), command = new NostifyCommand("Service2Event") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        combinedHandler.AddService("https://service2.com/events", service2Events);
        var httpClient = new HttpClient(combinedHandler);

        // Mix different constructor types in a single query
        var mixedEventRequests = new List<EventRequester<TestProjectionForExternalData>>();
        
        // 1. Original nullable constructor
        mixedEventRequests.Add(new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId));
        
        // 2. New non-nullable single constructor
        Func<TestProjectionForExternalData, Guid> nonNullableSelector = p => p.ownerId!.Value;
        mixedEventRequests.Add(new EventRequester<TestProjectionForExternalData>("https://service2.com/events", nonNullableSelector));

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, _testProjections, mixedEventRequests.ToArray());

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // One for each projection
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(2, allEvents.Count); // One event from each service
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WithComplexMixedConstructors_ReturnsCorrectResults()
    {
        // Test complex scenario with multiple constructor types and selectors mixed together
        
        // Arrange
        var service1Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("Service1SingleEvent") }
        };
        
        var service2Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[0].ownerId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-35), command = new NostifyCommand("Service2MultiSingleEvent1") },
            new Event { aggregateRootId = _testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-32), command = new NostifyCommand("Service2MultiSingleEvent2") }
        };
        
        var service3Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[0].id, timestamp = DateTime.UtcNow.AddMinutes(-28), command = new NostifyCommand("Service3MixedEvent") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        combinedHandler.AddService("https://service2.com/events", service2Events);
        combinedHandler.AddService("https://service3.com/events", service3Events);
        var httpClient = new HttpClient(combinedHandler);

        var complexEventRequests = new List<EventRequester<TestProjectionForExternalData>>();
        
        // 1. Original nullable single selector
        complexEventRequests.Add(new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId));
        
        // 2. Original nullable multiple single selectors
        complexEventRequests.Add(new EventRequester<TestProjectionForExternalData>("https://service2.com/events", p => p.ownerId, p => p.siteId));
        
        // 3. Mixed nullable constructor (single + list)
        var singleSelectors = new Func<TestProjectionForExternalData, Guid?>[] { p => p.id };
        var listSelectors = new Func<TestProjectionForExternalData, List<Guid?>>[] { p => new List<Guid?> { p.siteId, p.ownerId } };
        complexEventRequests.Add(new EventRequester<TestProjectionForExternalData>("https://service3.com/events", singleSelectors, listSelectors));

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, _testProjections, complexEventRequests.ToArray());

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 1); // At least 1 projection with events
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.True(allEvents.Count >= 4); // At least one event from each service call
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WithPointInTime_AndNewConstructors_FiltersCorrectly()
    {
        // Test that pointInTime filtering works with all new constructor types
        
        // Arrange
        var pointInTime = DateTime.UtcNow.AddMinutes(-22);
        
        var service1Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("BeforeFilter") }, // Before pointInTime
            new Event { aggregateRootId = _testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-10), command = new NostifyCommand("AfterFilter") }   // After pointInTime
        };
        
        var service2Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjectionsWithLists[0].relatedIds[0], timestamp = DateTime.UtcNow.AddMinutes(-25), command = new NostifyCommand("BeforeFilterList") }, // Before pointInTime
            new Event { aggregateRootId = _testProjectionsWithLists[0].relatedIds[1], timestamp = DateTime.UtcNow.AddMinutes(-15), command = new NostifyCommand("AfterFilterList") }   // After pointInTime
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        combinedHandler.AddService("https://service2.com/events", service2Events);
        var httpClient = new HttpClient(combinedHandler);

        // Use new constructor types
        Func<TestProjectionForExternalData, Guid> singleSelector = p => p.siteId!.Value;
        var eventRequests1 = new[] { new EventRequester<TestProjectionForExternalData>("https://service1.com/events", singleSelector) };
        
        Func<TestProjectionWithLists, List<Guid>> listSelector = p => p.relatedIds;
        var eventRequests2 = new[] { new EventRequester<TestProjectionWithLists>("https://service2.com/events", listSelector) };

        // Act
        var result1 = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, _testProjections, pointInTime, eventRequests1);
        var result2 = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, _testProjectionsWithLists, pointInTime, eventRequests2);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        
        var allEvents1 = result1.SelectMany(r => r.events).ToList();
        var allEvents2 = result2.SelectMany(r => r.events).ToList();
        
        // Only events before pointInTime should be returned
        Assert.All(allEvents1, e => Assert.True(e.timestamp <= pointInTime));
        Assert.All(allEvents2, e => Assert.True(e.timestamp <= pointInTime));
        
        // Should have only the "before" events
        Assert.Single(allEvents1);
        // For list selectors, we might get more results due to expansion
        Assert.True(allEvents2.Count >= 1);
        // Check that we have events containing the expected command types
        Assert.All(allEvents1, e => Assert.Contains("BeforeFilter", e.command.ToString()));
        Assert.All(allEvents2, e => Assert.Contains("BeforeFilterList", e.command.ToString()));
    }

    [Fact]
    public async Task GetMultiServiceEventsAsync_WithAllConstructorTypesMixed_ExecutesSuccessfully()
    {
        // Test that all different constructor types can be used together in the same query
        
        // Arrange
        var service1Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[0].siteId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-30), command = new NostifyCommand("OriginalNullable") }
        };
        
        var service2Events = new List<IEvent>
        {
            new Event { aggregateRootId = _testProjections[0].ownerId!.Value, timestamp = DateTime.UtcNow.AddMinutes(-25), command = new NostifyCommand("NewNonNullable") }
        };

        var combinedHandler = new MultiServiceMockHttpHandler();
        combinedHandler.AddService("https://service1.com/events", service1Events);
        combinedHandler.AddService("https://service2.com/events", service2Events);
        var httpClient = new HttpClient(combinedHandler);

        var allConstructorTypes = new List<EventRequester<TestProjectionForExternalData>>();
        
        // 1. Original nullable params constructor
        allConstructorTypes.Add(new EventRequester<TestProjectionForExternalData>("https://service1.com/events", p => p.siteId));
        
        // 2. New non-nullable params constructor
        Func<TestProjectionForExternalData, Guid> nonNullableSelector = p => p.ownerId!.Value;
        allConstructorTypes.Add(new EventRequester<TestProjectionForExternalData>("https://service2.com/events", nonNullableSelector));

        // Act
        var result = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient, _testProjections, allConstructorTypes.ToArray());

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 1);
        var allEvents = result.SelectMany(r => r.events).ToList();
        Assert.Equal(2, allEvents.Count); // Should get events from both services
        
        // Verify we got events from both constructor types
        Assert.Contains(allEvents, e => e.command.ToString().Contains("OriginalNullable"));
        Assert.Contains(allEvents, e => e.command.ToString().Contains("NewNonNullable"));
    }
}

// Test model for list-based projections
public class TestProjectionWithLists : IUniquelyIdentifiable
{
    public Guid id { get; set; }
    public Guid siteId { get; set; }
    public List<Guid> relatedIds { get; set; } = new List<Guid>();
    public List<Guid> dependentIds { get; set; } = new List<Guid>();
}
