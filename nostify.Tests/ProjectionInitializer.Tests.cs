using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using MockQueryable.Moq;
using Moq;
using nostify;
using Xunit;

namespace nostify.Tests;


public class ProjectionInitializerTests
{
    private readonly Mock<INostify> _nostifyMock;
    private readonly Mock<Container> _containerMock;
    private readonly Mock<HttpClient> _httpClientMock;
    private readonly ProjectionInitializer _projectionInitializer;

    public ProjectionInitializerTests()
    {
        _nostifyMock = new Mock<INostify>();
        _containerMock = new Mock<Container>();
        _httpClientMock = new Mock<HttpClient>();
        _projectionInitializer = new ProjectionInitializer();
    }

    [Fact]
    public void ProjectionInitializer_ImplementsIProjectionInitializer()
    {
        Assert.IsAssignableFrom<IProjectionInitializer>(_projectionInitializer);
    }

    [Fact]
    public void IProjectionInitializer_HasPartitionKeyOverloads()
    {
        // Verify PartitionKey overloads exist on the interface at compile time
        IProjectionInitializer initializer = _projectionInitializer;

        // These assignments will fail at compile-time if the signatures are missing
        Func<Guid, PartitionKey, INostify, HttpClient?, DateTime?, Task<List<TestProjection>>> initById =
            initializer.InitAsync<TestProjection, TestAggregate>;
        Func<List<Guid>, PartitionKey, INostify, HttpClient?, DateTime?, Task<List<TestProjection>>> initByIds =
            initializer.InitAsync<TestProjection, TestAggregate>;
        Func<INostify, PartitionKey, HttpClient?, string, int, DateTime?, Task> initContainer =
            initializer.InitContainerAsync<TestProjection, TestAggregate>;
        Func<INostify, PartitionKey, HttpClient?, int, DateTime?, Task> initAllUninitialized =
            initializer.InitAllUninitialized<TestProjection>;

        Assert.NotNull(initById);
        Assert.NotNull(initByIds);
        Assert.NotNull(initContainer);
        Assert.NotNull(initAllUninitialized);
    }

    [Fact]
    public void IProjectionInitializer_HasTenantIdOverloads()
    {
        // Verify Guid tenantId overloads exist on the interface at compile time
        IProjectionInitializer initializer = _projectionInitializer;

        Func<Guid, Guid, INostify, HttpClient?, DateTime?, Task<List<TestProjection>>> initById =
            initializer.InitAsync<TestProjection, TestAggregate>;
        Func<List<Guid>, Guid, INostify, HttpClient?, DateTime?, Task<List<TestProjection>>> initByIds =
            initializer.InitAsync<TestProjection, TestAggregate>;
        Func<INostify, Guid, HttpClient?, string, int, DateTime?, Task> initContainer =
            initializer.InitContainerAsync<TestProjection, TestAggregate>;
        Func<INostify, Guid, HttpClient?, int, DateTime?, Task> initAllUninitialized =
            initializer.InitAllUninitialized<TestProjection>;

        Assert.NotNull(initById);
        Assert.NotNull(initByIds);
        Assert.NotNull(initContainer);
        Assert.NotNull(initAllUninitialized);
    }

    [Fact]
    public async Task InitAsync_WithTenantId_DelegatesToPartitionKeyOverload()
    {
        // Arrange: verify that the tenantId overload reaches GetCurrentStateContainerAsync
        // (same entry point as the PartitionKey overload would use)
        var tenantId = Guid.NewGuid();
        var testId = Guid.NewGuid();

        _nostifyMock
            .Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(_containerMock.Object);

        // The call will fail when it reaches GetItemLinqQueryable (not mockable without a real Cosmos connection),
        // but we verify GetCurrentStateContainerAsync was called, proving delegation occurred.
        await Assert.ThrowsAnyAsync<Exception>(
            () => _projectionInitializer.InitAsync<TestProjection, TestAggregate>(testId, tenantId, _nostifyMock.Object));

        _nostifyMock.Verify(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task InitAsyncList_WithTenantId_DelegatesToPartitionKeyOverload()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        _nostifyMock
            .Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(_containerMock.Object);

        await Assert.ThrowsAnyAsync<Exception>(
            () => _projectionInitializer.InitAsync<TestProjection, TestAggregate>(testIds, tenantId, _nostifyMock.Object));

        _nostifyMock.Verify(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task InitContainerAsync_WithTenantId_DelegatesToPartitionKeyOverload()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        _nostifyMock
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(_containerMock.Object);

        await Assert.ThrowsAnyAsync<Exception>(
            () => _projectionInitializer.InitContainerAsync<TestProjection, TestAggregate>(_nostifyMock.Object, tenantId));

        _nostifyMock.Verify(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task InitAllUninitialized_WithTenantId_DelegatesToPartitionKeyOverload()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        _nostifyMock
            .Setup(n => n.GetProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(_containerMock.Object);

        await Assert.ThrowsAnyAsync<Exception>(
            () => _projectionInitializer.InitAllUninitialized<TestProjection>(_nostifyMock.Object, tenantId));

        _nostifyMock.Verify(n => n.GetProjectionContainerAsync<TestProjection>(It.IsAny<string>()), Times.Once);
    }

    //WHY DID MICROSOFT MAKE IT SO HARD TO TEST THIS?! ToFeedIterator can't be mocked without wrapping it, ugh
    //TODO: Figure out how to work around this limitation or refactor the code to make it more testable
    //https://stackoverflow.com/questions/58212697/mocking-getitemlinqqueryable-and-extension-method-tofeediterator
    // [Fact]
    // public async Task InitAsync_WithSingleId_ShouldInitializeProjection()
    // {
    //     // Arrange
    //     var testId = Guid.NewGuid();
    //     var testAggregate = new TestAggregate { id = testId, isDeleted = false };
    //     var testProjection = new TestProjection { id = testId, initialized = false };

    //     _nostifyMock.Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>("/tenantId"))
    //         .ReturnsAsync(_containerMock.Object);

    //     _containerMock.Setup(c => c.GetItemLinqQueryable<TestAggregate>(false, null, null, null))
    //         .Returns(new List<TestAggregate> { testAggregate }.AsQueryable().OrderBy(a => a.id));

    //     _nostifyMock.Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>("/tenantId"))
    //         .ReturnsAsync(_containerMock.Object);

    //     // Act
    //     var result = await _projectionInitializer.InitAsync<TestProjection, TestAggregate>(testId, _nostifyMock.Object, _httpClientMock.Object);

    //     // Assert
    //     Assert.NotNull(result);
    //     Assert.Single(result);
    //     Assert.Equal(testId, result.First().id);
    // }

    // [Fact]
    // public async Task InitAsync_WithMultipleIds_ShouldInitializeProjections()
    // {
    //     // Arrange
    //     var testIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
    //     var testAggregates = testIds.Select(id => new TestAggregate { id = id, isDeleted = false }).ToList();

    //     _nostifyMock.Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>())
    //         .ReturnsAsync(_containerMock.Object);

    //     _containerMock.Setup(c => c.GetItemLinqQueryable<TestAggregate>(true, null, null))
    //         .Returns(testAggregates.AsQueryable().BuildMock().Object);

    //     _nostifyMock.Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>())
    //         .ReturnsAsync(_containerMock.Object);

    //     // Act
    //     var result = await _projectionInitializer.InitAsync<TestProjection, TestAggregate>(testIds, _nostifyMock.Object, _httpClientMock.Object);

    //     // Assert
    //     Assert.NotNull(result);
    //     Assert.Equal(testIds.Count, result.Count);
    //     Assert.All(result, p => Assert.Contains(p.id, testIds));
    // }

    // [Fact]
    // public async Task InitContainerAsync_ShouldRecreateContainerAndInitializeProjections()
    // {
    //     // Arrange
    //     var testIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
    //     var testAggregates = testIds.Select(id => new TestAggregate { id = id, isDeleted = false }).ToList();
    //     var testEvents = testIds.Select(id => new Event { aggregateRootId = id }).ToList();

    //     _nostifyMock.Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
    //         .ReturnsAsync(_containerMock.Object);

    //     _nostifyMock.Setup(n => n.GetEventStoreContainerAsync())
    //         .ReturnsAsync(_containerMock.Object);

    //     _nostifyMock.Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
    //         .ReturnsAsync(_containerMock.Object);

    //     _containerMock.Setup(c => c.GetItemLinqQueryable<TestAggregate>(true, null, null))
    //         .Returns(testAggregates.AsQueryable().BuildMock().Object);

    //     _containerMock.Setup(c => c.GetItemLinqQueryable<Event>(true, null, null))
    //         .Returns(testEvents.AsQueryable().BuildMock().Object);

    //     // Act
    //     await _projectionInitializer.InitContainerAsync<TestProjection, TestAggregate>(_nostifyMock.Object, _httpClientMock.Object);

    //     // Assert
    //     _containerMock.Verify(c => c.DeleteAllBulkAsync<TestProjection>(), Times.Once);
    //     _containerMock.Verify(c => c.DoBulkUpsertAsync(It.IsAny<List<TestProjection>>()), Times.AtLeastOnce);
    // }

    // [Fact]
    // public async Task InitAllUninitialized_ShouldInitializeAllUninitializedProjections()
    // {
    //     // Arrange
    //     var uninitializedProjections = new List<TestProjection>
    //     {
    //         new TestProjection { id = Guid.NewGuid(), initialized = false },
    //         new TestProjection { id = Guid.NewGuid(), initialized = false }
    //     };

    //     _nostifyMock.Setup(n => n.GetProjectionContainerAsync<TestProjection>())
    //         .ReturnsAsync(_containerMock.Object);

    //     _containerMock.Setup(c => c.GetItemLinqQueryable<TestProjection>(true, null, null))
    //         .Returns(uninitializedProjections.AsQueryable().BuildMock().Object);

    //     // Act
    //     await _projectionInitializer.InitAllUninitialized<TestProjection>(_nostifyMock.Object, _httpClientMock.Object);

    //     // Assert
    //     _containerMock.Verify(c => c.DoBulkUpsertAsync(It.IsAny<List<TestProjection>>()), Times.AtLeastOnce);
    // }
}