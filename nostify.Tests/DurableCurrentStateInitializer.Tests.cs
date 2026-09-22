using Microsoft.DurableTask;
using Moq;

namespace nostify.Tests;

/// <summary>Unit tests for durable aggregate current-state initializer configuration.</summary>
public class DurableCurrentStateInitializerTests
{
    [Fact]
    public void Constructor_RejectsInvalidBatchSize()
    {
        var nostify = new Mock<INostify>();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(nostify.Object, "aggregate-init", batchSize: 0));
    }

    [Fact]
    public void Constructor_RejectsInvalidConcurrentBatchCount()
    {
        var nostify = new Mock<INostify>();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(nostify.Object, "aggregate-init", concurrentBatchCount: 0));
    }

    [Fact]
    public void PageInfo_RejectsNegativePageNumber()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DurableCurrentStatePageInfo(-1));

    [Fact]
    public void DefaultTaskOptions_UsesRetryPolicy()
    {
        TaskOptions options = DurableCurrentStateInitializer<TestAggregate>.CreateDefaultTaskOptions();

        Assert.NotNull(options);
    }
}
