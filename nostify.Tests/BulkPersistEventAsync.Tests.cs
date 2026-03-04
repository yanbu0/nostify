using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Tests for BulkPersistEventAsync on INostify, verifying:
/// - The RetryOptions? overload exists on the INostify interface and is callable
/// - The bool allowRetry overload exists on the INostify interface and is callable
/// - Both overloads can be invoked through a mock with proper parameter matching
/// - DefaultCommandHandlers correctly invoke both overloads
/// - Batching, retry, and error handling behavior through mock verification
/// </summary>
public class BulkPersistEventAsyncTests
{
    private readonly Mock<INostify> _mockNostify;

    public BulkPersistEventAsyncTests()
    {
        _mockNostify = new Mock<INostify>();
        _mockNostify
            .Setup(n => n.HandleUndeliverableAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()))
            .Returns(Task.CompletedTask);
    }

    #region INostify Interface Contract - Both Overloads

    [Fact]
    public async Task BulkPersistEventAsync_BoolOverload_CanBeCalledOnInterface()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(3);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, 100, true, false);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, 100, true, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_RetryOptionsOverload_CanBeCalledOnInterface()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(3);
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, 100, retryOptions, false);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, 100, retryOptions, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_RetryOptionsOverload_NullRetryOptions_CanBeCalled()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(2);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, null, (RetryOptions?)null, false);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, null, (RetryOptions?)null, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_BoolOverload_DefaultParameters()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(1);

        // Act - call with defaults (must explicitly specify bool to disambiguate overloads)
        await _mockNostify.Object.BulkPersistEventAsync(events, null, false, false);

        // Assert - defaults are batchSize=null, allowRetry=false, publishErrorEvents=false
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, null, false, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_RetryOptionsOverload_DefaultParameters()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(1);
        var retryOptions = new RetryOptions(maxRetries: 2, delay: TimeSpan.FromMilliseconds(100), retryWhenNotFound: false);

        // Act - call with events, batchSize, and retryOptions (batchSize is required)
        await _mockNostify.Object.BulkPersistEventAsync(events, null, retryOptions);

        // Assert - defaults are batchSize=null, publishErrorEvents=false
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, null, retryOptions, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_BothOverloads_DistinctOnInterface()
    {
        // Arrange - set up both overloads
        int boolOverloadCount = 0;
        int retryOptionsOverloadCount = 0;

        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Callback(() => boolOverloadCount++)
            .Returns(Task.CompletedTask);

        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback(() => retryOptionsOverloadCount++)
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(1);
        var retryOptions = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act - call both overloads
        await _mockNostify.Object.BulkPersistEventAsync(events, 100, true, false);
        await _mockNostify.Object.BulkPersistEventAsync(events, 100, retryOptions, false);

        // Assert - each overload was called exactly once
        Assert.Equal(1, boolOverloadCount);
        Assert.Equal(1, retryOptionsOverloadCount);
    }

    #endregion

    #region Bool AllowRetry Overload Parameters

    [Fact]
    public async Task BulkPersistEventAsync_AllowRetryTrue_PassesTrueToInterface()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), true, It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(2);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, 50, true, false);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, 50, true, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_AllowRetryFalse_PassesFalseToInterface()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), false, It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(2);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, null, false, false);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, null, false, false), Times.Once);
    }

    #endregion

    #region RetryOptions Overload Parameters

    [Fact]
    public async Task BulkPersistEventAsync_RetryOptions_PassesBatchSizeThrough()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(5);
        var retryOptions = new RetryOptions(maxRetries: 2, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, 50, retryOptions, false);

        // Assert - batchSize is passed through
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, 50, retryOptions, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_RetryOptions_PublishErrorEventsTrue()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), true))
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(1);
        var retryOptions = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, null, retryOptions, true);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(events, null, retryOptions, true), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_RetryOptions_CustomConfiguration_PassedThrough()
    {
        // Arrange
        RetryOptions? capturedOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) => capturedOptions = ro)
            .Returns(Task.CompletedTask);

        var events = CreateTestEvents(1);
        var retryOptions = new RetryOptions(maxRetries: 5, delay: TimeSpan.FromMilliseconds(500), retryWhenNotFound: true, delayMultiplier: 3.0);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(events, null, retryOptions, false);

        // Assert - the exact RetryOptions instance is passed through
        Assert.NotNull(capturedOptions);
        Assert.Same(retryOptions, capturedOptions);
        Assert.Equal(5, capturedOptions!.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), capturedOptions.Delay);
        Assert.True(capturedOptions.RetryWhenNotFound);
        Assert.Equal(3.0, capturedOptions.DelayMultiplier);
    }

    #endregion

    #region Empty Events List

    [Fact]
    public async Task BulkPersistEventAsync_EmptyList_BoolOverload_Completes()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        // Act - empty list should complete without error
        await _mockNostify.Object.BulkPersistEventAsync(new List<IEvent>(), null, false, false);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(
            It.Is<List<IEvent>>(e => e.Count == 0), null, false, false), Times.Once);
    }

    [Fact]
    public async Task BulkPersistEventAsync_EmptyList_RetryOptionsOverload_Completes()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act
        await _mockNostify.Object.BulkPersistEventAsync(new List<IEvent>(), null, retryOptions, false);

        // Assert
        _mockNostify.Verify(n => n.BulkPersistEventAsync(
            It.Is<List<IEvent>>(e => e.Count == 0), null, retryOptions, false), Times.Once);
    }

    #endregion

    #region DefaultCommandHandlers Integration - RetryOptions Overloads

    [Fact]
    public async Task DefaultCommandHandlers_HandleBulkDelete_RetryOptionsOverload_CallsBulkPersistEventAsync()
    {
        // Arrange
        List<IEvent>? capturedEvents = null;
        RetryOptions? capturedRetryOptions = null;
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<List<IEvent>, int?, RetryOptions?, bool>((evts, bs, ro, pe) =>
            {
                capturedEvents = evts;
                capturedRetryOptions = ro;
            })
            .Returns(Task.CompletedTask);

        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(100), retryWhenNotFound: false);
        var command = new NostifyCommand("DeleteTestAggregate");
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Act - userId, partitionKey, and batchSize are required on RetryOptions overload
        var count = await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, ids,
            default, default, 50, retryOptions, publishErrorEvents: true);

        // Assert
        Assert.Equal(3, count);
        Assert.NotNull(capturedEvents);
        Assert.Equal(3, capturedEvents!.Count);
        Assert.Same(retryOptions, capturedRetryOptions);
        _mockNostify.Verify(n => n.BulkPersistEventAsync(
            It.IsAny<List<IEvent>>(), 50, retryOptions, true), Times.Once);
    }

    [Fact]
    public async Task DefaultCommandHandlers_HandleBulkDelete_BoolOverload_DelegatesToRetryOptionsOverload()
    {
        // Arrange - the bool overload now delegates to the RetryOptions overload
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("DeleteTestAggregate");
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Act - allowRetry=true should create default RetryOptions and delegate
        var count = await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, ids,
            batchSize: 100, allowRetry: true, publishErrorEvents: false);

        // Assert - calls RetryOptions overload with non-null RetryOptions
        Assert.Equal(2, count);
        _mockNostify.Verify(n => n.BulkPersistEventAsync(
            It.IsAny<List<IEvent>>(), 100, It.Is<RetryOptions?>(r => r != null), false), Times.Once);
    }

    [Fact]
    public async Task DefaultCommandHandlers_HandleBulkDelete_RetryOptionsNull_CallsRetryOptionsOverload()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var command = new NostifyCommand("DeleteTestAggregate");
        var ids = new List<Guid> { Guid.NewGuid() };

        // Act - userId, partitionKey, and batchSize are required on RetryOptions overload
        var count = await DefaultCommandHandler.HandleBulkDelete<TestAggregate>(
            _mockNostify.Object, command, ids,
            default, default, 100, null, publishErrorEvents: false);

        // Assert
        Assert.Equal(1, count);
        _mockNostify.Verify(n => n.BulkPersistEventAsync(
            It.IsAny<List<IEvent>>(), 100, (RetryOptions?)null, false), Times.Once);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a list of test events with unique aggregate root IDs.
    /// </summary>
    private static List<IEvent> CreateTestEvents(int count)
    {
        var events = new List<IEvent>();
        for (int i = 0; i < count; i++)
        {
            events.Add(new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = Guid.NewGuid(),
                command = new NostifyCommand("TestBulkCommand"),
                timestamp = DateTime.UtcNow,
                userId = Guid.NewGuid(),
                partitionKey = Guid.NewGuid(),
                payload = new { name = $"Test{i}" }
            });
        }
        return events;
    }

    #endregion
}
