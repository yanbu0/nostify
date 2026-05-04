using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace nostify.Tests;

public class RetryableContainerTests
{
    #region Constructor

    [Fact]
    public void Constructor_NullContainer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RetryableContainer(null!, new RetryOptions()));
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var mockContainer = new Mock<Container>();
        Assert.Throws<ArgumentNullException>(() => new RetryableContainer(mockContainer.Object, null!));
    }

    [Fact]
    public void Constructor_ValidArgs_SetsProperties()
    {
        var mockContainer = new Mock<Container>();
        var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.FromSeconds(2), retryWhenNotFound: true);

        var retryable = new RetryableContainer(mockContainer.Object, options);

        Assert.Same(mockContainer.Object, retryable.Container);
        Assert.Same(options, retryable.Options);
    }

    #endregion

    #region WithRetry extension

    [Fact]
    public void WithRetry_ReturnsRetryableContainer()
    {
        var mockContainer = new Mock<Container>();
        var options = new RetryOptions();

        IRetryableContainer retryable = mockContainer.Object.WithRetry(options);

        Assert.IsType<RetryableContainer>(retryable);
        Assert.Same(mockContainer.Object, retryable.Container);
        Assert.Same(options, retryable.Options);
    }

    [Fact]
    public void WithRetry_BoolTrue_SetsRetryWhenNotFound()
    {
        var mockContainer = new Mock<Container>();

        IRetryableContainer retryable = mockContainer.Object.WithRetry(true);

        Assert.IsType<RetryableContainer>(retryable);
        Assert.Same(mockContainer.Object, retryable.Container);
        Assert.True(retryable.Options.RetryWhenNotFound);
        Assert.Equal(3, retryable.Options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), retryable.Options.Delay);
    }

    [Fact]
    public void WithRetry_BoolFalse_KeepsRetryWhenNotFoundFalse()
    {
        var mockContainer = new Mock<Container>();

        IRetryableContainer retryable = mockContainer.Object.WithRetry(false);

        Assert.IsType<RetryableContainer>(retryable);
        Assert.False(retryable.Options.RetryWhenNotFound);
        Assert.Equal(3, retryable.Options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), retryable.Options.Delay);
    }

    #endregion

    #region ApplyAndPersistAsync - Success on first attempt

    [Fact]
    public async Task ApplyAndPersist_SuccessOnFirstAttempt_ReturnsResult()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = CreateSucceedingContainer<TestAggregate>(aggId);
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        var result = await retryable.ApplyAndPersistAsync<TestAggregate>(evt);

        Assert.NotNull(result);
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ApplyAndPersistAsync - Retry on NotFound

    [Fact]
    public async Task ApplyAndPersist_RetryWhenNotFound_SucceedsAfterRetries()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = CreateNotFoundThenSucceedContainer<TestAggregate>(2, aggId);
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        var result = await retryable.ApplyAndPersistAsync<TestAggregate>(evt);

        Assert.NotNull(result);
        // Initial attempt + 2 retries = 3 calls
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion

    #region ApplyAndPersistAsync - No retry when RetryWhenNotFound=false

    [Fact]
    public async Task ApplyAndPersist_RetryWhenNotFoundFalse_CallsOnNotFound()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = CreateAlwaysNotFoundContainer<TestAggregate>();
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        bool notFoundCalled = false;
        var result = await retryable.ApplyAndPersistAsync<TestAggregate>(evt,
            onNotFound: () => { notFoundCalled = true; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.True(notFoundCalled);
        // Only one attempt, no retries
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ApplyAndPersistAsync - Exhausts retries

    [Fact]
    public async Task ApplyAndPersist_ExhaustsRetries_CallsOnExhausted()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = CreateAlwaysNotFoundContainer<TestAggregate>();
        var options = new RetryOptions(maxRetries: 2, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        bool exhaustedCalled = false;
        var result = await retryable.ApplyAndPersistAsync<TestAggregate>(evt,
            onExhausted: () => { exhaustedCalled = true; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.True(exhaustedCalled);
        // Initial attempt + 2 retries = 3 calls total
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion

    #region ApplyAndPersistAsync - Exception calls onException

    [Fact]
    public async Task ApplyAndPersist_Exception_CallsOnException()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something broke"));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        Exception? caughtException = null;
        var result = await retryable.ApplyAndPersistAsync<TestAggregate>(evt,
            onException: (ex) => { caughtException = ex; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.NotNull(caughtException);
        Assert.IsType<InvalidOperationException>(caughtException);
        Assert.Equal("Something broke", caughtException.Message);
        // No retries for non-transient exceptions
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAndPersist_Exception_NoCallback_Throws()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something broke"));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            retryable.ApplyAndPersistAsync<TestAggregate>(evt));
    }

    #endregion

    #region ApplyAndPersistAsync with projectionBaseAggregateId

    [Fact]
    public async Task ApplyAndPersist_WithProjectionId_SuccessOnFirstAttempt()
    {
        var aggId = Guid.NewGuid();
        var projId = Guid.NewGuid();
        var mockContainer = CreateSucceedingContainer<TestProjection>(aggId);
        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        var result = await retryable.ApplyAndPersistAsync<TestProjection>(evt, projId);

        Assert.NotNull(result);
    }

    #endregion

    #region ReadItemAsync

    [Fact]
    public async Task ReadItem_SuccessOnFirstAttempt_ReturnsResponse()
    {
        var mockContainer = new Mock<Container>();
        var mockResponse = new Mock<ItemResponse<TestAggregate>>();
        mockResponse.Setup(r => r.Resource).Returns(new TestAggregate { id = Guid.NewGuid(), name = "Test" });
        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var result = await retryable.ReadItemAsync<TestAggregate>(
            Guid.NewGuid().ToString(), new PartitionKey(Guid.NewGuid().ToString()));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ReadItem_NotFoundRetryWhenNotFoundFalse_CallsOnNotFound()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        bool notFoundCalled = false;
        var result = await retryable.ReadItemAsync<TestAggregate>(
            "test", new PartitionKey("pk"),
            onNotFound: () => { notFoundCalled = true; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.True(notFoundCalled);
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadItem_NotFoundRetryEnabled_RetriesThenSucceeds()
    {
        var mockContainer = new Mock<Container>();
        int callCount = 0;
        var mockResponse = new Mock<ItemResponse<TestAggregate>>();
        mockResponse.Setup(r => r.Resource).Returns(new TestAggregate());

        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, pk, opts, ct) =>
            {
                int current = Interlocked.Increment(ref callCount);
                if (current <= 2)
                    throw new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0);
                return Task.FromResult(mockResponse.Object);
            });

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var result = await retryable.ReadItemAsync<TestAggregate>(
            "test", new PartitionKey("pk"));

        Assert.NotNull(result);
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ReadItem_ExhaustsRetries_CallsOnExhausted()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0));

        var options = new RetryOptions(maxRetries: 2, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        bool exhaustedCalled = false;
        var result = await retryable.ReadItemAsync<TestAggregate>(
            "test", new PartitionKey("pk"),
            onExhausted: () => { exhaustedCalled = true; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.True(exhaustedCalled);
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ReadItem_NonTransientException_CallsOnException()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Bad request"));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        Exception? caught = null;
        await retryable.ReadItemAsync<TestAggregate>(
            "test", new PartitionKey("pk"),
            onException: (ex) => { caught = ex; return Task.CompletedTask; });

        Assert.NotNull(caught);
        Assert.IsType<InvalidOperationException>(caught);
    }

    #endregion

    #region CreateItemAsync

    [Fact]
    public async Task CreateItem_Success_ReturnsResponse()
    {
        var mockContainer = new Mock<Container>();
        var mockResponse = new Mock<ItemResponse<TestAggregate>>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var result = await retryable.CreateItemAsync(
            new TestAggregate(), new PartitionKey("pk"));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateItem_Exception_CallsOnException()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Conflict", HttpStatusCode.Conflict, 0, string.Empty, 0));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        Exception? caught = null;
        await retryable.CreateItemAsync(
            new TestAggregate(), new PartitionKey("pk"),
            onException: (ex) => { caught = ex; return Task.CompletedTask; });

        Assert.NotNull(caught);
        Assert.IsType<CosmosException>(caught);
    }

    [Fact]
    public async Task CreateItem_Exception_NoCallback_Throws()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Conflict", HttpStatusCode.Conflict, 0, string.Empty, 0));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        await Assert.ThrowsAsync<CosmosException>(() =>
            retryable.CreateItemAsync(new TestAggregate(), new PartitionKey("pk")));
    }

    [Fact]
    public async Task CreateItem_NullPartitionKey_Success_CallsContainerWithoutPK()
    {
        var mockContainer = new Mock<Container>();
        var mockResponse = new Mock<ItemResponse<TestAggregate>>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var result = await retryable.CreateItemAsync(
            new TestAggregate(), (PartitionKey?)null);

        Assert.NotNull(result);
        // Verify CreateItemAsync was called with a null partition key
        mockContainer.Verify(c => c.CreateItemAsync(
            It.IsAny<TestAggregate>(), null,
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateItem_NullPartitionKey_Exception_CallsOnException()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Conflict", HttpStatusCode.Conflict, 0, string.Empty, 0));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        Exception? caught = null;
        await retryable.CreateItemAsync(
            new TestAggregate(), (PartitionKey?)null,
            onException: (ex) => { caught = ex; return Task.CompletedTask; });

        Assert.NotNull(caught);
        Assert.IsType<CosmosException>(caught);
    }

    [Fact]
    public async Task CreateItem_NoPK_Success_DelegatesToNullablePKOverload()
    {
        var mockContainer = new Mock<Container>();
        var mockResponse = new Mock<ItemResponse<TestAggregate>>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var result = await retryable.CreateItemAsync(new TestAggregate());

        Assert.NotNull(result);
        // Should call Container with null partition key (since PK is null internally)
        mockContainer.Verify(c => c.CreateItemAsync(
            It.IsAny<TestAggregate>(), null,
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateItem_NoPK_Exception_NoCallback_Throws()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Conflict", HttpStatusCode.Conflict, 0, string.Empty, 0));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        await Assert.ThrowsAsync<CosmosException>(() =>
            retryable.CreateItemAsync(new TestAggregate()));
    }

    [Fact]
    public async Task CreateItem_Conflict409OnFirstAttempt_CallsOnExceptionCallback()
    {
        // On the first attempt (attempt == 0) a 409 should still be treated as an error.
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Conflict", HttpStatusCode.Conflict, 0, string.Empty, 0));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        Exception? caught = null;
        await retryable.CreateItemAsync(
            new TestAggregate(), new PartitionKey("pk"),
            onException: (ex) => { caught = ex; return Task.CompletedTask; });

        Assert.NotNull(caught);
        Assert.IsType<CosmosException>(caught);
    }

    [Fact]
    public async Task CreateItem_Conflict409AfterTooManyRequests_TreatsAsIdempotentSuccess()
    {
        // Simulates the Cosmos bulk SDK scenario: first call returns 429, retry returns 409
        // because the item was already committed before the throttle was applied.
        int callCount = 0;
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns<TestAggregate, PartitionKey, ItemRequestOptions, CancellationToken>((item, pk, opts, ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new CosmosException("Too many requests", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);
                throw new CosmosException("Conflict", HttpStatusCode.Conflict, 0, string.Empty, 0);
            });

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        // Should NOT throw — the 409 on retry is treated as idempotent success
        var result = await retryable.CreateItemAsync(new TestAggregate(), new PartitionKey("pk"));

        Assert.Null(result); // returns default on idempotent path
        Assert.Equal(2, callCount); // called twice: once for 429, once for 409
    }

    [Fact]
    public async Task CreateItem_TooManyRequests_ExhaustedRetries_ThrowsOriginalException()
    {
        // ExceptionDispatchInfo.Capture(ce).Throw() must be used instead of `throw ce`
        // to preserve the original exception identity (same reference) and stack trace.
        var originalException = new CosmosException("Too many requests", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(originalException);

        var options = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var thrown = await Assert.ThrowsAsync<CosmosException>(() =>
            retryable.CreateItemAsync(new TestAggregate(), new PartitionKey("pk")));

        // The rethrown exception must be the same object, not a copy created by `throw ce`
        Assert.Same(originalException, thrown);
    }

    [Fact]
    public async Task ReadItem_TooManyRequests_ExhaustedRetries_ThrowsOriginalException()
    {
        var originalException = new CosmosException("Too many requests", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(originalException);

        var options = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var thrown = await Assert.ThrowsAsync<CosmosException>(() =>
            retryable.ReadItemAsync<TestAggregate>("id", new PartitionKey("pk")));

        Assert.Same(originalException, thrown);
    }

    [Fact]
    public async Task UpsertItem_TooManyRequests_ExhaustedRetries_ThrowsOriginalException()
    {
        var originalException = new CosmosException("Too many requests", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(originalException);

        var options = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var thrown = await Assert.ThrowsAsync<CosmosException>(() =>
            retryable.UpsertItemAsync(new TestAggregate(), new PartitionKey("pk")));

        Assert.Same(originalException, thrown);
    }

    #endregion

    #region UpsertItemAsync

    [Fact]
    public async Task UpsertItem_Success_ReturnsResponse()
    {
        var mockContainer = new Mock<Container>();
        var mockResponse = new Mock<ItemResponse<TestAggregate>>();
        mockContainer
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var result = await retryable.UpsertItemAsync(new TestAggregate());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpsertItem_Exception_CallsOnException()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<TestAggregate>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Upsert failed"));

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        Exception? caught = null;
        await retryable.UpsertItemAsync(new TestAggregate(),
            onException: (ex) => { caught = ex; return Task.CompletedTask; });

        Assert.NotNull(caught);
        Assert.IsType<InvalidOperationException>(caught);
    }

    #endregion

    #region DoBulkCreateEventAsync

    [Fact]
    public async Task DoBulkCreateEventAsync_BulkEnabled_CreatesAllEvents()
    {
        var clientOptions = new CosmosClientOptions { AllowBulkExecution = true };
        var mockClient = new Mock<CosmosClient>();
        mockClient.Setup(c => c.ClientOptions).Returns(clientOptions);

        var mockDatabase = new Mock<Database>();
        mockDatabase.Setup(d => d.Client).Returns(mockClient.Object);

        var mockContainer = new Mock<Container>();
        mockContainer.Setup(c => c.Database).Returns(mockDatabase.Object);

        int createCalls = 0;
        var createdEvents = new List<IEvent>();
        var mockResponse = new Mock<ItemResponse<IEvent>>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<IEvent>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEvent, PartitionKey?, ItemRequestOptions, CancellationToken>((evt, pk, opts, ct) =>
            {
                Interlocked.Increment(ref createCalls);
                createdEvents.Add(evt);
            })
            .ReturnsAsync(mockResponse.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var events = new List<IEvent>
        {
            CreateTestEvent(Guid.NewGuid()),
            CreateTestEvent(Guid.NewGuid()),
            CreateTestEvent(Guid.NewGuid())
        };

        await retryable.DoBulkCreateEventAsync(events);

        Assert.Equal(events.Count, createCalls);
        Assert.Equal(events.Count, createdEvents.Count);
        Assert.All(events, e => Assert.Contains(e, createdEvents));
    }

    [Fact]
    public async Task DoBulkCreateEventAsync_BulkDisabled_ThrowsNostifyException()
    {
        var clientOptions = new CosmosClientOptions { AllowBulkExecution = false };
        var mockClient = new Mock<CosmosClient>();
        mockClient.Setup(c => c.ClientOptions).Returns(clientOptions);

        var mockDatabase = new Mock<Database>();
        mockDatabase.Setup(d => d.Client).Returns(mockClient.Object);

        var mockContainer = new Mock<Container>();
        mockContainer.Setup(c => c.Database).Returns(mockDatabase.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var events = new List<IEvent> { CreateTestEvent(Guid.NewGuid()) };

        var ex = await Assert.ThrowsAsync<NostifyException>(() => retryable.DoBulkCreateEventAsync(events));
        Assert.Equal("Bulk operations must be enabled for this container", ex.Message);
    }

    [Fact]
    public async Task DoBulkUpsertEventAsync_BulkEnabled_UpsertsAllEvents()
    {
        var clientOptions = new CosmosClientOptions { AllowBulkExecution = true };
        var mockClient = new Mock<CosmosClient>();
        mockClient.Setup(c => c.ClientOptions).Returns(clientOptions);

        var mockDatabase = new Mock<Database>();
        mockDatabase.Setup(d => d.Client).Returns(mockClient.Object);

        var mockContainer = new Mock<Container>();
        mockContainer.Setup(c => c.Database).Returns(mockDatabase.Object);

        int upsertCalls = 0;
        var upsertedEvents = new List<IEvent>();
        var mockResponse = new Mock<ItemResponse<IEvent>>();
        mockContainer
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<IEvent>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEvent, PartitionKey?, ItemRequestOptions, CancellationToken>((evt, pk, opts, ct) =>
            {
                Interlocked.Increment(ref upsertCalls);
                upsertedEvents.Add(evt);
            })
            .ReturnsAsync(mockResponse.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var events = new List<IEvent>
        {
            CreateTestEvent(Guid.NewGuid()),
            CreateTestEvent(Guid.NewGuid()),
            CreateTestEvent(Guid.NewGuid())
        };

        await retryable.DoBulkUpsertEventAsync(events);

        Assert.Equal(events.Count, upsertCalls);
        Assert.Equal(events.Count, upsertedEvents.Count);
        Assert.All(events, e => Assert.Contains(e, upsertedEvents));
    }

    [Fact]
    public async Task DoBulkUpsertEventAsync_BulkDisabled_ThrowsNostifyException()
    {
        var clientOptions = new CosmosClientOptions { AllowBulkExecution = false };
        var mockClient = new Mock<CosmosClient>();
        mockClient.Setup(c => c.ClientOptions).Returns(clientOptions);

        var mockDatabase = new Mock<Database>();
        mockDatabase.Setup(d => d.Client).Returns(mockClient.Object);

        var mockContainer = new Mock<Container>();
        mockContainer.Setup(c => c.Database).Returns(mockDatabase.Object);

        var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);

        var events = new List<IEvent> { CreateTestEvent(Guid.NewGuid()) };

        var ex = await Assert.ThrowsAsync<NostifyException>(() => retryable.DoBulkUpsertEventAsync(events));
        Assert.Equal("Bulk operations must be enabled for this container", ex.Message);
    }

    #endregion

    #region MaxRetries = 0

    [Fact]
    public async Task ApplyAndPersist_MaxRetriesZero_SingleAttemptOnly()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = CreateAlwaysNotFoundContainer<TestAggregate>();
        var options = new RetryOptions(maxRetries: 0, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        bool exhaustedCalled = false;
        var result = await retryable.ApplyAndPersistAsync<TestAggregate>(evt,
            onExhausted: () => { exhaustedCalled = true; return Task.CompletedTask; });

        Assert.Null(result);
        Assert.True(exhaustedCalled);
        // Only 1 call (initial attempt, 0 retries)
        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Logging

    [Fact]
    public async Task ApplyAndPersist_WithLogging_LogsRetryAttempts()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = CreateNotFoundThenSucceedContainer<TestAggregate>(1, aggId);
        var mockLogger = new Mock<ILogger>();
        var options = new RetryOptions(
            maxRetries: 3,
            delay: TimeSpan.FromMilliseconds(1),
            retryWhenNotFound: true,
            logRetries: true,
            logger: mockLogger.Object);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        await retryable.ApplyAndPersistAsync<TestAggregate>(evt);

        // Should have logged at least one retry message
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Null callbacks

    [Fact]
    public async Task ApplyAndPersist_NullCallbacks_StillReturnsNull()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = CreateAlwaysNotFoundContainer<TestAggregate>();
        var options = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        // All callbacks null - should not throw
        var result = await retryable.ApplyAndPersistAsync<TestAggregate>(evt);

        Assert.Null(result);
    }

    [Fact]
    public async Task ApplyAndPersist_ExhaustedNullCallback_StillReturnsNull()
    {
        var aggId = Guid.NewGuid();
        var mockContainer = CreateAlwaysNotFoundContainer<TestAggregate>();
        var options = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: true);
        var retryable = new RetryableContainer(mockContainer.Object, options);
        var evt = CreateTestEvent(aggId);

        var result = await retryable.ApplyAndPersistAsync<TestAggregate>(evt);

        Assert.Null(result);
    }

    #endregion

    #region Test helpers

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

    private static Mock<Container> CreateSucceedingContainer<T>(Guid aggregateRootId) where T : NostifyObject, new()
    {
        var mockContainer = new Mock<Container>();
        var existing = new T();
        existing.GetType().GetProperty("id")!.SetValue(existing, aggregateRootId);

        var mockReadResponse = new Mock<ItemResponse<T>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existing);

        mockContainer
            .Setup(c => c.ReadItemAsync<T>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockReadResponse.Object);

        var updated = new T();
        updated.GetType().GetProperty("id")!.SetValue(updated, aggregateRootId);
        var mockPatchResponse = new Mock<ItemResponse<T>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(updated);

        mockContainer
            .Setup(c => c.PatchItemAsync<T>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        return mockContainer;
    }

    private static Mock<Container> CreateNotFoundThenSucceedContainer<T>(int notFoundCount, Guid aggregateRootId) where T : NostifyObject, new()
    {
        var mockContainer = new Mock<Container>();
        int callCount = 0;

        var existing = new T();
        existing.GetType().GetProperty("id")!.SetValue(existing, aggregateRootId);
        var mockReadResponse = new Mock<ItemResponse<T>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existing);

        mockContainer
            .Setup(c => c.ReadItemAsync<T>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, pk, opts, ct) =>
            {
                int current = Interlocked.Increment(ref callCount);
                if (current <= notFoundCount)
                    throw new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0);
                return Task.FromResult(mockReadResponse.Object);
            });

        var updated = new T();
        updated.GetType().GetProperty("id")!.SetValue(updated, aggregateRootId);
        var mockPatchResponse = new Mock<ItemResponse<T>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(updated);

        mockContainer
            .Setup(c => c.PatchItemAsync<T>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        return mockContainer;
    }

    private static Mock<Container> CreateAlwaysNotFoundContainer<T>() where T : NostifyObject, new()
    {
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.ReadItemAsync<T>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0));

        return mockContainer;
    }

    #endregion
}
