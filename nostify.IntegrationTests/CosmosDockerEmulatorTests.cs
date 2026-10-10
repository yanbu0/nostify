using Microsoft.Azure.Cosmos;
namespace nostify.IntegrationTests;

/// <summary>
/// Verifies Cosmos SDK behavior against an emulator container created by the test process.
/// </summary>
[Collection(CosmosDockerIntegrationCollection.Name)]
[Trait(IntegrationTraits.Category, IntegrationTraits.Integration)]
[Trait(IntegrationTraits.Dependency, IntegrationTraits.CosmosDocker)]
public sealed class CosmosDockerEmulatorTests
{
    private readonly CosmosDockerIntegrationFixture _fixture;

    /// <summary>Initializes a test instance with the Docker-hosted Cosmos database.</summary>
    public CosmosDockerEmulatorTests(CosmosDockerIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies point operations, partition isolation, and JSON round-tripping against the emulator.
    /// </summary>
    [Fact]
    public async Task PointOperations_RoundTripItemsWithinTheirPartitions()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Container container = await _fixture.Database.CreateContainerAsync(
            $"points-{Guid.NewGuid():N}",
            "/tenantId",
            cancellationToken: timeout.Token);
        Guid sharedId = Guid.NewGuid();
        var first = new DockerDocument(sharedId, "tenant-a", "first", 1);
        var second = new DockerDocument(sharedId, "tenant-b", "second", 2);

        await container.CreateItemAsync(first, new PartitionKey(first.tenantId), cancellationToken: timeout.Token);
        await container.CreateItemAsync(second, new PartitionKey(second.tenantId), cancellationToken: timeout.Token);

        DockerDocument firstResult = (await container.ReadItemAsync<DockerDocument>(
            sharedId.ToString(),
            new PartitionKey(first.tenantId),
            cancellationToken: timeout.Token)).Resource;
        DockerDocument secondResult = (await container.ReadItemAsync<DockerDocument>(
            sharedId.ToString(),
            new PartitionKey(second.tenantId),
            cancellationToken: timeout.Token)).Resource;

        Assert.Equal(first, firstResult);
        Assert.Equal(second, secondResult);
    }

    /// <summary>
    /// Verifies that a same-partition transactional batch persists all operations atomically.
    /// </summary>
    [Fact]
    public async Task TransactionalBatch_CreatesAllItemsWithinOnePartition()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Container container = await _fixture.Database.CreateContainerAsync(
            $"batch-{Guid.NewGuid():N}",
            "/tenantId",
            cancellationToken: timeout.Token);
        const string tenantId = "tenant-a";
        var first = new DockerDocument(Guid.NewGuid(), tenantId, "first", 1);
        var second = new DockerDocument(Guid.NewGuid(), tenantId, "second", 2);

        using TransactionalBatchResponse response = await container
            .CreateTransactionalBatch(new PartitionKey(tenantId))
            .CreateItem(first)
            .CreateItem(second)
            .ExecuteAsync(timeout.Token);

        Assert.True(response.IsSuccessStatusCode, response.ErrorMessage);
        Assert.Equal(2, response.Count);
        DockerDocument firstResult = (await container.ReadItemAsync<DockerDocument>(
            first.id.ToString(),
            new PartitionKey(tenantId),
            cancellationToken: timeout.Token)).Resource;
        DockerDocument secondResult = (await container.ReadItemAsync<DockerDocument>(
            second.id.ToString(),
            new PartitionKey(tenantId),
            cancellationToken: timeout.Token)).Resource;
        Assert.Equal(first, firstResult);
        Assert.Equal(second, secondResult);
    }

    /// <summary>Verifies the production create-then-patch ApplyAndPersist path against Cosmos.</summary>
    [Fact(Timeout = 120_000)]
    public async Task ApplyAndPersist_CreatesThenPatchesProjection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Container container = await _fixture.Database.CreateContainerAsync(
            $"apply-{Guid.NewGuid():N}",
            "/tenantId",
            cancellationToken: timeout.Token);
        Guid tenantId = Guid.NewGuid();
        Guid projectionId = Guid.NewGuid();
        var create = new Event(
            new ProjectionCreated(),
            projectionId,
            new { id = projectionId, tenantId, name = "created", rank = 1 },
            partitionKey: tenantId);
        var update = new Event(
            new ProjectionUpdated(),
            projectionId,
            new { name = "patched", rank = 2 },
            partitionKey: tenantId);

        QueryDocument? created = await container.ApplyAndPersistAsync<QueryDocument>(
            create,
            new PartitionKey(tenantId.ToString()),
            projectionBaseAggregateId: null);
        QueryDocument? patched = await container.ApplyAndPersistAsync<QueryDocument>(
            update,
            new PartitionKey(tenantId.ToString()),
            projectionBaseAggregateId: null);
        QueryDocument persisted = (await container.ReadItemAsync<QueryDocument>(
            projectionId.ToString(),
            new PartitionKey(tenantId.ToString()),
            cancellationToken: timeout.Token)).Resource;

        Assert.NotNull(created);
        Assert.NotNull(patched);
        Assert.Equal("created", created.name);
        Assert.Equal("patched", patched.name);
        Assert.Equal("patched", persisted.name);
        Assert.Equal(2, persisted.rank);
    }

    /// <summary>Verifies the configured Newtonsoft serializer through a real Cosmos write/read.</summary>
    [Fact(Timeout = 120_000)]
    public async Task NewtonsoftSerializer_RoundTripsTypedEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Container container = await _fixture.Database.CreateContainerAsync(
            $"serializer-{Guid.NewGuid():N}",
            "/aggregateRootId",
            cancellationToken: timeout.Token);
        Guid aggregateRootId = Guid.NewGuid();
        var expected = new Event(
            new ProjectionCreated(),
            aggregateRootId,
            new { name = "serialized", rank = 7 });

        await container.CreateItemAsync(
            expected,
            new PartitionKey(aggregateRootId.ToString()),
            cancellationToken: timeout.Token);
        Event actual = (await container.ReadItemAsync<Event>(
            expected.id.ToString(),
            new PartitionKey(aggregateRootId.ToString()),
            cancellationToken: timeout.Token)).Resource;

        Assert.Equal(expected.id, actual.id);
        Assert.Equal(aggregateRootId, actual.aggregateRootId);
        Assert.IsType<ProjectionCreated>(actual.eventType);
        Assert.Equal(2, actual.schemaVersion);
        Assert.Equal("serialized", actual.GetPayload<SerializerPayload>()?.name);
    }

    private sealed record DockerDocument(Guid id, string tenantId, string name, int rank);

    private sealed class QueryDocument : NostifyObject
    {
        public string name { get; set; } = string.Empty;

        public int rank { get; set; }

        protected override void Apply(EventType eventType, IEvent eventToApply)
        {
            UpdateProperties<QueryDocument>(eventToApply.payload);
        }
    }

    private sealed class ProjectionCreated : EventType
    {
        public ProjectionCreated() : base("Docker_Projection_Created", isNew: true)
        {
        }
    }

    private sealed class ProjectionUpdated : EventType
    {
        public ProjectionUpdated() : base("Docker_Projection_Updated")
        {
        }
    }

    private sealed class SerializerPayload
    {
        public string name { get; set; } = string.Empty;

        public int rank { get; set; }
    }
}
