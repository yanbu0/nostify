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
    }

    [Fact]
    public async Task GetEventsAsync_WithPointInTime_MethodSignatureExists()
    {
        // This test validates that the pointInTime parameter is available in method signatures
        
        // Arrange
        var foreignIdSelectors = new Func<TestProjection, Guid?>[] 
        { 
            p => p.id // Use the projection's own ID as foreign ID for simplicity
        };

        // Assert that methods with pointInTime parameter exist
        var methods = typeof(ExternalDataEvent).GetMethods()
            .Where(m => m.Name == "GetEventsAsync" && m.IsStatic)
            .Where(m => m.GetParameters().Any(p => p.Name == "pointInTime" && p.ParameterType == typeof(DateTime?)))
            .ToList();

        Assert.NotEmpty(methods);
        
        // Should have methods for both Container and HttpClient
        var containerMethods = methods.Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Container))).ToList();
        var httpClientMethods = methods.Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(HttpClient))).ToList();
        
        Assert.NotEmpty(containerMethods);
        Assert.NotEmpty(httpClientMethods);
    }

    [Fact]
    public async Task GetEventsAsync_WithoutPointInTime_BackwardCompatibility()
    {
        // This test validates backward compatibility - the original method signatures still work
        
        // Arrange
        var foreignIdSelectors = new Func<TestProjection, Guid?>[] 
        { 
            p => p.id
        };

        // Assert that backward compatible methods still exist
        var compatibilityMethods = typeof(ExternalDataEvent).GetMethods()
            .Where(m => m.Name == "GetEventsAsync" && m.IsStatic)
            .Where(m => !m.GetParameters().Any(p => p.Name == "pointInTime"))
            .ToList();

        Assert.NotEmpty(compatibilityMethods);

        // Should have backward compatible methods for both Container and HttpClient
        var containerMethods = compatibilityMethods.Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Container))).ToList();
        var httpClientMethods = compatibilityMethods.Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(HttpClient))).ToList();
        
        Assert.NotEmpty(containerMethods);
        Assert.NotEmpty(httpClientMethods);
    }

    [Fact]
    public async Task GetEventsAsync_HttpClient_WithPointInTime_IncludesPointInTimeInRequest()
    {
        // Test that HTTP client version includes pointInTime in the request
        
        // Arrange
        var testPointInTime = DateTime.UtcNow.AddHours(-2);
        var foreignIdSelectors = new Func<TestProjection, Guid?>[] { p => p.id };
        
        // Create a simple HttpClient with a mock handler that returns empty array
        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);
        var url = "https://test.example.com/events";

        // Act - this should not throw and should complete successfully
        var result = await ExternalDataEvent.GetEventsAsync(httpClient, url, _testProjections, testPointInTime, foreignIdSelectors);
        
        // Assert - result should be non-null (even if empty)
        Assert.NotNull(result);
        
        // Verify the request included pointInTime
        Assert.Contains("pointInTime", mockHandler.RequestContent);
    }

    [Fact]
    public void GetEventsAsync_AllOverloads_HavePointInTimeVariants()
    {
        // Verify all method overloads have corresponding pointInTime variants
        var allMethods = typeof(ExternalDataEvent).GetMethods()
            .Where(m => m.Name == "GetEventsAsync" && m.IsStatic)
            .ToList();

        // Count methods with and without pointInTime parameter
        var withPointInTime = allMethods.Where(m => m.GetParameters().Any(p => p.Name == "pointInTime")).ToList();
        var withoutPointInTime = allMethods.Where(m => !m.GetParameters().Any(p => p.Name == "pointInTime")).ToList();

        // Should have both variants
        Assert.NotEmpty(withPointInTime);
        Assert.NotEmpty(withoutPointInTime);
        
        // Should have at least 4 methods with pointInTime (2 for Container, 2 for HttpClient with different selectors)
        Assert.True(withPointInTime.Count >= 4);
        
        // Should have backward compatibility methods
        Assert.True(withoutPointInTime.Count >= 4);
    }

    [Fact]
    public void ProjectionInitializer_AllMethods_HavePointInTimeSupport()
    {
        // Verify ProjectionInitializer interface and implementation support pointInTime
        var interfaceMethods = typeof(IProjectionInitializer).GetMethods()
            .Where(m => m.GetParameters().Any(p => p.Name == "pointInTime"))
            .ToList();
            
        var implementationMethods = typeof(ProjectionInitializer).GetMethods()
            .Where(m => m.GetParameters().Any(p => p.Name == "pointInTime"))
            .ToList();

        // Should have pointInTime methods in both interface and implementation
        Assert.NotEmpty(interfaceMethods);
        Assert.NotEmpty(implementationMethods);
        
        // Should have at least InitAsync variations with pointInTime
        Assert.Contains(interfaceMethods, m => m.Name == "InitAsync");
        Assert.Contains(implementationMethods, m => m.Name == "InitAsync");
    }
}

// Simple mock HTTP message handler for testing
public class MockHttpMessageHandler : HttpMessageHandler
{
    public string RequestContent { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        if (request.Content != null)
        {
            RequestContent = await request.Content.ReadAsStringAsync();
        }

        var responseContent = JsonConvert.SerializeObject(new List<Event>());
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        };
    }
}