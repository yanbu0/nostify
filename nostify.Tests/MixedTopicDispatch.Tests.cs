using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Verifies that logical event types remain independent when a service consumes
/// heterogeneous events from one Kafka topic through Nostify's dispatch routes.
/// </summary>
public sealed class MixedTopicDispatchTests
{
    private const string SharedTopic = "orders.shared";
    private const string FirstEventName = "Mixed_First";
    private const string SecondEventName = "Mixed_Second";

    [Fact]
    public async Task AggregateSingleHandler_WithMixedEventsOnSameTopic_DispatchesEachApplyMethod()
    {
        Guid aggregateId = Guid.NewGuid();
        Guid partitionKey = Guid.NewGuid();
        MixedDispatchAggregate aggregate = new() { id = aggregateId, tenantId = partitionKey };
        Mock<Container> container = CreateReadableContainer(aggregate);
        Mock<INostify> nostify = CreateNostifyWithoutRetries();
        nostify.Setup(n => n.GetCurrentStateContainerAsync<MixedDispatchAggregate>(It.IsAny<string>()))
            .ReturnsAsync(container.Object);
        NostifyKafkaTriggerEvent[] triggers = CreateMixedTriggers(aggregateId, partitionKey, isNew: false);

        foreach (NostifyKafkaTriggerEvent trigger in triggers)
        {
            await DefaultEventHandlers.HandleAggregateEventAsync<MixedDispatchAggregate>(
                nostify.Object,
                trigger,
                allowRetry: false);
        }

        Assert.All(triggers, trigger => Assert.Equal(SharedTopic, trigger.Topic));
        Assert.Equal(1, aggregate.FirstApplyCount);
        Assert.Equal(1, aggregate.SecondApplyCount);
    }

    [Fact]
    public async Task ProjectionSingleHandler_WithMixedEventsOnSameTopic_DispatchesEachApplyMethod()
    {
        Guid projectionId = Guid.NewGuid();
        Guid partitionKey = Guid.NewGuid();
        MixedDispatchProjection projection = new() { id = projectionId, tenantId = partitionKey };
        Mock<Container> container = CreateReadableContainer(projection);
        Mock<INostify> nostify = CreateNostifyWithoutRetries();
        Mock<IProjectionInitializer> initializer = new();
        initializer
            .Setup(i => i.InitAsync(
                It.IsAny<List<MixedDispatchProjection>>(),
                nostify.Object,
                null,
                null,
                null))
            .ReturnsAsync((List<MixedDispatchProjection> values, INostify _, HttpClient? _, DateTime? _, RetryOptions? _) => values);
        nostify.SetupGet(n => n.ProjectionInitializer).Returns(initializer.Object);
        nostify.Setup(n => n.GetProjectionContainerAsync<MixedDispatchProjection>(It.IsAny<string>()))
            .ReturnsAsync(container.Object);
        NostifyKafkaTriggerEvent[] triggers = CreateMixedTriggers(projectionId, partitionKey, isNew: false);

        foreach (NostifyKafkaTriggerEvent trigger in triggers)
        {
            await DefaultEventHandlers.HandleProjectionEventAsync<MixedDispatchProjection>(
                nostify.Object,
                trigger,
                httpClient: null,
                allowRetry: false);
        }

        Assert.All(triggers, trigger => Assert.Equal(SharedTopic, trigger.Topic));
        Assert.Equal(1, projection.FirstApplyCount);
        Assert.Equal(1, projection.SecondApplyCount);
    }

    [Fact]
    public async Task BulkCreateHandlers_WithMixedEventsOnSameTopic_DispatchEveryMatchingEvent()
    {
        Guid partitionKey = Guid.NewGuid();
        string[] events = CreateMixedTriggers(Guid.NewGuid(), partitionKey, isNew: true)
            .Select(SerializeTrigger)
            .ToArray();
        List<MixedDispatchAggregate> aggregates = [];
        List<MixedDispatchProjection> projections = [];
        Mock<Container> aggregateContainer = CreateBulkCreateContainer(aggregates);
        Mock<Container> projectionContainer = CreateBulkCreateContainer(projections);
        Mock<INostify> nostify = CreateNostifyWithoutRetries();
        nostify.Setup(n => n.GetBulkCurrentStateContainerAsync<MixedDispatchAggregate>(It.IsAny<string>()))
            .ReturnsAsync(aggregateContainer.Object);
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<MixedDispatchProjection>(It.IsAny<string>()))
            .ReturnsAsync(projectionContainer.Object);
        nostify.Setup(n => n.InitAllUninitializedAsync<MixedDispatchProjection>(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        int aggregateCount = await DefaultEventHandlers.HandleAggregateBulkCreateEventAsync<MixedDispatchAggregate>(
            nostify.Object,
            events,
            [FirstEventName, SecondEventName]);
        int projectionCount = await DefaultEventHandlers.HandleProjectionBulkCreateEventAsync<MixedDispatchProjection>(
            nostify.Object,
            events,
            [FirstEventName, SecondEventName]);

        Assert.Equal(2, aggregateCount);
        Assert.Equal(2, projectionCount);
        Assert.Equal(1, aggregates.Sum(value => value.FirstApplyCount));
        Assert.Equal(1, aggregates.Sum(value => value.SecondApplyCount));
        Assert.Equal(1, projections.Sum(value => value.FirstApplyCount));
        Assert.Equal(1, projections.Sum(value => value.SecondApplyCount));
    }

    [Fact]
    public async Task BulkUpdateHandlers_WithMixedEventsOnSameTopic_DispatchEveryMatchingEvent()
    {
        Guid partitionKey = Guid.NewGuid();
        NostifyKafkaTriggerEvent[] triggers = CreateMixedTriggersWithDistinctIds(partitionKey, isNew: false);
        string[] events = triggers.Select(SerializeTrigger).ToArray();
        Dictionary<Guid, MixedDispatchAggregate> aggregates = triggers.ToDictionary(
            trigger => trigger.GetEvent()!.aggregateRootId,
            trigger => new MixedDispatchAggregate { id = trigger.GetEvent()!.aggregateRootId, tenantId = partitionKey });
        Dictionary<Guid, MixedDispatchProjection> projections = triggers.ToDictionary(
            trigger => trigger.GetEvent()!.aggregateRootId,
            trigger => new MixedDispatchProjection { id = trigger.GetEvent()!.aggregateRootId, tenantId = partitionKey });
        Mock<Container> aggregateContainer = CreateReadableContainer(aggregates);
        Mock<Container> projectionContainer = CreateReadableContainer(projections);
        Mock<INostify> nostify = CreateNostifyWithoutRetries();
        nostify.Setup(n => n.GetBulkCurrentStateContainerAsync<MixedDispatchAggregate>(It.IsAny<string>()))
            .ReturnsAsync(aggregateContainer.Object);
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<MixedDispatchProjection>(It.IsAny<string>()))
            .ReturnsAsync(projectionContainer.Object);
        nostify.Setup(n => n.InitAsync<MixedDispatchProjection>(It.IsAny<List<MixedDispatchProjection>>()))
            .ReturnsAsync((List<MixedDispatchProjection> values) => values);

        int aggregateCount = await DefaultEventHandlers.HandleAggregateBulkUpdateEventAsync<MixedDispatchAggregate>(
            nostify.Object,
            events,
            [FirstEventName, SecondEventName]);
        int projectionCount = await DefaultEventHandlers.HandleProjectionBulkUpdateEventAsync<MixedDispatchProjection>(
            nostify.Object,
            events,
            [FirstEventName, SecondEventName]);

        Assert.Equal(2, aggregateCount);
        Assert.Equal(2, projectionCount);
        Assert.Equal(1, aggregates.Values.Sum(value => value.FirstApplyCount));
        Assert.Equal(1, aggregates.Values.Sum(value => value.SecondApplyCount));
        Assert.Equal(1, projections.Values.Sum(value => value.FirstApplyCount));
        Assert.Equal(1, projections.Values.Sum(value => value.SecondApplyCount));
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_WithMixedEventsOnSameTopic_DispatchesEachApplyMethod()
    {
        Guid partitionKey = Guid.NewGuid();
        Guid firstTarget = Guid.NewGuid();
        Guid secondTarget = Guid.NewGuid();
        Event first = CreateEvent(FirstEventName, Guid.NewGuid(), partitionKey, isNew: false, new { targetId = firstTarget });
        Event second = CreateEvent(SecondEventName, Guid.NewGuid(), partitionKey, isNew: false, new { targetId = secondTarget });
        string[] events =
        [
            SerializeTrigger(CreateTrigger(first)),
            SerializeTrigger(CreateTrigger(second))
        ];
        Dictionary<Guid, MixedDispatchProjection> projections = new()
        {
            [firstTarget] = new MixedDispatchProjection { id = firstTarget, tenantId = partitionKey },
            [secondTarget] = new MixedDispatchProjection { id = secondTarget, tenantId = partitionKey }
        };
        Mock<Container> container = CreateReadableContainer(projections, bulkEnabled: true);
        Nostify nostify = CreateConcreteNostify();

        List<MixedDispatchProjection> updated = await nostify.BulkApplyAndPersistAsync<MixedDispatchProjection>(
            container.Object,
            "targetId",
            events,
            retryOptions: null);

        Assert.Equal(2, updated.Count);
        Assert.Equal(1, projections.Values.Sum(value => value.FirstApplyCount));
        Assert.Equal(1, projections.Values.Sum(value => value.SecondApplyCount));
    }

    [Fact]
    public async Task MultiApplyHandler_WithMixedEventsOnSameTopic_DispatchesEachApplyMethod()
    {
        Guid sourceId = Guid.NewGuid();
        Guid partitionKey = Guid.NewGuid();
        Guid firstTarget = Guid.NewGuid();
        Guid secondTarget = Guid.NewGuid();
        NostifyKafkaTriggerEvent[] triggers = CreateMixedTriggers(sourceId, partitionKey, isNew: false);
        List<MixedDispatchProjection> projections =
        [
            new() { id = firstTarget, tenantId = partitionKey, SourceId = sourceId },
            new() { id = secondTarget, tenantId = partitionKey, SourceId = sourceId }
        ];
        Mock<Container> container = CreateQueryableContainer(projections);
        Mock<INostify> nostify = CreateNostifyWithoutRetries();
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<MixedDispatchProjection>(It.IsAny<string>()))
            .ReturnsAsync(container.Object);
        nostify.Setup(n => n.MultiApplyAndPersistAsync<MixedDispatchProjection>(
                container.Object,
                It.IsAny<IEvent>(),
                It.IsAny<List<Guid>>(),
                It.IsAny<int>(),
                It.IsAny<RetryOptions?>()))
            .Returns<Container, IEvent, List<Guid>, int, RetryOptions?>((_, @event, ids, _, _) =>
            {
                List<MixedDispatchProjection> matched = projections.Where(value => ids.Contains(value.id)).ToList();
                matched.ForEach(value => value.Apply(@event));
                return Task.FromResult(matched);
            });
        nostify.Setup(n => n.InitAllUninitializedAsync<MixedDispatchProjection>(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        int firstUpdated = await DefaultEventHandlers.HandleMultiApplyEventAsync<MixedDispatchProjection>(
            nostify.Object,
            triggers[0],
            projection => projection.SourceId,
            eventTypeFilter: FirstEventName,
            allowRetry: false);
        int secondUpdated = await DefaultEventHandlers.HandleMultiApplyEventAsync<MixedDispatchProjection>(
            nostify.Object,
            triggers[1],
            projection => projection.SourceId,
            eventTypeFilter: SecondEventName,
            allowRetry: false);

        Assert.Equal(2, firstUpdated);
        Assert.Equal(2, secondUpdated);
        Assert.All(triggers, trigger => Assert.Equal(SharedTopic, trigger.Topic));
        Assert.Equal(2, projections.Sum(value => value.FirstApplyCount));
        Assert.Equal(2, projections.Sum(value => value.SecondApplyCount));
    }

    [Fact]
    public async Task MultiApplyAndPersistAsync_WithEventsFromSameTopic_DispatchesEachApplyMethod()
    {
        Guid partitionKey = Guid.NewGuid();
        Guid firstTarget = Guid.NewGuid();
        Guid secondTarget = Guid.NewGuid();
        NostifyKafkaTriggerEvent[] triggers = CreateMixedTriggers(Guid.NewGuid(), partitionKey, isNew: false);
        Dictionary<Guid, MixedDispatchProjection> projections = new()
        {
            [firstTarget] = new MixedDispatchProjection { id = firstTarget, tenantId = partitionKey },
            [secondTarget] = new MixedDispatchProjection { id = secondTarget, tenantId = partitionKey }
        };
        Mock<Container> container = CreateReadableContainer(projections, bulkEnabled: true);
        Nostify nostify = CreateConcreteNostify();

        await nostify.MultiApplyAndPersistAsync<MixedDispatchProjection>(
            container.Object,
            triggers[0].GetEvent()!,
            [firstTarget]);
        await nostify.MultiApplyAndPersistAsync<MixedDispatchProjection>(
            container.Object,
            triggers[1].GetEvent()!,
            [secondTarget]);

        Assert.All(triggers, trigger => Assert.Equal(SharedTopic, trigger.Topic));
        Assert.Equal(1, projections.Values.Sum(value => value.FirstApplyCount));
        Assert.Equal(1, projections.Values.Sum(value => value.SecondApplyCount));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BulkDeleteHandlers_WithMixedEventsOnSameTopic_DeleteOnlyMatchingEventType(bool aggregateRoute)
    {
        Guid partitionKey = Guid.NewGuid();
        NostifyKafkaTriggerEvent[] triggers = CreateMixedTriggersWithDistinctIds(partitionKey, isNew: false);
        string[] events = triggers.Select(SerializeTrigger).ToArray();
        Guid matchingId = triggers[0].GetEvent()!.aggregateRootId;
        Guid excludedId = triggers[1].GetEvent()!.aggregateRootId;
        List<Guid> patchedIds = [];
        Mock<INostify> nostify = CreateNostifyWithoutRetries();

        int deleted;
        if (aggregateRoute)
        {
            List<MixedDispatchAggregate> values =
            [
                new() { id = matchingId, tenantId = partitionKey },
                new() { id = excludedId, tenantId = partitionKey }
            ];
            Mock<Container> container = CreateBulkDeleteContainer(values, patchedIds);
            nostify.Setup(n => n.GetBulkCurrentStateContainerAsync<MixedDispatchAggregate>(It.IsAny<string>()))
                .ReturnsAsync(container.Object);
            deleted = await DefaultEventHandlers.HandleAggregateBulkDeleteEventAsync<MixedDispatchAggregate>(
                nostify.Object,
                events,
                [FirstEventName]);
        }
        else
        {
            List<MixedDispatchProjection> values =
            [
                new() { id = matchingId, tenantId = partitionKey },
                new() { id = excludedId, tenantId = partitionKey }
            ];
            Mock<Container> container = CreateBulkDeleteContainer(values, patchedIds);
            nostify.Setup(n => n.GetBulkProjectionContainerAsync<MixedDispatchProjection>(It.IsAny<string>()))
                .ReturnsAsync(container.Object);
            deleted = await DefaultEventHandlers.HandleProjectionBulkDeleteEventAsync<MixedDispatchProjection>(
                nostify.Object,
                events,
                [FirstEventName]);
        }

        Assert.Equal(1, deleted);
        Assert.Equal([matchingId], patchedIds);
        Assert.DoesNotContain(excludedId, patchedIds);
        Assert.All(triggers, trigger => Assert.Equal(SharedTopic, trigger.Topic));
    }

    [Fact]
    public async Task BulkDeleteHandlers_WithMixedEventsOnSameTopic_IgnoreNonMatchingEventTypes()
    {
        Guid partitionKey = Guid.NewGuid();
        string[] events = CreateMixedTriggersWithDistinctIds(partitionKey, isNew: false)
            .Select(SerializeTrigger)
            .ToArray();
        var aggregateContainer = new Mock<Container>(MockBehavior.Strict);
        var projectionContainer = new Mock<Container>(MockBehavior.Strict);
        Mock<INostify> nostify = CreateNostifyWithoutRetries();
        nostify.Setup(n => n.GetBulkCurrentStateContainerAsync<MixedDispatchAggregate>(It.IsAny<string>()))
            .ReturnsAsync(aggregateContainer.Object);
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<MixedDispatchProjection>(It.IsAny<string>()))
            .ReturnsAsync(projectionContainer.Object);

        int aggregateDeleted = await DefaultEventHandlers.HandleAggregateBulkDeleteEventAsync<MixedDispatchAggregate>(
            nostify.Object,
            events,
            ["Unrelated_Event"]);
        int projectionDeleted = await DefaultEventHandlers.HandleProjectionBulkDeleteEventAsync<MixedDispatchProjection>(
            nostify.Object,
            events,
            ["Unrelated_Event"]);

        Assert.Equal(0, aggregateDeleted);
        Assert.Equal(0, projectionDeleted);
        aggregateContainer.VerifyNoOtherCalls();
        projectionContainer.VerifyNoOtherCalls();
    }

    private static Mock<INostify> CreateNostifyWithoutRetries()
    {
        var nostify = new Mock<INostify>();
        nostify.SetupGet(n => n.DefaultRetryOptions).Returns((RetryOptions?)null);
        nostify.Setup(n => n.HandleUndeliverableAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEvent>(),
                It.IsAny<ErrorCommand?>()))
            .Returns(Task.CompletedTask);
        return nostify;
    }

    private static Nostify CreateConcreteNostify()
    {
        return new Nostify(
            new NostifyCosmosClient(),
            "/tenantId",
            Guid.Empty,
            "localhost:9092",
            Mock.Of<Confluent.Kafka.IProducer<string, string>>(),
            Mock.Of<IHttpClientFactory>());
    }

    private static NostifyKafkaTriggerEvent[] CreateMixedTriggers(Guid aggregateId, Guid partitionKey, bool isNew)
    {
        return
        [
            CreateTrigger(CreateEvent(FirstEventName, aggregateId, partitionKey, isNew, new { value = "first" })),
            CreateTrigger(CreateEvent(SecondEventName, aggregateId, partitionKey, isNew, new { value = "second" }))
        ];
    }

    private static NostifyKafkaTriggerEvent[] CreateMixedTriggersWithDistinctIds(Guid partitionKey, bool isNew)
    {
        return
        [
            CreateTrigger(CreateEvent(FirstEventName, Guid.NewGuid(), partitionKey, isNew, new { value = "first" })),
            CreateTrigger(CreateEvent(SecondEventName, Guid.NewGuid(), partitionKey, isNew, new { value = "second" }))
        ];
    }

    private static Event CreateEvent(
        string name,
        Guid aggregateId,
        Guid partitionKey,
        bool isNew,
        object payload)
    {
        // Use the name-carrying command type so distinct logical names and isNew metadata
        // survive the Kafka JSON round trip independently of the shared broker topic.
        return new Event(
            new NostifyCommand(name, isNew),
            aggregateId,
            payload,
            partitionKey: partitionKey);
    }

    private static NostifyKafkaTriggerEvent CreateTrigger(Event @event)
    {
        return new NostifyKafkaTriggerEvent
        {
            Topic = SharedTopic,
            Key = @event.aggregateRootId.ToString(),
            Value = JsonConvert.SerializeObject(@event, SerializationSettings.NostifyDefault),
            Headers = []
        };
    }

    private static string SerializeTrigger(NostifyKafkaTriggerEvent trigger)
    {
        return JsonConvert.SerializeObject(trigger, SerializationSettings.NostifyDefault);
    }

    private static Mock<Container> CreateReadableContainer<T>(T value)
        where T : NostifyObject
    {
        return CreateReadableContainer(new Dictionary<Guid, T> { [value.id] = value });
    }

    private static Mock<Container> CreateReadableContainer<T>(
        Dictionary<Guid, T> values,
        bool bulkEnabled = false)
        where T : NostifyObject
    {
        var container = new Mock<Container>();
        if (bulkEnabled)
        {
            var client = new Mock<CosmosClient>();
            client.SetupGet(c => c.ClientOptions).Returns(new CosmosClientOptions { AllowBulkExecution = true });
            var database = new Mock<Database>();
            database.SetupGet(d => d.Client).Returns(client.Object);
            container.SetupGet(c => c.Database).Returns(database.Object);
        }

        container
            .Setup(c => c.ReadItemAsync<T>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, _, _, _) =>
            {
                T value = values[Guid.Parse(id)];
                var response = new Mock<ItemResponse<T>>();
                response.SetupGet(r => r.Resource).Returns(value);
                return Task.FromResult(response.Object);
            });
        container
            .Setup(c => c.PatchItemAsync<T>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, IReadOnlyList<PatchOperation>, PatchItemRequestOptions, CancellationToken>(
                (id, _, _, _, _) =>
                {
                    var response = new Mock<ItemResponse<T>>();
                    response.SetupGet(r => r.Resource).Returns(values[Guid.Parse(id)]);
                    return Task.FromResult(response.Object);
                });
        return container;
    }

    private static Mock<Container> CreateQueryableContainer<T>(List<T> values)
        where T : NostifyObject
    {
        var container = new Mock<Container>();
        container.Setup(c => c.GetItemLinqQueryable<T>(
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>(),
                It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(values.AsQueryable().OrderBy(_ => 1));
        return container;
    }

    private static Mock<Container> CreateBulkDeleteContainer<T>(List<T> values, List<Guid> patchedIds)
        where T : NostifyObject
    {
        Mock<Container> container = CreateQueryableContainer(values);
        var client = new Mock<CosmosClient>();
        client.SetupGet(c => c.ClientOptions).Returns(new CosmosClientOptions { AllowBulkExecution = true });
        var database = new Mock<Database>();
        database.SetupGet(d => d.Client).Returns(client.Object);
        container.SetupGet(c => c.Database).Returns(database.Object);

        var properties = new ContainerProperties("mixed", "/tenantId") { DefaultTimeToLive = -1 };
        var containerResponse = new Mock<ContainerResponse>();
        containerResponse.SetupGet(response => response.Resource).Returns(properties);
        container.Setup(c => c.ReadContainerAsync(
                It.IsAny<ContainerRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerResponse.Object);
        container.Setup(c => c.PatchItemAsync<T>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PartitionKey, IReadOnlyList<PatchOperation>, PatchItemRequestOptions, CancellationToken>(
                (id, _, _, _, _) => patchedIds.Add(Guid.Parse(id)))
            .ReturnsAsync(Mock.Of<ItemResponse<T>>());
        return container;
    }

    private static Mock<Container> CreateBulkCreateContainer<T>(List<T> created)
        where T : NostifyObject
    {
        var client = new Mock<CosmosClient>();
        client.SetupGet(c => c.ClientOptions).Returns(new CosmosClientOptions { AllowBulkExecution = true });
        var database = new Mock<Database>();
        database.SetupGet(d => d.Client).Returns(client.Object);
        var container = new Mock<Container>();
        container.SetupGet(c => c.Database).Returns(database.Object);
        container
            .Setup(c => c.CreateItemAsync(
                It.IsAny<T>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<T, PartitionKey?, ItemRequestOptions, CancellationToken>((value, _, _, _) => created.Add(value))
            .ReturnsAsync(Mock.Of<ItemResponse<T>>());
        return container;
    }

    public class MixedDispatchAggregate : NostifyObject, IAggregate
    {
        public bool isDeleted { get; set; }
        public static string aggregateType => "MixedDispatch";
        public static string currentStateContainerName => "MixedDispatchCurrentState";
        public int FirstApplyCount { get; private set; }
        public int SecondApplyCount { get; private set; }

        public MixedDispatchAggregate()
        {
        }

        [ApplyEvents(FirstEventName)]
        protected void ApplyFirst(IEvent @event)
        {
            FirstApplyCount++;
            SetIdentity(@event);
        }

        [ApplyEvents(SecondEventName)]
        protected void ApplySecond(IEvent @event)
        {
            SecondApplyCount++;
            SetIdentity(@event);
        }

        private void SetIdentity(IEvent @event)
        {
            id = @event.aggregateRootId;
            tenantId = @event.partitionKey;
        }
    }

    public class MixedDispatchProjection : NostifyObject, IProjection, IHasExternalData<MixedDispatchProjection>
    {
        public bool initialized { get; set; }
        public Guid? SourceId { get; set; }
        public static string containerName => "MixedDispatchProjection";
        public int FirstApplyCount { get; private set; }
        public int SecondApplyCount { get; private set; }

        public MixedDispatchProjection()
        {
        }

        [ApplyEvents(FirstEventName)]
        protected void ApplyFirst(IEvent @event)
        {
            FirstApplyCount++;
            SetIdentity(@event);
        }

        [ApplyEvents(SecondEventName)]
        protected void ApplySecond(IEvent @event)
        {
            SecondApplyCount++;
            SetIdentity(@event);
        }

        private void SetIdentity(IEvent @event)
        {
            if (id == Guid.Empty)
            {
                id = @event.aggregateRootId;
            }
            tenantId = @event.partitionKey;
        }

        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
            List<MixedDispatchProjection> projectionsToInit,
            INostify nostify,
            HttpClient? httpClient = null,
            DateTime? pointInTime = null)
        {
            return Task.FromResult(new List<ExternalDataEvent>());
        }
    }
}
