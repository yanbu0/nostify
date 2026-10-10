using Microsoft.Azure.Cosmos;
using Moq;

namespace nostify.IntegrationTests;

/// <summary>
/// Verifies durable projection cursor queries against the real Cosmos LINQ provider.
/// </summary>
[Collection(CosmosIntegrationCollection.Name)]
[Trait(IntegrationTraits.Category, IntegrationTraits.Integration)]
[Trait(IntegrationTraits.Dependency, IntegrationTraits.Cosmos)]
public sealed class DurableProjectionInitializerCosmosTests
{
    private readonly CosmosIntegrationFixture _fixture;

    /// <summary>Initializes a test instance with the shared isolated Cosmos database.</summary>
    public DurableProjectionInitializerCosmosTests(CosmosIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Proves that the cursor predicate and ordering execute with matching string semantics while
    /// records are deleted and inserted between pages.
    /// </summary>
    [Fact]
    public async Task GetIdsForPartition_UsesStableExclusiveCursorThroughCosmosProvider()
    {
        Guid tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid otherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Guid firstId = Guid.Parse("10000000-0000-0000-0000-000000000000");
        Guid firstCursor = Guid.Parse("20000000-0000-0000-0000-000000000000");
        Guid softDeletedId = Guid.Parse("25000000-0000-0000-0000-000000000000");
        Guid otherTenantRecordId = Guid.Parse("27500000-0000-0000-0000-000000000000");
        Guid thirdId = Guid.Parse("30000000-0000-0000-0000-000000000000");
        Guid insertedAheadId = Guid.Parse("35000000-0000-0000-0000-000000000000");
        Guid fourthId = Guid.Parse("40000000-0000-0000-0000-000000000000");
        Guid insertedBehindId = Guid.Parse("15000000-0000-0000-0000-000000000000");

        Container container = await _fixture.Database.CreateContainerAsync(
            $"cursor-{Guid.NewGuid():N}",
            "/tenantId");

        foreach (Guid id in new[] { firstId, firstCursor, thirdId, fourthId })
        {
            await CreateAggregateAsync(container, tenantId, id);
        }

        await CreateAggregateAsync(container, tenantId, softDeletedId, isDeleted: true);
        await CreateAggregateAsync(container, otherTenantId, otherTenantRecordId);

        var nostify = new Mock<INostify>();
        nostify.Setup(value => value.GetCurrentStateContainerAsync<CursorAggregate>(It.IsAny<string>()))
            .ReturnsAsync(container);
        var initializer = new DurableProjectionInitializer<CursorProjection, CursorAggregate>(
            new HttpClient(),
            nostify.Object,
            "cosmos-cursor-integration",
            batchSize: 2,
            concurrentBatchCount: 1,
            cosmosRetryOptions: new RetryOptions(0, TimeSpan.Zero, false));
        var partitionKey = new PartitionKey(tenantId.ToString());

        List<Guid> firstPage = await initializer.GetIdsForPartition(partitionKey);
        Assert.Equal(new[] { firstId, firstCursor }, firstPage);

        // Mutate both sides of the cursor. The earlier insertion must not be revisited, while the
        // later insertion must still be discovered despite deleting a record from the first page.
        await container.DeleteItemAsync<CursorAggregate>(firstId.ToString(), partitionKey);
        await CreateAggregateAsync(container, tenantId, insertedBehindId);
        await CreateAggregateAsync(container, tenantId, insertedAheadId);

        List<Guid> secondPage = await initializer.GetIdsForPartition(partitionKey, firstPage[^1]);
        List<Guid> thirdPage = await initializer.GetIdsForPartition(partitionKey, secondPage[^1]);
        List<Guid> finalPage = await initializer.GetIdsForPartition(partitionKey, thirdPage[^1]);

        Assert.Equal(new[] { thirdId, insertedAheadId }, secondPage);
        Assert.Equal(new[] { fourthId }, thirdPage);
        Assert.Empty(finalPage);

        List<Guid> processed = [.. firstPage, .. secondPage, .. thirdPage];
        Assert.Equal(processed.Count, processed.Distinct().Count());
        Assert.DoesNotContain(insertedBehindId, processed);
        Assert.DoesNotContain(softDeletedId, processed);
        Assert.DoesNotContain(otherTenantRecordId, processed);
    }

    /// <summary>
    /// Verifies distinct aggregate-root cursor paging through the same Cosmos provider used by
    /// aggregate current-state rebuild activities.
    /// </summary>
    [Fact]
    public async Task CurrentStateGetAggregateIds_UsesStableExclusiveCursorThroughCosmosProvider()
    {
        Guid firstId = Guid.Parse("10000000-0000-0000-0000-000000000000");
        Guid firstCursor = Guid.Parse("20000000-0000-0000-0000-000000000000");
        Guid thirdId = Guid.Parse("30000000-0000-0000-0000-000000000000");
        Guid insertedAheadId = Guid.Parse("35000000-0000-0000-0000-000000000000");
        Guid fourthId = Guid.Parse("40000000-0000-0000-0000-000000000000");
        Guid insertedBehindId = Guid.Parse("15000000-0000-0000-0000-000000000000");

        Container eventStore = await _fixture.Database.CreateContainerAsync(
            $"events-{Guid.NewGuid():N}",
            "/partitionKey");
        foreach (Guid aggregateId in new[] { firstId, firstCursor, thirdId, fourthId })
        {
            await CreateEventAsync(eventStore, aggregateId);
        }

        // A duplicate event for one aggregate proves Distinct is applied before paging.
        await CreateEventAsync(eventStore, firstId);

        var nostify = new Mock<INostify>();
        nostify.Setup(value => value.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(eventStore);
        var initializer = new DurableCurrentStateInitializer<CursorAggregate>(
            nostify.Object,
            "current-state-cosmos-cursor-integration",
            batchSize: 2,
            concurrentBatchCount: 1,
            cosmosRetryOptions: new RetryOptions(0, TimeSpan.Zero, false));

        List<Guid> firstPage = await initializer.GetAggregateIds(new DurableCurrentStatePageInfo());
        Assert.Equal(new[] { firstId, firstCursor }, firstPage);

        // Delete an event from the consumed range and insert aggregate IDs on both sides of the cursor.
        List<Event> firstAggregateEvents = eventStore.GetItemLinqQueryable<Event>(allowSynchronousQueryExecution: true)
            .Where(value => value.aggregateRootId == firstId)
            .ToList();
        foreach (Event @event in firstAggregateEvents)
        {
            await eventStore.DeleteItemAsync<Event>(
                @event.id.ToString(),
                new PartitionKey(@event.partitionKey.ToString()));
        }

        await CreateEventAsync(eventStore, insertedBehindId);
        await CreateEventAsync(eventStore, insertedAheadId);

        List<Guid> secondPage = await initializer.GetAggregateIds(new DurableCurrentStatePageInfo(firstPage[^1]));
        List<Guid> thirdPage = await initializer.GetAggregateIds(new DurableCurrentStatePageInfo(secondPage[^1]));
        List<Guid> finalPage = await initializer.GetAggregateIds(new DurableCurrentStatePageInfo(thirdPage[^1]));

        Assert.Equal(new[] { thirdId, insertedAheadId }, secondPage);
        Assert.Equal(new[] { fourthId }, thirdPage);
        Assert.Empty(finalPage);
        Assert.DoesNotContain(insertedBehindId, firstPage.Concat(secondPage).Concat(thirdPage));
    }

    private static Task<ItemResponse<Event>> CreateEventAsync(Container container, Guid aggregateId)
    {
        var @event = new Event(
            CursorEventType.Instance,
            aggregateId,
            new { id = aggregateId },
            partitionKey: aggregateId);
        return container.CreateItemAsync(@event, new PartitionKey(aggregateId.ToString()));
    }

    private static Task<ItemResponse<CursorAggregate>> CreateAggregateAsync(
        Container container,
        Guid tenantId,
        Guid id,
        bool isDeleted = false)
    {
        var aggregate = new CursorAggregate
        {
            id = id,
            tenantId = tenantId,
            isDeleted = isDeleted
        };

        return container.CreateItemAsync(aggregate, new PartitionKey(tenantId.ToString()));
    }

    private sealed class CursorAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => nameof(CursorAggregate);

        public static string currentStateContainerName => "CursorAggregateCurrentState";

        public bool isDeleted { get; set; }
    }

    private sealed class CursorProjection : NostifyObject, IProjection, IHasExternalData<CursorProjection>
    {
        public static string containerName => "CursorProjection";

        public bool initialized { get; set; }

        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
            List<CursorProjection> projectionsToInit,
            INostify nostify,
            HttpClient? httpClient = null,
            DateTime? pointInTime = null)
            => Task.FromResult(new List<ExternalDataEvent>());
    }

    private sealed class CursorEventType : EventType
    {
        public static CursorEventType Instance { get; } = new();

        private CursorEventType()
            : base("CursorIntegrationEvent", false, false)
        {
        }
    }
}
