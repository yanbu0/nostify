using Confluent.Kafka;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Exercises concrete <see cref="Nostify"/> behavior through its injectable Kafka and Cosmos boundaries.
/// </summary>
public sealed class NostifyBehaviorTests
{
    [Fact]
    public async Task PublishEventAsync_WithMultipleEvents_PublishesEachEventToItsEventTypeTopic()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string>());
        Nostify nostify = CreateNostify(producer: producer.Object);
        Event first = CreateEvent(new PublishEventType("FirstTopic"), new { name = "first" });
        Event second = CreateEvent(new PublishEventType("SecondTopic"), new { name = "second" });

        await nostify.PublishEventAsync([first, second]);

        producer.Verify(p => p.ProduceAsync(
            "FirstTopic",
            It.Is<Message<string, string>>(message =>
                message.Value.Contains(first.id.ToString(), StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        producer.Verify(p => p.ProduceAsync(
            "SecondTopic",
            It.Is<Message<string, string>>(message =>
                message.Value.Contains(second.id.ToString(), StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishEventAsync_WithOutputEnabled_LogsDistinctPublishedTopics()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string>());
        var logger = new Mock<ILogger>();
        logger.Setup(candidate => candidate.IsEnabled(LogLevel.Information)).Returns(true);
        Nostify nostify = CreateNostify(producer: producer.Object, logger: logger.Object);
        Event first = CreateEvent(new PublishEventType("SharedTopic"), new { index = 1 });
        Event second = CreateEvent(new PublishEventType("SharedTopic"), new { index = 2 });

        await nostify.PublishEventAsync([first, second], showOutput: true);

        VerifyLog(logger, LogLevel.Information, "SharedTopic", Times.Once());
    }

    [Fact]
    public async Task PublishEventAsync_WhenProducerFailsAndOutputIsEnabled_LogsAndRethrows()
    {
        var expected = new InvalidOperationException("broker unavailable");
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var logger = new Mock<ILogger>();
        logger.Setup(candidate => candidate.IsEnabled(LogLevel.Error)).Returns(true);
        Nostify nostify = CreateNostify(producer: producer.Object, logger: logger.Object);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nostify.PublishEventAsync(
                [CreateEvent(new PublishEventType("FailureTopic"), new { })],
                showOutput: true));

        Assert.Same(expected, actual);
        VerifyLog(logger, LogLevel.Error, "FailureTopic", Times.Once());
    }

    [Fact]
    public async Task PublishEventAsync_WhenProducerFails_RethrowsOriginalException()
    {
        var expected = new InvalidOperationException("broker unavailable");
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        Nostify nostify = CreateNostify(producer: producer.Object);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nostify.PublishEventAsync(CreateEvent(new PublishEventType("FailureTopic"), new { })));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task PublishEventAsync_WithTriggerJson_DeserializesAndPublishesEveryEvent()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string>());
        Nostify nostify = CreateNostify(producer: producer.Object);
        Event first = CreateEvent(new PublishEventType("JsonTopicOne"), new { value = 1 });
        Event second = CreateEvent(new PublishEventType("JsonTopicTwo"), new { value = 2 });
        string triggerOutput = JsonConvert.SerializeObject(new[] { first, second });

        await nostify.PublishEventAsync(triggerOutput);

        producer.Verify(p => p.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_WithListTargetIds_AppliesEventToEveryTarget()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Event @event = CreateEvent(
            new PublishEventType("ProjectionUpdated"),
            new { targetIds = new List<Guid> { firstId, secondId }, name = "updated" });
        Mock<Container> container = CreateBulkProjectionContainer();
        Nostify nostify = CreateNostify();

        List<TestProjection> result = await nostify.BulkApplyAndPersistAsync<TestProjection>(
            container.Object,
            "targetIds",
            [CreateTriggerJson(@event)],
            retryOptions: null);

        Assert.Equal(2, result.Count);
        container.Verify(c => c.ReadItemAsync<TestProjection>(
            It.Is<string>(id => id == firstId.ToString() || id == secondId.ToString()),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_WithScalarTargetId_AppliesEventOnce()
    {
        Guid targetId = Guid.NewGuid();
        Event @event = CreateEvent(
            new PublishEventType("ProjectionUpdated"),
            new { targetId, name = "updated" });
        Mock<Container> container = CreateBulkProjectionContainer();
        Nostify nostify = CreateNostify();

        List<TestProjection> result = await nostify.BulkApplyAndPersistAsync<TestProjection>(
            container.Object,
            "targetId",
            [CreateTriggerJson(@event)],
            allowRetry: false);

        Assert.Single(result);
        container.Verify(c => c.ReadItemAsync<TestProjection>(
            targetId.ToString(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_WithNullPayload_ThrowsDescriptiveException()
    {
        Event @event = CreateEvent(new PublishEventType("ProjectionUpdated"), payload: null);
        Mock<Container> container = CreateBulkProjectionContainer();
        Nostify nostify = CreateNostify();

        NostifyException exception = await Assert.ThrowsAsync<NostifyException>(() =>
            nostify.BulkApplyAndPersistAsync<TestProjection>(
                container.Object,
                "targetId",
                [CreateTriggerJson(@event)],
                retryOptions: null));

        Assert.Contains("payload is null", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("targetId", exception.Message, StringComparison.Ordinal);
        container.Verify(c => c.ReadItemAsync<TestProjection>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BulkPersistEventAsync_WithoutRetries_PersistsEveryEventAcrossBatches()
    {
        var container = new Mock<Container>();
        container
            .Setup(c => c.CreateItemAsync<IEvent>(
                It.IsAny<IEvent>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<ItemResponse<IEvent>>().Object);
        var nostify = new BulkPersistTestNostify(container.Object);
        List<IEvent> events =
        [
            CreateEvent(new PublishEventType("Persisted"), new { index = 1 }),
            CreateEvent(new PublishEventType("Persisted"), new { index = 2 }),
            CreateEvent(new PublishEventType("Persisted"), new { index = 3 })
        ];

        await nostify.BulkPersistEventAsync(
            events,
            batchSize: 2,
            retryOptions: null);

        container.Verify(c => c.CreateItemAsync<IEvent>(
            It.Is<IEvent>(@event => events.Contains(@event)),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
        Assert.Empty(nostify.UndeliverableCalls);
    }

    [Fact]
    public async Task BulkPersistEventAsync_WhenOneWriteFails_RecordsFailureAndPersistsSiblings()
    {
        Event failedEvent = CreateEvent(new PublishEventType("Persisted"), new { index = 1 });
        Event successfulEvent = CreateEvent(new PublishEventType("Persisted"), new { index = 2 });
        var expected = new InvalidOperationException("write unavailable");
        var container = new Mock<Container>();
        container
            .Setup(c => c.CreateItemAsync<IEvent>(
                It.Is<IEvent>(@event => ReferenceEquals(@event, failedEvent)),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        container
            .Setup(c => c.CreateItemAsync<IEvent>(
                It.Is<IEvent>(@event => ReferenceEquals(@event, successfulEvent)),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<ItemResponse<IEvent>>().Object);
        var nostify = new BulkPersistTestNostify(container.Object);

        await nostify.BulkPersistEventAsync(
            [failedEvent, successfulEvent],
            batchSize: null,
            retryOptions: null,
            publishErrorEvents: true);

        container.Verify(c => c.CreateItemAsync<IEvent>(
            successfulEvent,
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var undeliverable = Assert.Single(nostify.UndeliverableCalls);
        Assert.Equal(nameof(Nostify.BulkPersistEventAsync), undeliverable.FunctionName);
        Assert.Equal(expected.Message, undeliverable.ErrorMessage);
        Assert.Same(failedEvent, undeliverable.Event);
        Assert.Equal(ErrorCommand.BulkPersistEvent, undeliverable.Command);
    }

    [Fact]
    public async Task BulkPersistEventAsync_BooleanOverloadWithoutRetries_ExecutesConcretePersistence()
    {
        var container = new Mock<Container>();
        container
            .Setup(c => c.CreateItemAsync<IEvent>(
                It.IsAny<IEvent>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<ItemResponse<IEvent>>().Object);
        var nostify = new BulkPersistTestNostify(container.Object);
        Event @event = CreateEvent(new PublishEventType("Persisted"), new { });

        await nostify.BulkPersistEventAsync(
            [@event],
            batchSize: 1,
            allowRetry: false,
            publishErrorEvents: false);

        container.Verify(c => c.CreateItemAsync<IEvent>(
            @event,
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitAsync_WithIdAndNoHttpClientFactory_ThrowsConfigurationError()
    {
        Nostify nostify = CreateNostifyWithoutHttp();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nostify.InitAsync<TestProjection, TestAggregate>(Guid.NewGuid()));

        Assert.Contains("WithHttp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitAsync_WithIdsAndNoHttpClientFactory_ThrowsConfigurationError()
    {
        Nostify nostify = CreateNostifyWithoutHttp();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nostify.InitAsync<TestProjection, TestAggregate>([Guid.NewGuid()]));

        Assert.Contains("WithHttp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitAsync_WithProjectionsAndNoHttpClientFactory_ThrowsConfigurationError()
    {
        Nostify nostify = CreateNostifyWithoutHttp();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nostify.InitAsync([new TestProjection()]));

        Assert.Contains("WithHttp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitContainerAsync_WithNoHttpClientFactory_ThrowsConfigurationError()
    {
        Nostify nostify = CreateNostifyWithoutHttp();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nostify.InitContainerAsync<TestProjection, TestAggregate>());

        Assert.Contains("WithHttp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitAllUninitializedAsync_WithNoHttpClientFactory_ThrowsConfigurationError()
    {
        Nostify nostify = CreateNostifyWithoutHttp();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nostify.InitAllUninitializedAsync<TestProjection>());

        Assert.Contains("WithHttp", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KafkaConsumerCreation_WithoutBaseConfiguration_ThrowsActionableError(bool useCache)
    {
        Nostify nostify = CreateNostify();

        NostifyException exception = Assert.Throws<NostifyException>(() =>
            useCache
                ? nostify.GetOrCreateKafkaConsumer("missing-config-group")
                : nostify.CreateKafkaConsumer("missing-config-group"));

        // Both consumer creation paths must identify the factory configuration
        // calls that provide the otherwise-required base consumer settings.
        Assert.Contains("WithKafka() or WithEventHubs()", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_WithCachedConsumer_ClosesAndDisposesItOnlyOnce()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        var producer = new Mock<IProducer<string, string>>();
        Nostify nostify = CreateNostify(producer: producer.Object);
        AddCachedConsumer(nostify, "projection-group", consumer.Object);

        nostify.Dispose();
        nostify.Dispose();

        consumer.Verify(candidate => candidate.Close(), Times.Once);
        consumer.Verify(candidate => candidate.Dispose(), Times.Once);
        producer.Verify(candidate => candidate.Flush(It.IsAny<TimeSpan>()), Times.Once);
        producer.Verify(candidate => candidate.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_WhenCachedConsumerCleanupFails_ContinuesDisposingOtherResources()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.Setup(candidate => candidate.Close()).Throws<InvalidOperationException>();
        consumer.Setup(candidate => candidate.Dispose()).Throws<InvalidOperationException>();
        var producer = new Mock<IProducer<string, string>>();
        var logger = new Mock<ILogger>();
        logger.Setup(candidate => candidate.IsEnabled(LogLevel.Warning)).Returns(true);
        Nostify nostify = CreateNostify(producer: producer.Object, logger: logger.Object);
        AddCachedConsumer(nostify, "faulty-group", consumer.Object);

        nostify.Dispose();

        consumer.Verify(candidate => candidate.Close(), Times.Once);
        consumer.Verify(candidate => candidate.Dispose(), Times.Once);
        producer.Verify(candidate => candidate.Dispose(), Times.Once);
        VerifyLog(logger, LogLevel.Warning, "faulty-group", Times.Exactly(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetNextSequenceValuesAsync_WithNonPositiveCount_ThrowsBeforeRepositoryAccess(int count)
    {
        Nostify nostify = CreateNostify();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            nostify.GetNextSequenceValuesAsync("invoice", "tenant", count, startingValue: 100));

        Assert.Equal("count", exception.ParamName);
        Assert.Contains("greater than zero", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkApplyAndPersistAsync_WithNullTrigger_ThrowsDescriptiveException()
    {
        Mock<Container> container = CreateBulkProjectionContainer();
        Nostify nostify = CreateNostify();

        NostifyException exception = await Assert.ThrowsAsync<NostifyException>(() =>
            nostify.BulkApplyAndPersistAsync<TestProjection>(
                container.Object,
                "targetId",
                ["null"],
                retryOptions: null));

        Assert.Equal("Event is null", exception.Message);
    }

    [Fact]
    public async Task CreateContainersAsync_OutsideLocalhostAndLocalhostOnly_ReturnsWithoutConnecting()
    {
        var repository = new NostifyCosmosClient(
            "unused-key",
            "unused-database",
            ConnectionString: "AccountEndpoint=https://example.invalid/;AccountKey=unused;");
        Nostify nostify = CreateNostify(repository: repository);

        await nostify.CreateContainersAsync<NostifyBehaviorTests>(localhostOnly: true);
    }

    [Fact]
    public async Task CreateContainersAsync_WithoutConnectionString_ReturnsWithoutConnecting()
    {
        Nostify nostify = CreateNostify(repository: new NostifyCosmosClient());

        await nostify.CreateContainersAsync<NostifyBehaviorTests>(localhostOnly: false);
    }

    private static Nostify CreateNostify(
        IProducer<string, string>? producer = null,
        ILogger? logger = null,
        NostifyCosmosClient? repository = null)
    {
        return new Nostify(
            repository ?? new NostifyCosmosClient(),
            "/tenantId",
            Guid.Empty,
            "localhost:9092",
            producer ?? new Mock<IProducer<string, string>>().Object,
            new Mock<IHttpClientFactory>().Object,
            logger);
    }

    private static Nostify CreateNostifyWithoutHttp()
    {
        return new Nostify(
            new NostifyCosmosClient(),
            "/tenantId",
            Guid.Empty,
            "localhost:9092",
            new Mock<IProducer<string, string>>().Object,
            httpClientFactory: null);
    }

    private static Event CreateEvent(EventType eventType, object? payload)
    {
        // EventType can explicitly permit null payloads, but Event's constructor
        // retains a non-nullable payload annotation for compatibility.
        return new Event(
            eventType,
            Guid.NewGuid(),
            payload!,
            partitionKey: Guid.NewGuid());
    }

    private static string CreateTriggerJson(Event @event)
    {
        var trigger = new NostifyKafkaTriggerEvent
        {
            Topic = @event.eventType.name,
            Key = @event.aggregateRootId.ToString(),
            Value = JsonConvert.SerializeObject(@event, SerializationSettings.NostifyDefault),
            Headers = []
        };

        return JsonConvert.SerializeObject(trigger);
    }

    // Populate the private cache directly so disposal behavior can be tested without
    // constructing a native Kafka consumer or connecting to a broker.
    private static void AddCachedConsumer(
        Nostify nostify,
        string consumerGroup,
        IConsumer<string, string> consumer)
    {
        var field = typeof(Nostify).GetField(
            "_kafkaConsumers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var consumers = Assert.IsType<System.Collections.Concurrent.ConcurrentDictionary<string, IConsumer<string, string>>>(
            field?.GetValue(nostify));

        Assert.True(consumers.TryAdd(consumerGroup, consumer));
    }

    private static void VerifyLog(
        Mock<ILogger> logger,
        LogLevel level,
        string messageFragment,
        Times times)
    {
        logger.Verify(candidate => candidate.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(messageFragment, StringComparison.Ordinal)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
    }

    private static Mock<Container> CreateBulkProjectionContainer()
    {
        var client = new Mock<CosmosClient>();
        client.SetupGet(c => c.ClientOptions).Returns(new CosmosClientOptions { AllowBulkExecution = true });
        var database = new Mock<Database>();
        database.SetupGet(d => d.Client).Returns(client.Object);
        var container = new Mock<Container>();
        container.SetupGet(c => c.Database).Returns(database.Object);

        container
            .Setup(c => c.ReadItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, _, _, _) =>
            {
                var response = new Mock<ItemResponse<TestProjection>>();
                response.SetupGet(r => r.Resource).Returns(new TestProjection
                {
                    id = Guid.Parse(id),
                    name = "original"
                });
                return Task.FromResult(response.Object);
            });

        container
            .Setup(c => c.PatchItemAsync<TestProjection>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, PartitionKey, IReadOnlyList<PatchOperation>, PatchItemRequestOptions, CancellationToken>(
                (id, _, _, _, _) =>
                {
                    var response = new Mock<ItemResponse<TestProjection>>();
                    response.SetupGet(r => r.Resource).Returns(new TestProjection
                    {
                        id = Guid.Parse(id),
                        name = "updated"
                    });
                    return Task.FromResult(response.Object);
                });

        return container;
    }

    private sealed class BulkPersistTestNostify : Nostify
    {
        private readonly Container _eventContainer;

        public BulkPersistTestNostify(Container eventContainer)
            : base(
                new NostifyCosmosClient(),
                "/tenantId",
                Guid.Empty,
                "localhost:9092",
                new Mock<IProducer<string, string>>().Object,
                new Mock<IHttpClientFactory>().Object)
        {
            _eventContainer = eventContainer;
        }

        public List<(
            string FunctionName,
            string ErrorMessage,
            IEvent Event,
            ErrorCommand? Command)> UndeliverableCalls { get; } = [];

        public override Task<Container> GetEventStoreContainerAsync(bool allowBulk = false)
        {
            return Task.FromResult(_eventContainer);
        }

        public override Task HandleUndeliverableAsync(
            string functionName,
            string errorMessage,
            IEvent eventToHandle,
            ErrorCommand? errorCommand = null)
        {
            UndeliverableCalls.Add((functionName, errorMessage, eventToHandle, errorCommand));
            return Task.CompletedTask;
        }
    }

    private sealed class PublishEventType : EventType
    {
        // Event-type deserialization resolves immutable metadata through a
        // parameterless constructor, including non-public constructors.
        private PublishEventType()
            : base("BehaviorEvent", isNew: false, allowNullPayload: true)
        {
        }

        public PublishEventType(string name)
            : base(name, isNew: false, allowNullPayload: true)
        {
        }
    }
}
