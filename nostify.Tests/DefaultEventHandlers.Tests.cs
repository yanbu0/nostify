using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace nostify.Tests;

public class DefaultEventHandlersTests
{
    private readonly Mock<INostify> _mockNostify;

    public DefaultEventHandlersTests()
    {
        _mockNostify = new Mock<INostify>();
        _mockNostify
            .Setup(n => n.HandleUndeliverableAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()))
            .Returns(Task.CompletedTask);
        _mockNostify
            .Setup(n => n.InitAsync<TestProjection>(It.IsAny<List<TestProjection>>()))
            .ReturnsAsync(new List<TestProjection>());
    }

    /// <summary>
    /// Helper to build a serialized NostifyKafkaTriggerEvent string for a given Event.
    /// </summary>
    private static string CreateKafkaTriggerEventString(Event evt)
    {
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = JsonConvert.SerializeObject(evt, SerializationSettings.NostifyDefault),
            Offset = 0,
            Partition = 0,
            Topic = "test-topic",
            Key = "test-key",
            Headers = Array.Empty<string>()
        };
        return JsonConvert.SerializeObject(kafkaEvent, SerializationSettings.NostifyDefault);
    }

    /// <summary>
    /// Helper to create an update Event for TestProjection.
    /// </summary>
    private static Event CreateUpdateEvent(Guid? aggregateRootId = null, Guid? partitionKey = null)
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

    /// <summary>
    /// Creates a mock Container where ReadItemAsync succeeds, returning a TestProjection,
    /// and PatchItemAsync succeeds. ApplyAndPersistAsync will return a non-null projection.
    /// </summary>
    private static Mock<Container> CreateSucceedingMockContainer(Guid aggregateRootId)
    {
        var mockContainer = new Mock<Container>();
        var existingProjection = new TestProjection { id = aggregateRootId, name = "Original" };

        var mockReadResponse = new Mock<ItemResponse<TestProjection>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existingProjection);

        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockReadResponse.Object);

        var updatedProjection = new TestProjection { id = aggregateRootId, name = "Updated" };
        var mockPatchResponse = new Mock<ItemResponse<TestProjection>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(updatedProjection);

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
    /// Creates a mock Container where ReadItemAsync throws NotFound for the first N calls
    /// (causing ApplyAndPersistAsync to return null), then succeeds on subsequent calls.
    /// This simulates eventual consistency where a projection is not yet available.
    /// </summary>
    private static Mock<Container> CreateNotFoundThenSucceedMockContainer(int notFoundCount, Guid aggregateRootId)
    {
        var mockContainer = new Mock<Container>();
        int callCount = 0;

        var existingProjection = new TestProjection { id = aggregateRootId, name = "Original" };
        var mockReadResponse = new Mock<ItemResponse<TestProjection>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existingProjection);

        // First N calls throw NotFound (ApplyAndPersistAsync catches this and returns null),
        // subsequent calls return the projection successfully
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

        var updatedProjection = new TestProjection { id = aggregateRootId, name = "Updated" };
        var mockPatchResponse = new Mock<ItemResponse<TestProjection>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(updatedProjection);

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
    /// Creates a mock Container where ReadItemAsync always throws NotFound
    /// (causing ApplyAndPersistAsync to always return null).
    /// This simulates the case where all retries are exhausted.
    /// </summary>
    private static Mock<Container> CreateAlwaysNotFoundMockContainer()
    {
        var mockContainer = new Mock<Container>();

        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0));

        return mockContainer;
    }

    #region Success on first attempt (no retry needed)

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_SuccessOnFirstAttempt_UpdatesProjection()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateSucceedingMockContainer(aggId);
        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        // Act
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>());

        // Assert - ReadItemAsync called exactly once (no retries)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - InitAsync was called with the updated projection
        _mockNostify.Verify(n => n.InitAsync<TestProjection>(It.Is<List<TestProjection>>(l => l.Count == 1)), Times.Once);

        // Assert - HandleUndeliverableAsync was NOT called
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Never);
    }

    #endregion

    #region Retry path: succeeds after 1 retry

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_RetryWhenNotFound_SucceedsAfterOneRetry()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        // ReadItemAsync throws NotFound 1 time (ApplyAndPersistAsync returns null), then succeeds
        var mockContainer = CreateNotFoundThenSucceedMockContainer(1, aggId);
        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(
            maxRetries: 3,
            delay: TimeSpan.FromMilliseconds(10),
            retryWhenNotFound: true
        );

        // Act
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        // Assert - ReadItemAsync called 2 times (1 not-found + 1 success)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Assert - InitAsync was called with the updated projection
        _mockNostify.Verify(n => n.InitAsync<TestProjection>(It.Is<List<TestProjection>>(l => l.Count == 1)), Times.Once);

        // Assert - HandleUndeliverableAsync was NOT called
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Never);
    }

    #endregion

    #region Retry path: succeeds after 3 retries

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_RetryWhenNotFound_SucceedsAfterThreeRetries()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        // ReadItemAsync throws NotFound 3 times, then succeeds on the 4th call (3rd retry)
        var mockContainer = CreateNotFoundThenSucceedMockContainer(3, aggId);
        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(
            maxRetries: 3,
            delay: TimeSpan.FromMilliseconds(10),
            retryWhenNotFound: true
        );

        // Act
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        // Assert - ReadItemAsync called 4 times (initial + 3 retries, last one succeeds)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));

        // Assert - InitAsync was called with the updated projection
        _mockNostify.Verify(n => n.InitAsync<TestProjection>(It.Is<List<TestProjection>>(l => l.Count == 1)), Times.Once);

        // Assert - HandleUndeliverableAsync was NOT called
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Never);
    }

    #endregion

    #region Retry path: all retries exhausted - sends to undeliverable

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_RetryWhenNotFound_ExhaustsRetriesAndHandlesUndeliverable()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        // Always return null from ApplyAndPersistAsync (ReadItemAsync always throws NotFound internally)
        var mockContainer = CreateAlwaysNotFoundMockContainer();
        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(
            maxRetries: 3,
            delay: TimeSpan.FromMilliseconds(10),
            retryWhenNotFound: true
        );

        // Act
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        // Assert - ReadItemAsync called 4 times (initial + 3 retries, all returned null)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));

        // Assert - HandleUndeliverableAsync WAS called with retry context
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => s.Contains("Retry")),
            It.Is<string>(s => s.Contains("Not found after 3 retries")),
            It.IsAny<IEvent>(),
            It.IsAny<ErrorCommand?>()), Times.Once);

        // Assert - InitAsync was called with an empty list (nothing succeeded)
        _mockNostify.Verify(n => n.InitAsync<TestProjection>(It.Is<List<TestProjection>>(l => l.Count == 0)), Times.Once);
    }

    #endregion

    #region No retry when RetryWhenNotFound is false

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_RetryWhenNotFoundFalse_DoesNotRetryOnNotFound()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        // Always return null from ApplyAndPersistAsync
        var mockContainer = CreateAlwaysNotFoundMockContainer();
        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(
            maxRetries: 3,
            delay: TimeSpan.FromMilliseconds(10),
            retryWhenNotFound: false  // Do NOT retry on NotFound
        );

        // Act
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        // Assert - ReadItemAsync called only once (no retries since RetryWhenNotFound is false)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - HandleUndeliverableAsync WAS called (not-found without retry still reports undeliverable)
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => s.Contains("NotFound")),
            It.Is<string>(s => s.Contains("RetryWhenNotFound is false")),
            It.IsAny<IEvent>(),
            It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion

    #region Multiple events with mixed outcomes

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_MultipleEvents_MixedRetryOutcomes()
    {
        // Arrange - two events: one will succeed immediately, one will need retries
        var aggId1 = Guid.NewGuid();
        var aggId2 = Guid.NewGuid();
        var pk = Guid.NewGuid();
        var evt1 = CreateUpdateEvent(aggId1, pk);
        var evt2 = CreateUpdateEvent(aggId2, pk);
        var events = new[] { CreateKafkaTriggerEventString(evt1), CreateKafkaTriggerEventString(evt2) };

        var mockContainer = new Mock<Container>();
        int callCountAgg2 = 0;

        var projection1 = new TestProjection { id = aggId1, name = "Original1" };
        var projection2 = new TestProjection { id = aggId2, name = "Original2" };

        var mockReadResponse1 = new Mock<ItemResponse<TestProjection>>();
        mockReadResponse1.Setup(r => r.Resource).Returns(projection1);
        var mockReadResponse2 = new Mock<ItemResponse<TestProjection>>();
        mockReadResponse2.Setup(r => r.Resource).Returns(projection2);

        // aggId1 succeeds immediately, aggId2 throws NotFound twice then succeeds
        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, partitionKey, opts, ct) =>
            {
                if (id == aggId1.ToString())
                {
                    return Task.FromResult(mockReadResponse1.Object);
                }
                else
                {
                    int current = Interlocked.Increment(ref callCountAgg2);
                    if (current <= 2)
                    {
                        throw new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0);
                    }
                    return Task.FromResult(mockReadResponse2.Object);
                }
            });

        var mockPatchResponse = new Mock<ItemResponse<TestProjection>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(new TestProjection { id = aggId1, name = "Updated" });

        mockContainer
            .Setup(c => c.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(
            maxRetries: 3,
            delay: TimeSpan.FromMilliseconds(10),
            retryWhenNotFound: true
        );

        // Act
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        // Assert - Both projections were initialized (both eventually succeeded)
        _mockNostify.Verify(n => n.InitAsync<TestProjection>(It.Is<List<TestProjection>>(l => l.Count == 2)), Times.Once);

        // Assert - No undeliverable events
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Never);
    }

    #endregion

    #region Default RetryOptions (no retries on NotFound by default)

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_DefaultRetryOptions_DoesNotRetryOnNotFound()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateAlwaysNotFoundMockContainer();
        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        // Act - use default retryOptions (null => defaults to RetryWhenNotFound=false)
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>());

        // Assert - ReadItemAsync called only once (defaults don't retry on NotFound)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - HandleUndeliverableAsync WAS called (default RetryWhenNotFound=false still reports undeliverable)
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => s.Contains("NotFound")),
            It.Is<string>(s => s.Contains("RetryWhenNotFound is false")),
            It.IsAny<IEvent>(),
            It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion

    #region Non-CosmosException does not trigger retry

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_NonCosmosException_DoesNotRetryAndHandlesUndeliverable()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = new Mock<Container>();
        // Throw a non-Cosmos exception; this propagates through ApplyAndPersistAsync
        // to the catch(Exception) in the retry loop
        mockContainer
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(
            maxRetries: 3,
            delay: TimeSpan.FromMilliseconds(10),
            retryWhenNotFound: true
        );

        // Act
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        // Assert - ReadItemAsync called only once (no retry for non-NotFound exceptions)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - HandleUndeliverableAsync WAS called (general exception path)
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => !s.Contains("Retry")),
            It.IsAny<string>(),
            It.IsAny<IEvent>(),
            It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion

    #region MaxRetries = 0 means no retries at all

    [Fact]
    public async Task HandleProjectionBulkUpdateEvent_MaxRetriesZero_NoRetriesPerformed()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateAlwaysNotFoundMockContainer();
        _mockNostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(
            maxRetries: 0,
            delay: TimeSpan.FromMilliseconds(10),
            retryWhenNotFound: true
        );

        // Act
        await DefaultEventHandlers.HandleProjectionBulkUpdateEvent<TestProjection>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        // Assert - ReadItemAsync called only once (initial attempt only, MaxRetries=0 means loop body runs once)
        mockContainer.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - HandleUndeliverableAsync WAS called (exhausted retries)
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => s.Contains("Retry")),
            It.Is<string>(s => s.Contains("Not found after 0 retries")),
            It.IsAny<IEvent>(),
            It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion

    #region Aggregate helpers

    /// <summary>
    /// Creates a mock Container where ReadItemAsync succeeds for TestAggregate.
    /// </summary>
    private static Mock<Container> CreateSucceedingAggregateMockContainer(Guid aggregateRootId)
    {
        var mockContainer = new Mock<Container>();
        var existing = new TestAggregate { id = aggregateRootId, name = "Original" };

        var mockReadResponse = new Mock<ItemResponse<TestAggregate>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existing);

        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockReadResponse.Object);

        var updated = new TestAggregate { id = aggregateRootId, name = "Updated" };
        var mockPatchResponse = new Mock<ItemResponse<TestAggregate>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(updated);

        mockContainer
            .Setup(c => c.PatchItemAsync<TestAggregate>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        return mockContainer;
    }

    /// <summary>
    /// Creates a mock Container where ReadItemAsync throws NotFound for the first N calls for TestAggregate,
    /// then succeeds on subsequent calls.
    /// </summary>
    private static Mock<Container> CreateNotFoundThenSucceedAggregateMockContainer(int notFoundCount, Guid aggregateRootId)
    {
        var mockContainer = new Mock<Container>();
        int callCount = 0;

        var existing = new TestAggregate { id = aggregateRootId, name = "Original" };
        var mockReadResponse = new Mock<ItemResponse<TestAggregate>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existing);

        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
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

        var updated = new TestAggregate { id = aggregateRootId, name = "Updated" };
        var mockPatchResponse = new Mock<ItemResponse<TestAggregate>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(updated);

        mockContainer
            .Setup(c => c.PatchItemAsync<TestAggregate>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        return mockContainer;
    }

    /// <summary>
    /// Creates a mock Container where ReadItemAsync always throws NotFound for TestAggregate.
    /// </summary>
    private static Mock<Container> CreateAlwaysNotFoundAggregateMockContainer()
    {
        var mockContainer = new Mock<Container>();

        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0));

        return mockContainer;
    }

    #endregion

    #region Aggregate: Success on first attempt

    [Fact]
    public async Task HandleAggregateBulkUpdateEvent_SuccessOnFirstAttempt()
    {
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateSucceedingAggregateMockContainer(aggId);
        _mockNostify
            .Setup(n => n.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<TestAggregate>(
            _mockNostify.Object, events, new List<string>());

        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Never);
    }

    #endregion

    #region Aggregate: Retry succeeds after 1 retry

    [Fact]
    public async Task HandleAggregateBulkUpdateEvent_RetryWhenNotFound_SucceedsAfterOneRetry()
    {
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateNotFoundThenSucceedAggregateMockContainer(1, aggId);
        _mockNostify
            .Setup(n => n.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<TestAggregate>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Never);
    }

    #endregion

    #region Aggregate: Retry succeeds after 3 retries

    [Fact]
    public async Task HandleAggregateBulkUpdateEvent_RetryWhenNotFound_SucceedsAfterThreeRetries()
    {
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateNotFoundThenSucceedAggregateMockContainer(3, aggId);
        _mockNostify
            .Setup(n => n.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<TestAggregate>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(4));

        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Never);
    }

    #endregion

    #region Aggregate: Exhausts retries → undeliverable

    [Fact]
    public async Task HandleAggregateBulkUpdateEvent_ExhaustsRetriesAndHandlesUndeliverable()
    {
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateAlwaysNotFoundAggregateMockContainer();
        _mockNostify
            .Setup(n => n.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<TestAggregate>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(4));

        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => s.Contains("Retry")),
            It.Is<string>(s => s.Contains("Not found after 3 retries")),
            It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion

    #region Aggregate: RetryWhenNotFound=false → undeliverable immediately

    [Fact]
    public async Task HandleAggregateBulkUpdateEvent_RetryWhenNotFoundFalse_HandlesUndeliverable()
    {
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateAlwaysNotFoundAggregateMockContainer();
        _mockNostify
            .Setup(n => n.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: false);

        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<TestAggregate>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => s.Contains("NotFound")),
            It.Is<string>(s => s.Contains("RetryWhenNotFound is false")),
            It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion

    #region Aggregate: Default RetryOptions → undeliverable on not found

    [Fact]
    public async Task HandleAggregateBulkUpdateEvent_DefaultRetryOptions_HandlesUndeliverable()
    {
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateAlwaysNotFoundAggregateMockContainer();
        _mockNostify
            .Setup(n => n.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<TestAggregate>(
            _mockNostify.Object, events, new List<string>());

        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => s.Contains("NotFound")),
            It.Is<string>(s => s.Contains("RetryWhenNotFound is false")),
            It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion

    #region Aggregate: Non-Cosmos exception → undeliverable immediately

    [Fact]
    public async Task HandleAggregateBulkUpdateEvent_NonCosmosException_HandlesUndeliverable()
    {
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.ReadItemAsync<TestAggregate>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        _mockNostify
            .Setup(n => n.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<TestAggregate>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => !s.Contains("Retry") && !s.Contains("NotFound")),
            It.IsAny<string>(),
            It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion

    #region Aggregate: MaxRetries=0 → single attempt, then undeliverable

    [Fact]
    public async Task HandleAggregateBulkUpdateEvent_MaxRetriesZero_NoRetriesPerformed()
    {
        var aggId = Guid.NewGuid();
        var evt = CreateUpdateEvent(aggId);
        var events = new[] { CreateKafkaTriggerEventString(evt) };

        var mockContainer = CreateAlwaysNotFoundAggregateMockContainer();
        _mockNostify
            .Setup(n => n.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockContainer.Object);

        var retryOptions = new RetryOptions(maxRetries: 0, delay: TimeSpan.FromMilliseconds(10), retryWhenNotFound: true);

        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<TestAggregate>(
            _mockNostify.Object, events, new List<string>(), retryOptions);

        mockContainer.Verify(c => c.ReadItemAsync<TestAggregate>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.Is<string>(s => s.Contains("Retry")),
            It.Is<string>(s => s.Contains("Not found after 0 retries")),
            It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Once);
    }

    #endregion
}
