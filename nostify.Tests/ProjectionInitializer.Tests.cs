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
    public void INostify_InitContainerAsync_DefaultLoopSize_ShouldBe100()
    {
        var method = typeof(INostify)
            .GetMethods()
            .Single(m => m.Name == nameof(INostify.InitContainerAsync) && m.GetParameters().Length == 2);

        var loopSizeParameter = method.GetParameters().Single(p => p.Name == "loopSize");

        Assert.True(loopSizeParameter.IsOptional);
        Assert.Equal(100, Assert.IsType<int>(loopSizeParameter.DefaultValue));
    }

    [Fact]
    public void Nostify_InitContainerAsync_DefaultLoopSize_ShouldBe100()
    {
        var method = typeof(Nostify)
            .GetMethods()
            .Single(m => m.Name == nameof(Nostify.InitContainerAsync) && m.GetParameters().Length == 2);

        var loopSizeParameter = method.GetParameters().Single(p => p.Name == "loopSize");

        Assert.True(loopSizeParameter.IsOptional);
        Assert.Equal(100, Assert.IsType<int>(loopSizeParameter.DefaultValue));
    }

    [Fact]
    public void ProjectionInitializer_InitContainerAsync_DefaultLoopSize_ShouldBe100()
    {
        var interfaceMethod = typeof(IProjectionInitializer)
            .GetMethods()
            .Single(m => m.Name == nameof(IProjectionInitializer.InitContainerAsync));
        var interfaceLoopSizeParameter = interfaceMethod.GetParameters().Single(p => p.Name == "loopSize");

        Assert.True(interfaceLoopSizeParameter.IsOptional);
        Assert.Equal(100, Assert.IsType<int>(interfaceLoopSizeParameter.DefaultValue));

        var implementationMethods = typeof(ProjectionInitializer)
            .GetMethods()
            .Where(m => m.Name == nameof(ProjectionInitializer.InitContainerAsync))
            .ToList();

        Assert.NotEmpty(implementationMethods);
        Assert.All(implementationMethods, method =>
        {
            var loopSizeParameter = method.GetParameters().Single(p => p.Name == "loopSize");
            Assert.True(loopSizeParameter.IsOptional);
            Assert.Equal(100, Assert.IsType<int>(loopSizeParameter.DefaultValue));
        });
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