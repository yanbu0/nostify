using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json.Linq;
using nostify;
using Xunit;
using MockQueryable.Moq;
using Microsoft.Azure.Cosmos.Linq;

public class IProjectionTests
{
    public class TestAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => "TestAggregate";

        public static string currentStateContainerName => "TestAggregateCurrentState";

        public Guid id { get; set; }
        public bool isDeleted { get; set; } = false;
        public string name { get; set; } = string.Empty;

        public override void Apply(Event eventToApply)
        {
            // Mock implementation for testing
            // For example, set some properties based on the event payload
            if (eventToApply.PayloadHasProperty("name"))
            {
                // Assuming the projection has a property called "name"
                this.GetType().GetProperty("name")?.SetValue(this, eventToApply.GetPayload<dynamic>().name);
            }
        }
    }

    public class TestProjection : NostifyObject, IProjection<TestProjection>, IContainerName, IHasExternalData<TestProjection>
    {
        public bool initialized { get; set; } = false;

        public static string containerName => "TestContainer";
        public IProjection<TestProjection> projection => this;
        public string name { get; set; } = string.Empty;

        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<TestProjection> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? timestamp = null)
        {
            return Task.FromResult(new List<ExternalDataEvent>());
        }

        public override void Apply(Event eventToApply)
        {
            // Mock implementation for testing
            // For example, set some properties based on the event payload
            if (eventToApply.PayloadHasProperty("name"))
            {
                // Assuming the projection has a property called "name"
                this.GetType().GetProperty("name")?.SetValue(this, eventToApply.GetPayload<dynamic>().Name);
            }
        }
    }

    private readonly Mock<INostify> _mockNostify;
    private readonly HttpClient _mockHttpClient;
    private readonly Mock<Container> _mockProjectionContainer;
    private readonly Mock<Container> _mockAggregateContainer;

    public IProjectionTests()
    {
        _mockNostify = new Mock<INostify>();

        // Mock the Nostify method to return a mocked container
        _mockProjectionContainer = new Mock<Container>();
        _mockAggregateContainer = new Mock<Container>();

        // Mock the Database property
        var mockDatabase = new Mock<Database>();

        // Mock the Client property
        var mockClient = new Mock<CosmosClient>();
        mockClient.Setup(c => c.ClientOptions).Returns(new CosmosClientOptions { AllowBulkExecution = true });

        // Set up the Database to return the mocked Client
        mockDatabase.Setup(d => d.Client).Returns(mockClient.Object);

        // Set up the Container to return the mocked Database
        _mockProjectionContainer.Setup(c => c.Database).Returns(mockDatabase.Object);
        _mockAggregateContainer.Setup(c => c.Database).Returns(mockDatabase.Object);

        _mockNostify.Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(_mockProjectionContainer.Object);

        _mockNostify.Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(_mockAggregateContainer.Object);

        _mockHttpClient = new HttpClient();
    }

    [Fact]
    public async Task InitAsync_ShouldInitializeProjection()
    {
        // Arrange
        var projection = new TestProjection();        

        // Act
        var result = await projection.InitAsync(_mockNostify.Object, _mockHttpClient);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.initialized);
    }

    // //Skip this since it hits an extension method
    // [Fact]
    // public async Task InitAsync_WithId_ShouldInitializeProjections()
    // {
    //     // Arrange
    //     var testId = Guid.NewGuid();
    //     var projection = new TestProjection();

    //     // Create a list of TestAggregate with the specified testId
    //     var testAggregates = new List<TestAggregate>
    //     {
    //         new TestAggregate { id = testId, isDeleted = false }
    //     };

    //     // Mock the FeedResponse to return the testAggregates
    //     var mockFeedResponse = new Mock<FeedResponse<TestAggregate>>();
    //     mockFeedResponse.Setup(r => r.GetEnumerator()).Returns(testAggregates.GetEnumerator());
    //     mockFeedResponse.Setup(r => r.Count).Returns(testAggregates.Count);

    //     // Mock the FeedIterator to return the mocked FeedResponse
    //     var mockFeedIterator = new Mock<FeedIterator<TestAggregate>>();
    //     mockFeedIterator.SetupSequence(f => f.HasMoreResults)
    //         .Returns(true) // First call returns true
    //         .Returns(false); // Second call returns false
    //     mockFeedIterator.Setup(f => f.ReadNextAsync(It.IsAny<CancellationToken>()))
    //         .ReturnsAsync(mockFeedResponse.Object);

    //     var mockQueryable = testAggregates.AsQueryable().OrderBy(a => a.id).BuildMock();
    //     mockQueryable.Setup(q => q.ToFeedIterator<TestAggregate>())
    //         .Returns(mockFeedIterator.Object);

    //     // Mock the container to return the mocked FeedIterator
    //     _mockAggregateContainer.Setup(c => c.GetItemLinqQueryable<TestAggregate>(false, null, null, null))
    //         .Returns(testAggregates.AsQueryable().OrderBy(a => a.id));

    //     _mockNostify.Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
    //         .ReturnsAsync(_mockAggregateContainer.Object);

    //     // Act
    //     var result = await projection.projection.InitAsync<TestAggregate>(testId, _mockNostify.Object, _mockHttpClient);

    //     // Assert
    //     Assert.NotNull(result);
    //     Assert.Single(result); // Ensure only one projection is initialized
    //     Assert.Equal(testId, result.First().id); // Ensure the initialized projection has the correct id
    //     Assert.All(result, p => Assert.True(p.initialized)); // Ensure all projections are initialized
    // }

    [Fact]
    public async Task InitAsync_WithIds_ShouldInitializeProjections()
    {
        // Arrange
        var testIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var projection = new TestProjection();

        var mockContainer = new Mock<Container>();
        mockContainer.Setup(c => c.GetItemLinqQueryable<TestAggregate>(true, null, null, null))
            .Returns(new List<TestAggregate>
            {
                new TestAggregate { id = Guid.NewGuid(), isDeleted = false }
            }.AsQueryable().OrderBy(a => 0));

        _mockNostify.Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        // Act
        var result = await projection.projection.InitAsync<TestAggregate>(testIds, _mockNostify.Object, _mockHttpClient);

        // Assert
        Assert.NotNull(result);
        Assert.All(result, p => Assert.True(p.initialized));
    }

    [Fact]
    public async Task InitContainerAsync_ShouldRecreateContainerAndInitializeProjections()
    {
        // Arrange        
        var projection = new TestProjection();
        _mockNostify.Setup(n => n.GetContainerAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
            .ReturnsAsync(new Mock<Container>().Object);

        var mockContainer = new Mock<Container>();
        mockContainer.Setup(c => c.GetItemLinqQueryable<TestAggregate>(true, null, null, null))
            .Returns(new List<TestAggregate>
            {
                new TestAggregate { id = Guid.NewGuid(), isDeleted = false }
            }.AsQueryable().OrderBy(a => 0));

        _mockNostify.Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        // Act
        await projection.projection.InitContainerAsync<TestAggregate>(_mockNostify.Object, _mockHttpClient);

        // Assert
        _mockNostify.Verify(n => n.GetContainerAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.AtLeastOnce);
        _mockNostify.Verify(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitAllUninitialized_ShouldInitializeAllUninitializedProjections()
    {
        // Arrange
        var projection = new TestProjection();
        
        var mockProjectionContainer = new Mock<Container>();
        mockProjectionContainer.Setup(c => c.GetItemLinqQueryable<TestProjection>(true, null, null, null))
            .Returns(new List<TestProjection>
            {
                new TestProjection { initialized = false }
            }.AsQueryable().OrderBy(p => 0));

        _mockNostify.Setup(n => n.GetProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockProjectionContainer.Object);

        // Act
        await projection.projection.InitAllUninitialized(_mockNostify.Object, _mockHttpClient);

        // Assert
        _mockNostify.Verify(n => n.GetProjectionContainerAsync<TestProjection>("/tenantId"), Times.Once);
    }
}