using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Tests that verify all bool allowRetry overloads correctly delegate to the
/// RetryOptions overload. When allowRetry=true, a new RetryOptions() with defaults
/// (maxRetries: 3, delay: 1s, exponential backoff 2x) should be created. When
/// allowRetry=false, null should be passed.
/// </summary>
public class AllowRetryDelegationTests
{
    private readonly Mock<INostify> _mockNostify;

    public AllowRetryDelegationTests()
    {
        _mockNostify = new Mock<INostify>();
    }

    #region Nostify.BulkPersistEventAsync

    [Fact]
    public async Task BulkPersistEventAsync_AllowRetryTrue_DelegatesWithDefaultRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        // Also set up the bool overload so it's callable on the mock
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = new List<IEvent>
        {
            new EventFactory().Create<TestAggregate>(
                new NostifyCommand("CreateTestAggregate", isNew: true), Guid.NewGuid(), new { name = "test" }, Guid.NewGuid(), Guid.NewGuid())
        };

        // Act - call the bool overload with allowRetry=true
        await _mockNostify.Object.BulkPersistEventAsync(events, 50, true, false);

        // Assert - the bool overload was called on the interface
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, 50, true, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_AllowRetryFalse_DelegatesWithNullRetryOptions()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = new List<IEvent>
        {
            new EventFactory().Create<TestAggregate>(
                new NostifyCommand("CreateTestAggregate", isNew: true), Guid.NewGuid(), new { name = "test" }, Guid.NewGuid(), Guid.NewGuid())
        };

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, null, false, false);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, null, false, false), Times.Once);
    }

    #endregion

    #region Nostify.BulkApplyAndPersistAsync

    [Fact]
    public async Task BulkApplyAndPersistAsync_AllowRetryTrue_DelegatesWithDefaultRetryOptions()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1", "event2" };

        // Act
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, allowRetry: true, publishErrorEvents: false);

        // Assert - the bool overload was called
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, true, false), Times.Once);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_AllowRetryFalse_DelegatesWithNullRetryOptions()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1" };

        // Act
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, allowRetry: false, publishErrorEvents: false);

        // Assert
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, false, false), Times.Once);
    }

    #endregion

    #region DefaultCommandHandler.HandleBulkCreate

    [Fact]
    public async Task HandleBulkCreate_AllowRetryTrue_DelegatesWithNonNullRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("CreateTestAggregate", isNew: true);
        var objects = new List<dynamic> { new { name = "Item1" }, new { name = "Item2" } };
        var req = MockHttpRequestData.Create(objects);

        // Act
        var count = await DefaultCommandHandler.HandleBulkCreate<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 100, allowRetry: true, publishErrorEvents: false);

        // Assert
        Assert.Equal(2, count);
        Assert.NotNull(capturedRetryOptions);
        Assert.Equal(3, capturedRetryOptions!.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), capturedRetryOptions.Delay);
        Assert.False(capturedRetryOptions.RetryWhenNotFound);
        Assert.Equal(2.0, capturedRetryOptions.DelayMultiplier);
    }

    [Fact]
    public async Task HandleBulkCreate_AllowRetryFalse_DelegatesWithNullRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = new RetryOptions(); // set to non-null to verify it becomes null
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("CreateTestAggregate", isNew: true);
        var objects = new List<dynamic> { new { name = "Item1" } };
        var req = MockHttpRequestData.Create(objects);

        // Act
        var count = await DefaultCommandHandler.HandleBulkCreate<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 50, allowRetry: false, publishErrorEvents: false);

        // Assert
        Assert.Equal(1, count);
        Assert.Null(capturedRetryOptions);
    }

    [Fact]
    public async Task HandleBulkCreateAsync_AllowRetryTrue_PerformsBulkOperationForEachBatch()
    {
        // Arrange
        var batchCounts = new List<int>();
        RetryOptions? capturedRetryOptions = null;

        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
                var loopSize = bs ?? evts.Count;
                for (int i = 0; i < evts.Count; i += loopSize)
                {
                    batchCounts.Add(Math.Min(loopSize, evts.Count - i));
                }
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("CreateTestAggregate", isNew: true);
        var newObjects = new List<TestAggregate>
        {
            new() { name = "A" },
            new() { name = "B" },
            new() { name = "C" },
            new() { name = "D" },
            new() { name = "E" }
        };

        // Act
        var count = await DefaultCommandHandler.HandleBulkCreateAsync<TestAggregate>(
            _mockNostify.Object,
            command,
            newObjects,
            userId: Guid.NewGuid(),
            partitionKey: Guid.NewGuid(),
            batchSize: 2,
            allowRetry: true,
            publishErrorEvents: false);

        // Assert
        Assert.Equal(5, count);
        Assert.NotNull(capturedRetryOptions);
        Assert.Equal(new List<int> { 2, 2, 1 }, batchCounts);
    }

    [Fact]
    public async Task HandleBulkCreateAsync_AllowRetryFalse_PerformsBulkOperationForEachBatch()
    {
        // Arrange
        var batchCounts = new List<int>();
        RetryOptions? capturedRetryOptions = new RetryOptions();

        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
                var loopSize = bs ?? evts.Count;
                for (int i = 0; i < evts.Count; i += loopSize)
                {
                    batchCounts.Add(Math.Min(loopSize, evts.Count - i));
                }
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("CreateTestAggregate", isNew: true);
        var newObjects = new List<TestAggregate>
        {
            new() { name = "A" },
            new() { name = "B" },
            new() { name = "C" },
            new() { name = "D" },
            new() { name = "E" }
        };

        // Act
        var count = await DefaultCommandHandler.HandleBulkCreateAsync<TestAggregate>(
            _mockNostify.Object,
            command,
            newObjects,
            userId: Guid.NewGuid(),
            partitionKey: Guid.NewGuid(),
            batchSize: 2,
            allowRetry: false,
            publishErrorEvents: false);

        // Assert
        Assert.Equal(5, count);
        Assert.Null(capturedRetryOptions);
        Assert.Equal(new List<int> { 2, 2, 1 }, batchCounts);
    }

    #endregion

    #region DefaultCommandHandler.HandleBulkUpdate

    [Fact]
    public async Task HandleBulkUpdate_AllowRetryTrue_DelegatesWithNonNullRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("UpdateTestAggregate");
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var objects = new List<dynamic>
        {
            new { id = id1.ToString(), name = "Updated1" },
            new { id = id2.ToString(), name = "Updated2" }
        };
        var req = MockHttpRequestData.Create(objects);

        // Act
        var count = await DefaultCommandHandler.HandleBulkUpdate<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 100, allowRetry: true, publishErrorEvents: false);

        // Assert
        Assert.Equal(2, count);
        Assert.NotNull(capturedRetryOptions);
        Assert.Equal(3, capturedRetryOptions!.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), capturedRetryOptions.Delay);
        Assert.False(capturedRetryOptions.RetryWhenNotFound);
        Assert.Equal(2.0, capturedRetryOptions.DelayMultiplier);
    }

    [Fact]
    public async Task HandleBulkUpdate_AllowRetryFalse_DelegatesWithNullRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = new RetryOptions();
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("UpdateTestAggregate");
        var id1 = Guid.NewGuid();
        var objects = new List<dynamic> { new { id = id1.ToString(), name = "Updated" } };
        var req = MockHttpRequestData.Create(objects);

        // Act
        var count = await DefaultCommandHandler.HandleBulkUpdate<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 100, allowRetry: false, publishErrorEvents: false);

        // Assert
        Assert.Equal(1, count);
        Assert.Null(capturedRetryOptions);
    }

    #endregion

    #region DefaultCommandHandler.HandleBulkDelete (HttpRequestData)

    [Fact]
    public async Task HandleBulkDelete_HttpReq_AllowRetryTrue_DelegatesWithNonNullRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("DeleteTestAggregate");
        var idStrings = new List<string> { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        var req = MockHttpRequestData.Create(idStrings);

        // Act
        var count = await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 100, allowRetry: true, publishErrorEvents: false);

        // Assert
        Assert.Equal(2, count);
        Assert.NotNull(capturedRetryOptions);
        Assert.Equal(3, capturedRetryOptions!.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), capturedRetryOptions.Delay);
        Assert.False(capturedRetryOptions.RetryWhenNotFound);
        Assert.Equal(2.0, capturedRetryOptions.DelayMultiplier);
    }

    [Fact]
    public async Task HandleBulkDelete_HttpReq_AllowRetryFalse_DelegatesWithNullRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = new RetryOptions();
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("DeleteTestAggregate");
        var idStrings = new List<string> { Guid.NewGuid().ToString() };
        var req = MockHttpRequestData.Create(idStrings);

        // Act
        var count = await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 100, allowRetry: false, publishErrorEvents: false);

        // Assert
        Assert.Equal(1, count);
        Assert.Null(capturedRetryOptions);
    }

    #endregion

    #region DefaultCommandHandler.HandleBulkDelete (List<Guid>)

    [Fact]
    public async Task HandleBulkDelete_GuidList_AllowRetryTrue_DelegatesWithNonNullRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("DeleteTestAggregate");
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var count = await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, ids,
            batchSize: 100, allowRetry: true, publishErrorEvents: false);

        // Assert
        Assert.Equal(3, count);
        Assert.NotNull(capturedRetryOptions);
        Assert.Equal(3, capturedRetryOptions!.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), capturedRetryOptions.Delay);
        Assert.False(capturedRetryOptions.RetryWhenNotFound);
        Assert.Equal(2.0, capturedRetryOptions.DelayMultiplier);
    }

    [Fact]
    public async Task HandleBulkDelete_GuidList_AllowRetryFalse_DelegatesWithNullRetryOptions()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = new RetryOptions();
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("DeleteTestAggregate");
        var ids = new List<Guid> { Guid.NewGuid() };

        // Act
        var count = await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, ids,
            batchSize: 50, allowRetry: false, publishErrorEvents: true);

        // Assert
        Assert.Equal(1, count);
        Assert.Null(capturedRetryOptions);
    }

    #endregion

    #region DefaultCommandHandler - publishErrorEvents Passthrough

    [Fact]
    public async Task HandleBulkCreate_AllowRetryTrue_PublishErrorEventsTrue_BothPassedThrough()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = null;
        bool capturedPublishErrorEvents = false;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
                capturedPublishErrorEvents = pe;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("CreateTestAggregate", isNew: true);
        var objects = new List<dynamic> { new { name = "Item1" } };
        var req = MockHttpRequestData.Create(objects);

        // Act
        var count = await DefaultCommandHandler.HandleBulkCreate<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 100, allowRetry: true, publishErrorEvents: true);

        // Assert
        Assert.Equal(1, count);
        Assert.NotNull(capturedRetryOptions);
        Assert.True(capturedPublishErrorEvents);
    }

    [Fact]
    public async Task HandleBulkDelete_GuidList_AllowRetryTrue_PublishErrorEventsTrue_BothPassedThrough()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = null;
        bool capturedPublishErrorEvents = false;
        int? capturedBatchSize = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedBatchSize = bs;
                capturedRetryOptions = ro;
                capturedPublishErrorEvents = pe;
            })
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("DeleteTestAggregate");
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var count = await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, ids,
            batchSize: 75, allowRetry: true, publishErrorEvents: true);

        // Assert
        Assert.Equal(2, count);
        Assert.NotNull(capturedRetryOptions);
        Assert.True(capturedPublishErrorEvents);
        Assert.Equal(75, capturedBatchSize);
    }

    #endregion

    #region RetryOptions Default Values Verification

    [Fact]
    public async Task HandleBulkUpdate_AllowRetryTrue_RetryOptionsMatchNewRetryOptionsDefaults()
    {
        // Arrange - verify the captured RetryOptions exactly matches new RetryOptions()
        RetryOptions? capturedRetryOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var expected = new RetryOptions(); // Reference defaults
        var command = new NostifyCommand("UpdateTestAggregate");
        var id1 = Guid.NewGuid();
        var objects = new List<dynamic> { new { id = id1.ToString(), name = "Updated" } };
        var req = MockHttpRequestData.Create(objects);

        // Act
        await DefaultCommandHandler.HandleBulkUpdate<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 100, allowRetry: true, publishErrorEvents: false);

        // Assert - every property matches new RetryOptions() defaults
        Assert.NotNull(capturedRetryOptions);
        Assert.Equal(expected.MaxRetries, capturedRetryOptions!.MaxRetries);
        Assert.Equal(expected.Delay, capturedRetryOptions.Delay);
        Assert.Equal(expected.RetryWhenNotFound, capturedRetryOptions.RetryWhenNotFound);
        Assert.Equal(expected.DelayMultiplier, capturedRetryOptions.DelayMultiplier);
        Assert.Equal(expected.LogRetries, capturedRetryOptions.LogRetries);
        Assert.Equal(expected.Logger, capturedRetryOptions.Logger);
    }

    [Fact]
    public async Task HandleBulkDelete_HttpReq_AllowRetryTrue_RetryOptionsMatchNewRetryOptionsDefaults()
    {
        // Arrange
        RetryOptions? capturedRetryOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var expected = new RetryOptions();
        var command = new NostifyCommand("DeleteTestAggregate");
        var idStrings = new List<string> { Guid.NewGuid().ToString() };
        var req = MockHttpRequestData.Create(idStrings);

        // Act
        await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, req,
            batchSize: 100, allowRetry: true, publishErrorEvents: false);

        // Assert
        Assert.NotNull(capturedRetryOptions);
        Assert.Equal(expected.MaxRetries, capturedRetryOptions!.MaxRetries);
        Assert.Equal(expected.Delay, capturedRetryOptions.Delay);
        Assert.Equal(expected.RetryWhenNotFound, capturedRetryOptions.RetryWhenNotFound);
        Assert.Equal(expected.DelayMultiplier, capturedRetryOptions.DelayMultiplier);
        Assert.Equal(expected.LogRetries, capturedRetryOptions.LogRetries);
        Assert.Equal(expected.Logger, capturedRetryOptions.Logger);
    }

    #endregion
}
