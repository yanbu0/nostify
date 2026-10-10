using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json.Linq;
using nostify.IntegrationTestModels;

namespace nostify.IntegrationTests;

/// <summary>
/// Exercises public Nostify workflows against the Docker-hosted Cosmos emulator.
/// </summary>
[Collection(CosmosDockerIntegrationCollection.Name)]
[Trait(IntegrationTraits.Category, IntegrationTraits.Integration)]
[Trait(IntegrationTraits.Dependency, IntegrationTraits.CosmosDocker)]
public sealed class NostifyCosmosWorkflowTests
{
    private readonly CosmosDockerIntegrationFixture _fixture;

    /// <summary>Initializes a test instance with the Docker-hosted Cosmos database.</summary>
    public NostifyCosmosWorkflowTests(CosmosDockerIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies that the generic factory build provisions every framework and model container with
    /// the partition-key and TTL contracts used by Nostify, and remains idempotent on repeated startup.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Build_WithAutoCreation_ProvisionsAllContainersWithExpectedContractsIdempotently()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        NostifyConfig CreateConfig() => NostifyFactory.WithCosmos(
            _fixture.AccountKey,
            _fixture.Database.Id,
            _fixture.Endpoint,
            createContainers: true,
            containerThroughput: -1,
            useGatewayConnection: true,
            defaultRetryOptions: null,
            cosmosHttpClientFactory: _fixture.CreateHttpClient);

        // Build returns the public interface, while the production implementation owns disposable clients.
        using IDisposable first = (IDisposable)CreateConfig().Build<KafkaDiscoveryAggregate>();
        using IDisposable second = (IDisposable)CreateConfig().Build<KafkaDiscoveryAggregate>();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["eventStore"] = "/aggregateRootId",
            ["sequenceContainer"] = "/partitionKey",
            ["undeliverableEvents"] = "/aggregateRootId",
            [KafkaDiscoveryAggregate.currentStateContainerName] = "/tenantId",
            [CosmosProvisioningProjection.containerName] = "/tenantId"
        };

        using FeedIterator<ContainerProperties> iterator = _fixture.Database.GetContainerQueryIterator<ContainerProperties>();
        var actual = new Dictionary<string, ContainerProperties>(StringComparer.Ordinal);
        while (iterator.HasMoreResults)
        {
            foreach (ContainerProperties properties in await iterator.ReadNextAsync(timeout.Token))
            {
                actual[properties.Id] = properties;
            }
        }

        // The collection fixture is shared with the retained raw-SDK scenarios, so unrelated
        // run-unique containers may already exist. Validate the complete Nostify-owned contract set.
        Assert.All(expected.Keys, name => Assert.Contains(name, actual.Keys));
        foreach ((string name, string partitionKeyPath) in expected)
        {
            Assert.Equal(partitionKeyPath, actual[name].PartitionKeyPath);
            Assert.Equal(-1, actual[name].DefaultTimeToLive);
        }
    }

    /// <summary>
    /// Verifies selected-property rolling initialization against persisted event and projection
    /// documents. The old documents intentionally omit the newly introduced property.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task RollingInitialization_BackfillsNewPropertyOnEveryPreExistingProjection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        Container eventStore = await _fixture.Database.CreateContainerAsync(
            $"rolling-events-{Guid.NewGuid():N}",
            "/aggregateRootId",
            cancellationToken: timeout.Token);
        Container projections = await _fixture.Database.CreateContainerAsync(
            $"rolling-projections-{Guid.NewGuid():N}",
            "/tenantId",
            cancellationToken: timeout.Token);
        Guid tenantId = Guid.NewGuid();
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];

        foreach ((Guid id, int index) in ids.Select((value, index) => (value, index)))
        {
            // This is the persisted shape from before backfilledLabel existed on the projection model.
            var oldProjection = new JObject
            {
                ["id"] = id,
                ["tenantId"] = tenantId,
                ["ttl"] = -1,
                ["initialized"] = false,
                ["preservedValue"] = $"live-{index}"
            };
            await projections.CreateItemAsync(
                oldProjection,
                new PartitionKey(tenantId.ToString()),
                cancellationToken: timeout.Token);

            var source = new Event(
                RollingBackfillEventType.Instance,
                id,
                new
                {
                    id,
                    tenantId,
                    backfilledLabel = $"backfilled-{index}",
                    preservedValue = $"event-{index}"
                },
                partitionKey: id);
            await eventStore.CreateItemAsync(
                source,
                new PartitionKey(id.ToString()),
                cancellationToken: timeout.Token);
        }

        var nostify = new Mock<INostify>();
        nostify.Setup(value => value.GetEventStoreContainerAsync(It.IsAny<bool>())).ReturnsAsync(eventStore);
        nostify.Setup(value => value.GetProjectionContainerAsync<RollingBackfillProjection>(It.IsAny<string>()))
            .ReturnsAsync(projections);
        var initializer = new DurableProjectionInitializer<RollingBackfillProjection, RollingBackfillAggregate>(
            new HttpClient(),
            nostify.Object,
            $"rolling-backfill-{Guid.NewGuid():N}",
            batchSize: 2,
            concurrentBatchCount: 1,
            cosmosRetryOptions: new RetryOptions(1, TimeSpan.FromMilliseconds(10), false));
        var batch = new DurableRollingProjectionBatch(
            ids.Select(id => new DurableRollingProjectionWorkItem(id, tenantId.ToString(), true)).ToArray(),
            new DurableRollingProjectionOptions(
                [nameof(RollingBackfillProjection.backfilledLabel)],
                maxEtagRetries: 1,
                initialBackoff: TimeSpan.FromMilliseconds(10),
                backoffCoefficient: 2,
                partitionKeyPath: "/tenantId"));

        await initializer.ProcessRollingBatch(batch);

        for (int index = 0; index < ids.Length; index++)
        {
            RollingBackfillProjection persisted = (await projections.ReadItemAsync<RollingBackfillProjection>(
                ids[index].ToString(),
                new PartitionKey(tenantId.ToString()),
                cancellationToken: timeout.Token)).Resource;
            Assert.Equal($"backfilled-{index}", persisted.backfilledLabel);
            Assert.Equal($"live-{index}", persisted.preservedValue);
            Assert.False(persisted.initialized);
            Assert.Equal(tenantId, persisted.tenantId);
        }
    }

    private sealed class RollingBackfillAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => nameof(RollingBackfillAggregate);

        public static string currentStateContainerName => "RollingBackfillCurrentState";

        public bool isDeleted { get; set; }
    }

    private sealed class RollingBackfillProjection : NostifyObject, IProjection, IHasExternalData<RollingBackfillProjection>
    {
        public static string containerName => "RollingBackfillProjection";

        public bool initialized { get; set; }

        public string backfilledLabel { get; set; } = string.Empty;

        public string preservedValue { get; set; } = string.Empty;

        protected override void Apply(EventType eventType, IEvent eventToApply)
        {
            UpdateProperties<RollingBackfillProjection>(eventToApply.payload);
        }

        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
            List<RollingBackfillProjection> projectionsToInit,
            INostify nostify,
            HttpClient? httpClient = null,
            DateTime? pointInTime = null)
            => Task.FromResult(new List<ExternalDataEvent>());
    }

    private sealed class RollingBackfillEventType : EventType
    {
        public static RollingBackfillEventType Instance { get; } = new();

        public RollingBackfillEventType()
            : base("Docker_Rolling_Backfill", false, false)
        {
        }
    }
}
