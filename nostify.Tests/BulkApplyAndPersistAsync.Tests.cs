using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Tests for BulkApplyAndPersistAsync on INostify, verifying:
/// - The RetryOptions? overload exists on the INostify interface and is callable
/// - The bool allowRetry overload exists on the INostify interface and is callable
/// - Both overloads can be invoked through a mock with proper parameter matching
/// - Both overloads are distinct and route to the correct setup
/// - Parameter passing (retryOptions, publishErrorEvents) works correctly
/// </summary>
public class BulkApplyAndPersistAsyncTests
{
    private readonly Mock<INostify> _mockNostify;

    public BulkApplyAndPersistAsyncTests()
    {
        _mockNostify = new Mock<INostify>();
    }

    #region INostify Interface Contract - Both Overloads

    [Fact]
    public async Task BulkApplyAndPersistAsync_BoolOverload_CanBeCalledOnInterface()
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
        var result = await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, allowRetry: true, publishErrorEvents: false);

        // Assert
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, true, false), Times.Once);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_RetryOptionsOverload_CanBeCalledOnInterface()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1" };
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(100), retryWhenNotFound: false);

        // Act
        var result = await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions: retryOptions, publishErrorEvents: true);

        // Assert
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions, true), Times.Once);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_RetryOptionsOverload_NullRetryOptions_CanBeCalled()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1" };

        // Act
        var result = await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions: null, publishErrorEvents: false);

        // Assert
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, (RetryOptions?)null, false), Times.Once);
    }

    #endregion

    #region Overload Disambiguation

    [Fact]
    public async Task BulkApplyAndPersistAsync_BothOverloads_AreDistinctOnInterface()
    {
        // Arrange
        int boolOverloadCount = 0;
        int retryOptionsOverloadCount = 0;

        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<bool>(), It.IsAny<bool>()))
            .Callback(() => boolOverloadCount++)
            .ReturnsAsync(new List<TestAggregate>());

        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback(() => retryOptionsOverloadCount++)
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1" };
        var retryOptions = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, allowRetry: true, publishErrorEvents: false);
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions: retryOptions, publishErrorEvents: false);

        // Assert - each overload was called exactly once
        Assert.Equal(1, boolOverloadCount);
        Assert.Equal(1, retryOptionsOverloadCount);
    }

    #endregion

    #region RetryOptions Parameter Passing

    [Fact]
    public async Task BulkApplyAndPersistAsync_RetryOptions_CustomConfiguration_PassedThrough()
    {
        // Arrange
        RetryOptions? capturedOptions = null;
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Callback<Container, string, string[], RetryOptions?, bool>((c, id, e, ro, pe) => capturedOptions = ro)
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1" };
        var retryOptions = new RetryOptions(maxRetries: 5, delay: TimeSpan.FromMilliseconds(500), retryWhenNotFound: true, delayMultiplier: 3.0);

        // Act
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions: retryOptions, publishErrorEvents: false);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Same(retryOptions, capturedOptions);
        Assert.Equal(5, capturedOptions!.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), capturedOptions.Delay);
        Assert.True(capturedOptions.RetryWhenNotFound);
        Assert.Equal(3.0, capturedOptions.DelayMultiplier);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_RetryOptions_PublishErrorEventsTrue()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<RetryOptions?>(), true))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1" };
        var retryOptions = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions: retryOptions, publishErrorEvents: true);

        // Assert
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions, true), Times.Once);
    }

    #endregion

    #region Bool AllowRetry Parameters

    [Fact]
    public async Task BulkApplyAndPersistAsync_AllowRetryTrue_PassesTrueToInterface()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                true, It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1", "event2" };

        // Act
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "testProp", events, allowRetry: true, publishErrorEvents: false);

        // Assert
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "testProp", events, true, false), Times.Once);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_AllowRetryFalse_PassesFalseToInterface()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                false, It.IsAny<bool>()))
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

    #region Default Parameters

    [Fact]
    public async Task BulkApplyAndPersistAsync_BoolOverload_DefaultParameters()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1" };

        // Act - call with defaults (allowRetry=false, publishErrorEvents=false)
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, allowRetry: false);

        // Assert
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, false, false), Times.Once);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_RetryOptionsOverload_DefaultParameters()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var events = new string[] { "event1" };
        var retryOptions = new RetryOptions(maxRetries: 2, delay: TimeSpan.FromMilliseconds(100), retryWhenNotFound: false);

        // Act - call with retryOptions, publishErrorEvents defaults to false
        await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions: retryOptions);

        // Assert
        _mockNostify.Verify(n => n.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", events, retryOptions, false), Times.Once);
    }

    #endregion

    #region Empty Events Array

    [Fact]
    public async Task BulkApplyAndPersistAsync_EmptyArray_BoolOverload_ReturnsEmptyList()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();

        // Act
        var result = await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", Array.Empty<string>(), allowRetry: false, publishErrorEvents: false);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_EmptyArray_RetryOptionsOverload_ReturnsEmptyList()
    {
        // Arrange
        _mockNostify
            .Setup(n => n.BulkApplyAndPersistAsync<TestAggregate>(
                It.IsAny<Container>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<TestAggregate>());

        var mockContainer = new Mock<Container>();
        var retryOptions = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act
        var result = await _mockNostify.Object.BulkApplyAndPersistAsync<TestAggregate>(
            mockContainer.Object, "id", Array.Empty<string>(), retryOptions: retryOptions, publishErrorEvents: false);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion
}
