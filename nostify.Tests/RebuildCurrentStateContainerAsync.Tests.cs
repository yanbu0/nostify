using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace nostify.Tests;

public sealed class RebuildCurrentStateContainerAsyncTests
{
    [Fact]
    public async Task RebuildCurrentStateContainerAsync_RehydratesEachStreamInTimestampOrder()
    {
        Guid aggregateId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        List<IEvent> events =
        [
            CreateEvent(
                RebuildUpdated.Instance,
                aggregateId,
                tenantId,
                new DateTime(2026, 1, 1, 0, 2, 0, DateTimeKind.Utc),
                "second"),
            CreateEvent(
                RebuildCreated.Instance,
                aggregateId,
                tenantId,
                new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc),
                "first")
        ];
        using TestContext context = CreateContext(events);

        await context.Nostify.RebuildCurrentStateContainerAsync<RebuildAggregate>();

        RebuildAggregate rebuilt = Assert.Single(context.Upserted);
        Assert.Equal(aggregateId, rebuilt.id);
        Assert.Equal(tenantId, rebuilt.tenantId);
        Assert.Equal(new[] { "first", "second" }, rebuilt.appliedValues);
        context.CurrentState.Verify(container => container.UpsertItemAsync(
            It.IsAny<RebuildAggregate>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RebuildCurrentStateContainerAsync_DoesNotRebuildStreamsForUnrelatedEventTypes()
    {
        Guid targetAggregateId = Guid.NewGuid();
        Guid unrelatedAggregateId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        List<IEvent> events =
        [
            CreateEvent(
                RebuildCreated.Instance,
                targetAggregateId,
                tenantId,
                new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc),
                "target"),
            CreateEvent(
                UnrelatedCreated.Instance,
                unrelatedAggregateId,
                tenantId,
                new DateTime(2026, 1, 1, 0, 2, 0, DateTimeKind.Utc),
                "unrelated")
        ];
        using TestContext context = CreateContext(events);

        await context.Nostify.RebuildCurrentStateContainerAsync<RebuildAggregate>();

        // Ensure that events handled by T are use to rebuild
        Assert.Contains(context.Upserted, aggregate => aggregate.id == targetAggregateId);
        Assert.DoesNotContain(context.Upserted, aggregate => aggregate.id == unrelatedAggregateId);
        Assert.Single(context.Upserted);
    }

    private static Event CreateEvent(
        EventType eventType,
        Guid aggregateId,
        Guid tenantId,
        DateTime timestamp,
        string value)
    {
        return new Event(
            eventType,
            aggregateId,
            new { id = aggregateId, tenantId, value },
            partitionKey: tenantId)
        {
            timestamp = timestamp
        };
    }

    private static TestContext CreateContext(List<IEvent> events)
    {
        Mock<Container> eventStore = CosmosTestHelpers.CreateMockContainer(events);
        Mock<Container> currentState = CosmosTestHelpers.CreateMockContainer(new List<RebuildAggregate>());
        var upserted = new ConcurrentBag<RebuildAggregate>();

        var cosmosClient = new Mock<CosmosClient>();
        cosmosClient.SetupGet(client => client.ClientOptions)
            .Returns(new CosmosClientOptions { AllowBulkExecution = true });
        var currentStateDatabase = new Mock<Database>();
        currentStateDatabase.SetupGet(database => database.Client).Returns(cosmosClient.Object);
        currentState.SetupGet(container => container.Database).Returns(currentStateDatabase.Object);

        var containerProperties = new ContainerProperties(
            RebuildAggregate.currentStateContainerName,
            "/tenantId")
        {
            DefaultTimeToLive = -1
        };
        var containerResponse = new Mock<ContainerResponse>();
        containerResponse.SetupGet(response => response.Resource).Returns(containerProperties);
        currentState.Setup(container => container.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerResponse.Object);
        currentState.Setup(container => container.UpsertItemAsync(
                It.IsAny<RebuildAggregate>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<RebuildAggregate, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (aggregate, _, _, _) => upserted.Add(aggregate))
            .ReturnsAsync(new Mock<ItemResponse<RebuildAggregate>>().Object);

        var regularDatabase = new Mock<Database>();
        regularDatabase.Setup(database => database.GetContainer("eventStore"))
            .Returns(eventStore.Object);
        var bulkDatabase = new Mock<Database>();
        bulkDatabase.Setup(database => database.GetContainer(RebuildAggregate.currentStateContainerName))
            .Returns(currentState.Object);

        var repository = new NostifyCosmosClient();
        SetPrivateProperty(repository, "_cosmosClient", new Mock<CosmosClient>().Object);
        SetPrivateProperty(repository, "_bulkCosmosClient", cosmosClient.Object);
        SetPrivateProperty(
            repository,
            "_database",
            new DatabaseRef
            {
                database = regularDatabase.Object,
                knownContainers = ["eventStore"]
            });
        SetPrivateProperty(
            repository,
            "_bulkDatabase",
            new DatabaseRef
            {
                database = bulkDatabase.Object,
                knownContainers = [RebuildAggregate.currentStateContainerName]
            });

        var nostify = new Nostify(
            repository,
            "/tenantId",
            Guid.Empty,
            "localhost:9092",
            kafkaProducer: null,
            httpClientFactory: null);

        return new TestContext(nostify, repository, currentState, upserted);
    }

    private static void SetPrivateProperty<T>(NostifyCosmosClient repository, string name, T value)
    {
        typeof(NostifyCosmosClient)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(repository, value);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            Nostify nostify,
            NostifyCosmosClient repository,
            Mock<Container> currentState,
            ConcurrentBag<RebuildAggregate> upserted)
        {
            Nostify = nostify;
            Repository = repository;
            CurrentState = currentState;
            Upserted = upserted;
        }

        public Nostify Nostify { get; }
        public NostifyCosmosClient Repository { get; }
        public Mock<Container> CurrentState { get; }
        public ConcurrentBag<RebuildAggregate> Upserted { get; }

        public void Dispose()
        {
            Nostify.Dispose();
            Repository.Dispose();
        }
    }

    private sealed class RebuildCreated : EventType
    {
        public static RebuildCreated Instance { get; } = new();

        private RebuildCreated()
            : base("Create_RebuildCurrentStateTest", isNew: true)
        {
        }
    }

    private sealed class RebuildUpdated : EventType
    {
        public static RebuildUpdated Instance { get; } = new();

        private RebuildUpdated()
            : base("Update_RebuildCurrentStateTest")
        {
        }
    }

    private sealed class UnrelatedCreated : EventType
    {
        public static UnrelatedCreated Instance { get; } = new();

        private UnrelatedCreated()
            : base("Create_UnrelatedRebuildCurrentStateTest", isNew: true)
        {
        }
    }

    public sealed class RebuildAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => nameof(RebuildAggregate);
        public static string currentStateContainerName => "RebuildAggregateCurrentState";

        public bool isDeleted { get; set; }
        public List<string> appliedValues { get; } = [];

        [ApplyEvents(typeof(RebuildCreated))]
        [ApplyEvents(typeof(RebuildUpdated))]
        private void ApplyRebuildEvent(IEvent eventToApply)
        {
            JObject payload = JObject.FromObject(eventToApply.payload!);
            id = payload.Value<Guid>(nameof(id));
            tenantId = payload.Value<Guid>(nameof(tenantId));
            appliedValues.Add(payload.Value<string>("value")!);
        }
    }
}
