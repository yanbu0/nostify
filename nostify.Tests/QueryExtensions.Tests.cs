using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Behavioral tests for feed-iterator query helpers.
/// </summary>
public sealed class QueryExtensionsTests
{
    [Fact]
    public async Task ReadFeedIteratorAsync_WithNoPages_ReturnsEmptyList()
    {
        var iterator = new Mock<FeedIterator<TestEntity>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(false);

        List<TestEntity> result = await iterator.Object.ReadFeedIteratorAsync();

        Assert.Empty(result);
        iterator.Verify(
            value => value.ReadNextAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReadFeedIteratorAsync_WithMultiplePages_ReturnsItemsInPageOrder()
    {
        TestEntity first = new("first");
        TestEntity second = new("second");
        TestEntity third = new("third");
        FeedResponse<TestEntity> firstPage = CreatePage(first, second);
        FeedResponse<TestEntity> secondPage = CreatePage(third);
        var iterator = new Mock<FeedIterator<TestEntity>>();
        iterator
            .SetupSequence(value => value.HasMoreResults)
            .Returns(true)
            .Returns(true)
            .Returns(false);
        iterator
            .SetupSequence(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstPage)
            .ReturnsAsync(secondPage);

        List<TestEntity> result = await iterator.Object.ReadFeedIteratorAsync();

        Assert.Equal([first, second, third], result);
    }

    [Fact]
    public async Task ReadFeedIteratorAsync_WhenPageReadFails_PreservesException()
    {
        var expected = new InvalidOperationException("query failed");
        var iterator = new Mock<FeedIterator<TestEntity>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        iterator
            .Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => iterator.Object.ReadFeedIteratorAsync());

        Assert.Same(expected, actual);
    }

    private static FeedResponse<TestEntity> CreatePage(params TestEntity[] entities)
    {
        var response = new Mock<FeedResponse<TestEntity>>();
        response.Setup(value => value.GetEnumerator()).Returns(() => entities.AsEnumerable().GetEnumerator());
        return response.Object;
    }

    // The model must be public so Castle DynamicProxy can construct proxies for
    // the strong-named Cosmos generic types used by these tests.
    public sealed record TestEntity(string Name);
}
