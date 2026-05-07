using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace nostify.Tests;

public class BulkDeleteRetryTests
{
    [Fact]
    public async Task BulkDeleteAsync_WithRetryOptions_RetriesOn429AndSucceeds()
    {
        var mockContainer = CreateBulkEnabledDeleteContainer();
        int patchCalls = 0;

        mockContainer
            .Setup(c => c.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, IReadOnlyList<PatchOperation>, PatchItemRequestOptions, CancellationToken>((id, pk, ops, opts, ct) =>
            {
                patchCalls++;
                if (patchCalls == 1)
                {
                    throw new CosmosException("throttled", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);
                }

                return Task.FromResult(Mock.Of<ItemResponse<TestProjection>>());
            });

        var projections = new List<TestProjection>
        {
            new() { id = Guid.NewGuid(), tenantId = Guid.NewGuid(), name = "A" }
        };
        var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1), retryWhenNotFound: false);

        int deleted = await mockContainer.Object.BulkDeleteAsync(projections, retryOptions);

        Assert.Equal(1, deleted);
        Assert.Equal(2, patchCalls);
    }

    [Fact]
    public async Task BulkDeleteAsync_WithoutRetryOptions_DoesNotRetryOn429()
    {
        var mockContainer = CreateBulkEnabledDeleteContainer();
        int patchCalls = 0;

        mockContainer
            .Setup(c => c.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, IReadOnlyList<PatchOperation>, PatchItemRequestOptions, CancellationToken>((id, pk, ops, opts, ct) =>
            {
                patchCalls++;
                throw new CosmosException("throttled", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);
            });

        var projections = new List<TestProjection>
        {
            new() { id = Guid.NewGuid(), tenantId = Guid.NewGuid(), name = "A" }
        };

        await Assert.ThrowsAsync<NostifyException>(() => mockContainer.Object.BulkDeleteAsync(projections));
        Assert.Equal(1, patchCalls);
    }

    private static Mock<Container> CreateBulkEnabledDeleteContainer()
    {
        var clientOptions = new CosmosClientOptions { AllowBulkExecution = true };
        var mockClient = new Mock<CosmosClient>();
        mockClient.Setup(c => c.ClientOptions).Returns(clientOptions);

        var mockDatabase = new Mock<Database>();
        mockDatabase.Setup(d => d.Client).Returns(mockClient.Object);

        var mockContainer = new Mock<Container>();
        mockContainer.Setup(c => c.Database).Returns(mockDatabase.Object);

        var containerProps = new ContainerProperties("test-container", "/tenantId")
        {
            DefaultTimeToLive = 1
        };
        var mockContainerResponse = new Mock<ContainerResponse>();
        mockContainerResponse.Setup(r => r.Resource).Returns(containerProps);
        mockContainer
            .Setup(c => c.ReadContainerAsync(It.IsAny<ContainerRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContainerResponse.Object);

        return mockContainer;
    }
}
