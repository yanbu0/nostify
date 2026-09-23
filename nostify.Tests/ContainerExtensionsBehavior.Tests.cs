using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;

namespace nostify.Tests;

/// <summary>
/// Deterministic behavioral tests for Cosmos container extension boundaries.
/// </summary>
public class ContainerExtensionsBehaviorTests
{
    [Fact]
    public void ValidateBulkEnabled_WhenDisabled_ReturnsFalse()
    {
        var container = CreateContainerWithBulkExecution(enabled: false);

        bool enabled = container.Object.ValidateBulkEnabled();

        Assert.False(enabled);
    }

    [Fact]
    public void ValidateBulkEnabled_WhenDisabledAndRequired_ThrowsDescriptiveError()
    {
        var container = CreateContainerWithBulkExecution(enabled: false);

        var exception = Assert.Throws<NostifyException>(
            () => container.Object.ValidateBulkEnabled(throwIfNotEnabled: true));

        Assert.Equal("Bulk operations must be enabled for this container", exception.Message);
    }

    [Fact]
    public async Task BulkDeleteFromEventsAsync_WithNoUsableEvents_ReturnsWithoutCallingCosmos()
    {
        var container = new Mock<Container>(MockBehavior.Strict);

        int deleted = await container.Object.BulkDeleteFromEventsAsync<TestProjection>(["null"]);

        Assert.Equal(0, deleted);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenTtlIsDisabled_EnablesTtlBeforeReturning()
    {
        var container = CreateBulkDeleteContainer(
            new ContainerProperties("projections", "/tenantId"));

        int deleted = await container.Object.BulkDeleteAsync(new List<TestProjection>());

        Assert.Equal(0, deleted);
        container.Verify(
            value => value.ReplaceContainerAsync(
                It.Is<ContainerProperties>(properties => properties.DefaultTimeToLive == 1),
                It.IsAny<ContainerRequestOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenPartitionPropertyIsMissing_ThrowsDescriptiveError()
    {
        var properties = new ContainerProperties("projections", "/missingPartition")
        {
            DefaultTimeToLive = 1
        };
        var container = CreateBulkDeleteContainer(properties);
        var projection = new TestProjection { id = Guid.NewGuid(), tenantId = Guid.NewGuid() };

        var exception = await Assert.ThrowsAsync<NostifyException>(
            () => container.Object.BulkDeleteAsync([projection]));

        Assert.Contains(
            "Property 'missingPartition' does not exist on type 'TestProjection'.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenPartitionValueIsEmpty_ThrowsDescriptiveError()
    {
        var properties = new ContainerProperties("projections", "/partition")
        {
            DefaultTimeToLive = 1
        };
        var container = CreateBulkDeleteContainer(properties);
        var projection = new DeleteCandidate { id = Guid.NewGuid(), partition = string.Empty };

        var exception = await Assert.ThrowsAsync<NostifyException>(
            () => container.Object.BulkDeleteAsync([projection]));

        Assert.Contains(
            "Partition key value is null or empty for property 'partition' on item of type 'DeleteCandidate'.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SafePatchItemAsync_WithOnlyIdOperation_ReturnsSuccessWithoutCallingCosmos()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        var partitionKey = new PartitionKey("tenant");

        PatchItemResult result = await container.Object.SafePatchItemAsync<TestProjection>(
            "projection-id",
            partitionKey,
            [PatchOperation.Set("/ID", "replacement-id")]);

        Assert.True(result.PatchedSuccessfully);
        Assert.Equal("projection-id", result.id);
        container.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SafePatchItemAsync_WithIdAndMutableOperation_FiltersIdBeforePatching()
    {
        var container = new Mock<Container>();
        IReadOnlyList<PatchOperation>? capturedOperations = null;
        container
            .Setup(value => value.PatchItemAsync<TestProjection>(
                "projection-id",
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PartitionKey, IReadOnlyList<PatchOperation>, PatchItemRequestOptions, CancellationToken>(
                (_, _, operations, _, _) => capturedOperations = operations)
            .ReturnsAsync(Mock.Of<ItemResponse<TestProjection>>());

        PatchItemResult result = await container.Object.SafePatchItemAsync<TestProjection>(
            "projection-id",
            new PartitionKey("tenant"),
            [PatchOperation.Set("/id", "replacement-id"), PatchOperation.Set("/name", "updated")]);

        Assert.True(result.PatchedSuccessfully);
        PatchOperation operation = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PatchOperation>>(capturedOperations));
        Assert.Equal("/name", operation.Path);
    }

    [Fact]
    public async Task SafePatchItemAsync_WhenCosmosReturnsNotFound_ReturnsNotFoundResult()
    {
        var container = new Mock<Container>();
        container
            .Setup(value => value.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("missing", HttpStatusCode.NotFound, 0, "activity", 1));

        PatchItemResult result = await container.Object.SafePatchItemAsync<TestProjection>(
            "projection-id",
            new PartitionKey("tenant"),
            [PatchOperation.Set("/name", "updated")]);

        Assert.True(result.NotFound);
        Assert.False(result.IsException);
    }

    [Fact]
    public async Task SafePatchItemAsync_WhenGeneralExceptionOccurs_ReturnsExceptionResult()
    {
        var container = new Mock<Container>();
        container
            .Setup(value => value.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("patch failed"));

        PatchItemResult result = await container.Object.SafePatchItemAsync<TestProjection>(
            "projection-id",
            new PartitionKey("tenant"),
            [PatchOperation.Set("/name", "updated")]);

        Assert.True(result.IsException);
        Assert.Equal(HttpStatusCode.InternalServerError, result.statusCode);
        Assert.Equal("patch failed", result.exceptionMessage);
    }

    [Fact]
    public async Task DeleteItemAsync_WithGuids_DelegatesExactIdAndPartitionKey()
    {
        var aggregateId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var expectedPartitionKey = new PartitionKey(tenantId.ToString());
        var container = new Mock<Container>();
        container
            .Setup(value => value.DeleteItemAsync<TestProjection>(
                aggregateId.ToString(),
                It.Is<PartitionKey>(key => key.ToString() == expectedPartitionKey.ToString()),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<TestProjection>>());

        ItemResponse<TestProjection> response =
            await container.Object.DeleteItemAsync<TestProjection>(aggregateId, tenantId);

        Assert.NotNull(response);
        container.VerifyAll();
    }

    [Fact]
    public async Task DoBulkUpsertAsync_UpsertsEveryItem()
    {
        var container = CreateContainerWithBulkExecution(enabled: true);
        container
            .Setup(value => value.UpsertItemAsync(
                It.IsAny<TestAggregate>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<TestAggregate>>());
        var items = new List<TestAggregate>
        {
            new() { id = Guid.NewGuid() },
            new() { id = Guid.NewGuid() }
        };

        await container.Object.DoBulkUpsertAsync(items);

        container.Verify(
            value => value.UpsertItemAsync(
                It.IsAny<TestAggregate>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(items.Count));
    }

    [Fact]
    public async Task DoBulkCreateAsync_WhenOneItemConflicts_TreatsConflictAsIdempotentSuccess()
    {
        var container = CreateContainerWithBulkExecution(enabled: true);
        int calls = 0;
        container
            .Setup(value => value.CreateItemAsync(
                It.IsAny<TestAggregate>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<TestAggregate, PartitionKey?, ItemRequestOptions, CancellationToken>((_, _, _, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new CosmosException("duplicate", HttpStatusCode.Conflict, 0, "activity", 1);
                }

                return Task.FromResult(Mock.Of<ItemResponse<TestAggregate>>());
            });
        var items = new List<TestAggregate>
        {
            new() { id = Guid.NewGuid() },
            new() { id = Guid.NewGuid() }
        };

        await container.Object.DoBulkCreateAsync(items);

        Assert.Equal(items.Count, calls);
    }

    /// <summary>
    /// Creates a mock container whose client exposes the requested bulk-execution setting.
    /// </summary>
    private static Mock<Container> CreateContainerWithBulkExecution(bool enabled)
    {
        var client = new Mock<CosmosClient>();
        client.Setup(value => value.ClientOptions)
            .Returns(new CosmosClientOptions { AllowBulkExecution = enabled });
        var database = new Mock<Database>();
        database.Setup(value => value.Client).Returns(client.Object);
        var container = new Mock<Container>();
        container.Setup(value => value.Database).Returns(database.Object);
        return container;
    }

    /// <summary>
    /// Creates a bulk-enabled container with deterministic container metadata.
    /// </summary>
    private static Mock<Container> CreateBulkDeleteContainer(ContainerProperties properties)
    {
        var container = CreateContainerWithBulkExecution(enabled: true);
        var response = new Mock<ContainerResponse>();
        response.Setup(value => value.Resource).Returns(properties);
        container
            .Setup(value => value.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        container
            .Setup(value => value.ReplaceContainerAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<ContainerRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        return container;
    }

    /// <summary>
    /// Projection-like item with a nullable string partition key for validation testing.
    /// </summary>
    private sealed class DeleteCandidate : NostifyObject
    {
        public string? partition { get; set; }

        protected override void Apply(EventType eventType, IEvent e)
        {
            // No event application is needed by partition-key validation tests.
        }
    }
}
