using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using nostify;

namespace nostify.Tests;

public class ExternalDataEventTests
{
    private List<TestProjectionForExternalData> testProjections;

    public ExternalDataEventTests()
    {
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