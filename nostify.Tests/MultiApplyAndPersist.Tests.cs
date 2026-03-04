using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Tests for MultiApplyAndPersistAsync with RetryOptions parameter,
/// validating the retry flow through CreateApplyAndPersistTask → RetryableContainer.
/// Uses the internal Nostify constructor via InternalsVisibleTo.
/// </summary>
public class MultiApplyAndPersistTests
{
    private readonly Nostify _nostify;

    public MultiApplyAndPersistTests()
    {
        _nostify = new Nostify(
            new NostifyCosmosClient(),
            "/tenantId",
            Guid.Empty,
            "localhost:9092",
            new Mock<IProducer<string, string>>().Object,
            new Mock<IHttpClientFactory>().Object
        );
    }

    #region Helpers

    /// <summary>
    /// Creates a bulk-enabled mock Container that succeeds on ReadItemAsync and PatchItemAsync for TestProjection.
    /// </summary>
    private static Mock<Container> CreateBulkSucceedingProjectionMock(Guid projectionId)
    {
        var mockContainer = CreateBulkEnabledContainerBase();

        var existing = new TestProjection { id = projectionId, name = "Original" };
        var mockReadResponse = new Mock<ItemResponse<TestProjection>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existing);

        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockReadResponse.Object);

        var updated = new TestProjection { id = projectionId, name = "Updated" };
        var mockPatchResponse = new Mock<ItemResponse<TestProjection>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(updated);

        mockContainer
            .Setup(c => c.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        return mockContainer;
    }

    /// <summary>
    /// Creates a bulk-enabled mock Container where ReadItemAsync throws NotFound the first N times,
    /// then succeeds on subsequent calls for TestProjection.
    /// </summary>
    private static Mock<Container> CreateBulkNotFoundThenSucceedProjectionMock(int notFoundCount, Guid projectionId)
    {
        var mockContainer = CreateBulkEnabledContainerBase();
        int callCount = 0;

        var existing = new TestProjection { id = projectionId, name = "Original" };
        var mockReadResponse = new Mock<ItemResponse<TestProjection>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existing);

        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, pk, opts, ct) =>
            {
                int current = Interlocked.Increment(ref callCount);
                if (current <= notFoundCount)
                {
                    throw new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0);
                }
                return Task.FromResult(mockReadResponse.Object);
            });

        var updated = new TestProjection { id = projectionId, name = "Updated" };
        var mockPatchResponse = new Mock<ItemResponse<TestProjection>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(updated);

        mockContainer
            .Setup(c => c.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        return mockContainer;
    }

    /// <summary>
    /// Creates a bulk-enabled mock Container where ReadItemAsync always throws NotFound for TestProjection.
    /// </summary>
    private static Mock<Container> CreateBulkAlwaysNotFoundProjectionMock()
    {
        var mockContainer = CreateBulkEnabledContainerBase();

        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0));

        return mockContainer;
    }

    /// <summary>
    /// Creates a base mock Container with Database.Client.ClientOptions.AllowBulkExecution = true
    /// so that ValidateBulkEnabled passes.
    /// </summary>
    private static Mock<Container> CreateBulkEnabledContainerBase()
    {
        var clientOptions = new CosmosClientOptions { AllowBulkExecution = true };
        var mockClient = new Mock<CosmosClient>();
        mockClient.Setup(c => c.ClientOptions).Returns(clientOptions);

        var mockDatabase = new Mock<Database>();
        mockDatabase.Setup(d => d.Client).Returns(mockClient.Object);

        var mockContainer = new Mock<Container>();
        mockContainer.Setup(c => c.Database).Returns(mockDatabase.Object);

        return mockContainer;
    }

    /// <summary>
    /// Creates an Event suitable for apply-and-persist test scenarios.
    /// </summary>
    private static Event CreateTestEvent(Guid? aggregateRootId = null, Guid? partitionKey = null)
    {
        var aggId = aggregateRootId ?? Guid.NewGuid();
        var pk = partitionKey ?? Guid.NewGuid();
        return new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = aggId,
            command = new NostifyCommand("UpdateTestProjection"),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = pk,
            payload = new { name = "Updated" }
        };
    }

    #endregion

    #region With RetryOptions — success scenarios

    [Fact]
    public async Task MultiApplyAndPersist_WithRetryOptions_SuccessOnFirstAttempt_Returns1()
    {
        // Arrange
        var projId = Guid.NewGuid();
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkSucceedingProjectionMock(projId);
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        // Act
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, new List<Guid> { projId }, retryOptions: retryOptions);

        // Assert — ReadItemAsync called exactly once (no retries needed)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task MultiApplyAndPersist_WithRetryOptions_SucceedsAfterOneRetry_Returns1()
    {
        // Arrange
        var projId = Guid.NewGuid();
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkNotFoundThenSucceedProjectionMock(1, projId);
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        // Act
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, new List<Guid> { projId }, retryOptions: retryOptions);

        // Assert — ReadItemAsync called twice (1 NotFound + 1 success)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task MultiApplyAndPersist_WithRetryOptions_SucceedsAfterThreeRetries_Returns1()
    {
        // Arrange
        var projId = Guid.NewGuid();
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkNotFoundThenSucceedProjectionMock(3, projId);
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        // Act
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, new List<Guid> { projId }, retryOptions: retryOptions);

        // Assert — ReadItemAsync called 4 times (3 NotFound + 1 success)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(4));

        Assert.Equal(1, result.Count);
    }

    #endregion

    #region With RetryOptions — error scenarios

    [Fact]
    public async Task MultiApplyAndPersist_WithRetryOptions_ExhaustsRetries_Returns0()
    {
        // Arrange
        var projId = Guid.NewGuid();
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkAlwaysNotFoundProjectionMock();
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        // Act
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, new List<Guid> { projId }, retryOptions: retryOptions);

        // Assert — ReadItemAsync called 4 times (initial + 3 retries)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(4));

        // No successful results (all retries exhausted)
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task MultiApplyAndPersist_WithRetryOptions_RetryWhenNotFoundFalse_Returns0()
    {
        // Arrange
        var projId = Guid.NewGuid();
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkAlwaysNotFoundProjectionMock();
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        // Act
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, new List<Guid> { projId }, retryOptions: retryOptions);

        // Assert — ReadItemAsync called only once (no retry because RetryWhenNotFound=false)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task MultiApplyAndPersist_WithRetryOptions_MaxRetries0_SingleAttemptOnly()
    {
        // Arrange
        var projId = Guid.NewGuid();
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkAlwaysNotFoundProjectionMock();
        var retryOptions = new RetryOptions(maxRetries: 0, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        // Act
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, new List<Guid> { projId }, retryOptions: retryOptions);

        // Assert — ReadItemAsync called exactly once (maxRetries=0 means no retries)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(0, result.Count);
    }

    #endregion

    #region Without RetryOptions (null)

    [Fact]
    public async Task MultiApplyAndPersist_WithoutRetryOptions_SuccessOnFirstAttempt_Returns1()
    {
        // Arrange
        var projId = Guid.NewGuid();
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkSucceedingProjectionMock(projId);

        // Act — retryOptions defaults to null
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, new List<Guid> { projId });

        // Assert — ReadItemAsync called once
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(1, result.Count);
    }

    #endregion

    #region Multiple projections

    [Fact]
    public async Task MultiApplyAndPersist_MultipleProjections_WithRetryOptions_ReturnsCorrectCount()
    {
        // Arrange — 3 projections, all succeed on first attempt
        var projIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkEnabledContainerBase();
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        // Set up ReadItemAsync to return a projection for any ID
        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, pk, opts, ct) =>
            {
                var proj = new TestProjection { id = Guid.Parse(id), name = "Original" };
                var mockResp = new Mock<ItemResponse<TestProjection>>();
                mockResp.Setup(r => r.Resource).Returns(proj);
                return Task.FromResult(mockResp.Object);
            });

        var mockPatchResponse = new Mock<ItemResponse<TestProjection>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(new TestProjection { name = "Updated" });
        mockContainer
            .Setup(c => c.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        // Act
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, projIds, retryOptions: retryOptions);

        // Assert — all 3 projections processed
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task MultiApplyAndPersist_MultipleProjections_MixedOutcomes_WithRetryOptions()
    {
        // Arrange — 2 projections: one succeeds, one always NotFound
        var successId = Guid.NewGuid();
        var failId = Guid.NewGuid();
        var projIds = new List<Guid> { successId, failId };
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkEnabledContainerBase();
        var retryOptions = new RetryOptions(maxRetries: 1, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        var existingProj = new TestProjection { id = successId, name = "Original" };
        var mockReadResp = new Mock<ItemResponse<TestProjection>>();
        mockReadResp.Setup(r => r.Resource).Returns(existingProj);

        // ReadItemAsync: succeed for successId, NotFound for failId
        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, pk, opts, ct) =>
            {
                if (id == successId.ToString())
                {
                    return Task.FromResult(mockReadResp.Object);
                }
                throw new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0);
            });

        var mockPatchResp = new Mock<ItemResponse<TestProjection>>();
        mockPatchResp.Setup(r => r.Resource).Returns(new TestProjection { id = successId, name = "Updated" });
        mockContainer
            .Setup(c => c.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResp.Object);

        // Act
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, projIds, retryOptions: retryOptions);

        // Assert — successId succeeded, failId exhausted retries → result count is 1
        // ReadItemAsync: 1 call for successId + 2 calls for failId (initial + 1 retry) = 3
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

        Assert.Equal(1, result.Count);
    }

    #endregion

    #region Backwards compatibility — List<P> overload

    [Fact]
    public async Task MultiApplyAndPersist_ListOverload_WithRetryOptions_DelegatesToGuidOverload()
    {
        // Arrange
        var projId = Guid.NewGuid();
        var evt = CreateTestEvent();
        var mockContainer = CreateBulkSucceedingProjectionMock(projId);
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);
        var projections = new List<TestProjection> { new TestProjection { id = projId, name = "Original" } };

        // Act — use the List<P> overload
        List<TestProjection> result = await _nostify.MultiApplyAndPersistAsync<TestProjection>(
            mockContainer.Object, evt, projections, retryOptions: retryOptions);

        // Assert
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(1, result.Count);
    }

    #endregion
}
