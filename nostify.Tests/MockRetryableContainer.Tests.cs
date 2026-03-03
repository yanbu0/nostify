using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace nostify.Tests;

public class MockRetryableContainerTests
{
    #region Constructor - success result

    [Fact]
    public void Constructor_WithResult_SetsOptions()
    {
        var result = new TestAggregate { id = Guid.NewGuid(), name = "Test" };
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.FromSeconds(2), retryWhenNotFound: true);
        var mock = new MockRetryableContainer<TestAggregate>(result, options);

        Assert.Same(options, mock.Options);
        Assert.Equal(0, mock.ApplyCallCount);
        Assert.Equal(0, mock.ReadCallCount);
        Assert.Empty(mock.AppliedEvents);
    }

    [Fact]
    public void Constructor_WithResult_DefaultOptions()
    {
        var mock = new MockRetryableContainer<TestAggregate>();

        Assert.NotNull(mock.Options);
        Assert.Equal(3, mock.Options.MaxRetries); // default
    }

    #endregion

    #region ApplyAndPersistAsync - success

    [Fact]
    public async Task ApplyAndPersist_WithResult_ReturnsResult()
    {
        var aggId = Guid.NewGuid();
        var expected = new TestAggregate { id = aggId, name = "Updated" };
        var mock = new MockRetryableContainer<TestAggregate>(expected);
        var evt = CreateTestEvent(aggId);

        var result = await mock.ApplyAndPersistAsync<TestAggregate>(evt);

        Assert.NotNull(result);
        Assert.Equal(aggId, result.id);
        Assert.Equal("Updated", result.name);
        Assert.Equal(1, mock.ApplyCallCount);
        Assert.Single(mock.AppliedEvents);
        Assert.Same(evt, mock.AppliedEvents[0]);
    }

    [Fact]
    public async Task ApplyAndPersist_MultipleCalls_TracksAllEvents()
    {
        var expected = new TestAggregate { id = Guid.NewGuid() };
        var mock = new MockRetryableContainer<TestAggregate>(expected);

        var evt1 = CreateTestEvent(Guid.NewGuid());
        var evt2 = CreateTestEvent(Guid.NewGuid());
        var evt3 = CreateTestEvent(Guid.NewGuid());

        await mock.ApplyAndPersistAsync<TestAggregate>(evt1);
        await mock.ApplyAndPersistAsync<TestAggregate>(evt2);
        await mock.ApplyAndPersistAsync<TestAggregate>(evt3);

        Assert.Equal(3, mock.ApplyCallCount);
        Assert.Equal(3, mock.AppliedEvents.Count);
    }

    #endregion

    #region ApplyAndPersistAsync - exception

    [Fact]
    public async Task ApplyAndPersist_WithException_CallsOnException()
    {
        var ex = new InvalidOperationException("Simulated failure");
        var mock = new MockRetryableContainer<TestAggregate>(ex);
        var evt = CreateTestEvent(Guid.NewGuid());

        Exception? caught = null;
        var result = await mock.ApplyAndPersistAsync<TestAggregate>(evt,
            onException: (e) => { caught = e; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.Same(ex, caught);
        Assert.Equal(1, mock.ApplyCallCount);
    }

    [Fact]
    public async Task ApplyAndPersist_WithException_NoCallback_Throws()
    {
        var ex = new InvalidOperationException("Simulated failure");
        var mock = new MockRetryableContainer<TestAggregate>(ex);
        var evt = CreateTestEvent(Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mock.ApplyAndPersistAsync<TestAggregate>(evt));
    }

    #endregion

    #region ApplyAndPersistAsync - simulateNotFound

    [Fact]
    public async Task ApplyAndPersist_SimulateNotFound_CallsOnNotFound()
    {
        var mock = new MockRetryableContainer<TestAggregate>(simulateNotFound: true, simulateExhausted: false);
        var evt = CreateTestEvent(Guid.NewGuid());

        bool notFoundCalled = false;
        var result = await mock.ApplyAndPersistAsync<TestAggregate>(evt,
            onNotFound: () => { notFoundCalled = true; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.True(notFoundCalled);
    }

    #endregion

    #region ApplyAndPersistAsync - simulateExhausted

    [Fact]
    public async Task ApplyAndPersist_SimulateExhausted_CallsOnExhausted()
    {
        var mock = new MockRetryableContainer<TestAggregate>(simulateNotFound: false, simulateExhausted: true);
        var evt = CreateTestEvent(Guid.NewGuid());

        bool exhaustedCalled = false;
        var result = await mock.ApplyAndPersistAsync<TestAggregate>(evt,
            onExhausted: () => { exhaustedCalled = true; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.True(exhaustedCalled);
    }

    #endregion

    #region ApplyAndPersistAsync with projectionBaseAggregateId

    [Fact]
    public async Task ApplyAndPersist_WithProjectionId_DelegatesToSameLogic()
    {
        var expected = new TestProjection { id = Guid.NewGuid(), name = "Projected" };
        var mock = new MockRetryableContainer<TestProjection>(expected);
        var evt = CreateTestEvent(Guid.NewGuid());

        var result = await mock.ApplyAndPersistAsync<TestProjection>(evt, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal(expected.name, result.name);
        Assert.Equal(1, mock.ApplyCallCount);
    }

    #endregion

    #region ReadItemAsync

    [Fact]
    public async Task ReadItem_IncrementsReadCallCount()
    {
        var mock = new MockRetryableContainer<TestAggregate>(new TestAggregate());

        await mock.ReadItemAsync<TestAggregate>("id", new PartitionKey("pk"));
        await mock.ReadItemAsync<TestAggregate>("id2", new PartitionKey("pk"));

        Assert.Equal(2, mock.ReadCallCount);
    }

    [Fact]
    public async Task ReadItem_WithException_CallsOnException()
    {
        var ex = new InvalidOperationException("Read failed");
        var mock = new MockRetryableContainer<TestAggregate>(ex);

        Exception? caught = null;
        await mock.ReadItemAsync<TestAggregate>("id", new PartitionKey("pk"),
            onException: (e) => { caught = e; return Task.CompletedTask; });

        Assert.Same(ex, caught);
    }

    [Fact]
    public async Task ReadItem_SimulateNotFound_CallsOnNotFound()
    {
        var mock = new MockRetryableContainer<TestAggregate>(simulateNotFound: true, simulateExhausted: false);

        bool called = false;
        await mock.ReadItemAsync<TestAggregate>("id", new PartitionKey("pk"),
            onNotFound: () => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    #endregion

    #region CreateItemAsync

    [Fact]
    public async Task CreateItem_WithException_CallsOnException()
    {
        var ex = new InvalidOperationException("Create failed");
        var mock = new MockRetryableContainer<TestAggregate>(ex);

        Exception? caught = null;
        await mock.CreateItemAsync(new TestAggregate(), new PartitionKey("pk"),
            onException: (e) => { caught = e; return Task.CompletedTask; });

        Assert.Same(ex, caught);
    }

    [Fact]
    public async Task CreateItem_NoException_ReturnsDefault()
    {
        var mock = new MockRetryableContainer<TestAggregate>(new TestAggregate());

        var result = await mock.CreateItemAsync(new TestAggregate(), new PartitionKey("pk"));

        Assert.Null(result); // Mock always returns default for ItemResponse
    }

    [Fact]
    public async Task CreateItem_NullPartitionKey_WithException_CallsOnException()
    {
        var ex = new InvalidOperationException("Create failed");
        var mock = new MockRetryableContainer<TestAggregate>(ex);

        Exception? caught = null;
        await mock.CreateItemAsync(new TestAggregate(), (PartitionKey?)null,
            onException: (e) => { caught = e; return Task.CompletedTask; });

        Assert.Same(ex, caught);
    }

    [Fact]
    public async Task CreateItem_NullPartitionKey_NoException_ReturnsDefault()
    {
        var mock = new MockRetryableContainer<TestAggregate>(new TestAggregate());

        var result = await mock.CreateItemAsync(new TestAggregate(), (PartitionKey?)null);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateItem_NoPK_WithException_CallsOnException()
    {
        var ex = new InvalidOperationException("Create failed");
        var mock = new MockRetryableContainer<TestAggregate>(ex);

        Exception? caught = null;
        await mock.CreateItemAsync(new TestAggregate(),
            onException: (e) => { caught = e; return Task.CompletedTask; });

        Assert.Same(ex, caught);
    }

    [Fact]
    public async Task CreateItem_NoPK_NoException_ReturnsDefault()
    {
        var mock = new MockRetryableContainer<TestAggregate>(new TestAggregate());

        var result = await mock.CreateItemAsync(new TestAggregate());

        Assert.Null(result);
    }

    #endregion

    #region UpsertItemAsync

    [Fact]
    public async Task UpsertItem_WithException_CallsOnException()
    {
        var ex = new InvalidOperationException("Upsert failed");
        var mock = new MockRetryableContainer<TestAggregate>(ex);

        Exception? caught = null;
        await mock.UpsertItemAsync(new TestAggregate(),
            onException: (e) => { caught = e; return Task.CompletedTask; });

        Assert.Same(ex, caught);
    }

    [Fact]
    public async Task UpsertItem_NoException_ReturnsDefault()
    {
        var mock = new MockRetryableContainer<TestAggregate>(new TestAggregate());

        var result = await mock.UpsertItemAsync(new TestAggregate());

        Assert.Null(result);
    }

    #endregion

    #region NotFoundUntilAttempt

    [Fact]
    public async Task NotFoundUntilAttempt_ReturnsNullThenSucceeds()
    {
        var expected = new TestAggregate { id = Guid.NewGuid(), name = "Eventual" };
        var mock = new MockRetryableContainer<TestAggregate>(notFoundUntilAttempt: 2, applyResult: expected);
        var evt = CreateTestEvent(Guid.NewGuid());

        // First 2 calls return null
        var result1 = await mock.ApplyAndPersistAsync<TestAggregate>(evt);
        Assert.Null(result1);

        var result2 = await mock.ApplyAndPersistAsync<TestAggregate>(evt);
        Assert.Null(result2);

        // Third call returns the result
        var result3 = await mock.ApplyAndPersistAsync<TestAggregate>(evt);
        Assert.NotNull(result3);
        Assert.Equal("Eventual", result3.name);
        Assert.Equal(3, mock.ApplyCallCount);
    }

    #endregion

    #region Container property

    [Fact]
    public void Container_IsNullInMock()
    {
        var mock = new MockRetryableContainer<TestAggregate>();

        // Container is null! in mock - this is expected
        Assert.Null(mock.Container);
    }

    #endregion

    #region Helpers

    private static Event CreateTestEvent(Guid aggregateRootId)
    {
        return new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = aggregateRootId,
            command = new NostifyCommand("Update"),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = Guid.NewGuid(),
            payload = new { name = "Updated" }
        };
    }

    #endregion
}
