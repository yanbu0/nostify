using Confluent.Kafka;
using Newtonsoft.Json;

namespace nostify.IntegrationTests;

/// <summary>
/// Exercises the asynchronous event-request transport against a real Kafka broker without Cosmos.
/// </summary>
[Collection(KafkaIntegrationCollection.Name)]
[Trait(IntegrationTraits.Category, IntegrationTraits.Integration)]
[Trait(IntegrationTraits.Dependency, IntegrationTraits.Kafka)]
public sealed class KafkaAsyncRequestTests
{
    private readonly KafkaIntegrationFixture _fixture;

    /// <summary>Creates asynchronous transport tests backed by the shared Kafka fixture.</summary>
    public KafkaAsyncRequestTests(KafkaIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Async_request_round_trip_preserves_contract_filters_noise_and_accumulates_chunks()
    {
        string serviceName = _fixture.UniqueName("async-service");
        string requestTopic = await _fixture.CreateNamedTopicAsync(
            $"{serviceName}_EventRequest");
        string responseTopic = await _fixture.CreateNamedTopicAsync(
            $"{serviceName}_EventRequestResponse");

        Guid foreignId = Guid.NewGuid();
        var projection = new KafkaAsyncProjection
        {
            id = Guid.NewGuid(),
            externalId = foreignId
        };
        DateTime pointInTime = DateTime.UtcNow.AddMinutes(-1);
        Event first = CreateEvent(foreignId, "first", pointInTime.AddMinutes(-2));
        Event second = CreateEvent(foreignId, "second", pointInTime.AddMinutes(-1));

        using var responder = new KafkaSyntheticResponder(_fixture, requestTopic);
        await responder.StartAsync();
        // The Confluent consumer API blocks while polling, so run the synthetic
        // responder independently before invoking the requester.
        Task<AsyncEventRequest> responseTask = Task.Run(() =>
            responder.RespondOnceAsync(request =>
            [
                // Noise on the real response topic proves requester-side correlation filtering.
                new AsyncEventRequestResponse
                {
                    topic = request.topic,
                    correlationId = "unrelated-correlation-id",
                    events = [CreateEvent(foreignId, "noise", pointInTime)],
                    complete = true
                },
                new AsyncEventRequestResponse
                {
                    topic = request.topic,
                    correlationId = request.correlationId,
                    events = [first],
                    complete = false
                },
                new AsyncEventRequestResponse
                {
                    topic = request.topic,
                    correlationId = request.correlationId,
                    events = [second],
                    complete = true
                }
            ]));

        using Nostify nostify = CreateNostify();
        var factory = new ExternalDataEventFactory<KafkaAsyncProjection>(
            nostify,
            [projection],
            pointInTime: pointInTime);
        factory.WithAsyncEventRequestor(serviceName, item => item.externalId);

        List<ExternalDataEvent> result = await factory.GetEventsAsync();
        AsyncEventRequest capturedRequest = await responseTask;

        Assert.Equal(requestTopic, capturedRequest.topic);
        Assert.Equal(responseTopic, capturedRequest.responseTopic);
        Assert.Equal(string.Empty, capturedRequest.subtopic);
        Assert.Equal(pointInTime, capturedRequest.pointInTime);
        Assert.Equal([foreignId], capturedRequest.aggregateRootIds);
        Assert.False(string.IsNullOrWhiteSpace(capturedRequest.correlationId));

        ExternalDataEvent mapped = Assert.Single(result);
        Assert.Equal(projection.id, mapped.aggregateRootId);
        Assert.Equal([first.id, second.id], mapped.events.Select(item => item.id));
    }

    [Fact]
    public async Task Multiple_async_requestors_receive_and_map_independent_live_responses()
    {
        string firstService = _fixture.UniqueName("async-multi-first");
        string secondService = _fixture.UniqueName("async-multi-second");
        string firstRequestTopic = await _fixture.CreateNamedTopicAsync(
            $"{firstService}_EventRequest");
        await _fixture.CreateNamedTopicAsync(
            $"{firstService}_EventRequestResponse");
        string secondRequestTopic = await _fixture.CreateNamedTopicAsync(
            $"{secondService}_EventRequest");
        await _fixture.CreateNamedTopicAsync(
            $"{secondService}_EventRequestResponse");

        Guid firstForeignId = Guid.NewGuid();
        Guid secondForeignId = Guid.NewGuid();
        Event firstEvent = CreateEvent(firstForeignId, "multi-first", DateTime.UtcNow);
        Event secondEvent = CreateEvent(secondForeignId, "multi-second", DateTime.UtcNow);
        using var firstResponder = new KafkaSyntheticResponder(_fixture, firstRequestTopic);
        using var secondResponder = new KafkaSyntheticResponder(_fixture, secondRequestTopic);
        await firstResponder.StartAsync();
        await secondResponder.StartAsync();

        Task<AsyncEventRequest> firstResponseTask = Task.Run(() =>
            firstResponder.RespondOnceAsync(request =>
            [
                new AsyncEventRequestResponse
                {
                    topic = request.topic,
                    correlationId = request.correlationId,
                    events = [firstEvent],
                    complete = true
                }
            ]));
        Task<AsyncEventRequest> secondResponseTask = Task.Run(() =>
            secondResponder.RespondOnceAsync(request =>
            [
                new AsyncEventRequestResponse
                {
                    topic = request.topic,
                    correlationId = request.correlationId,
                    events = [secondEvent],
                    complete = true
                }
            ]));

        using Nostify nostify = CreateNostify();
        var projection = new KafkaAsyncProjection
        {
            id = Guid.NewGuid(),
            externalId = firstForeignId,
            secondaryExternalId = secondForeignId
        };
        var factory = new ExternalDataEventFactory<KafkaAsyncProjection>(
            nostify,
            [projection]);
        factory.WithAsyncEventRequestor(firstService, item => item.externalId);
        factory.WithAsyncEventRequestor(secondService, item => item.secondaryExternalId);

        List<ExternalDataEvent> result = await factory.GetEventsAsync();
        AsyncEventRequest[] requests = await Task.WhenAll(
            firstResponseTask,
            secondResponseTask);

        Assert.Equal([firstRequestTopic, secondRequestTopic], requests.Select(request => request.topic));
        Assert.Equal(
            [firstEvent.id, secondEvent.id],
            result.SelectMany(item => item.events).Select(item => item.id));
    }

    [Fact]
    public async Task Async_request_without_a_response_returns_empty_after_configured_timeout()
    {
        string serviceName = _fixture.UniqueName("async-timeout-service");
        string requestTopic = await _fixture.CreateNamedTopicAsync(
            $"{serviceName}_EventRequest");
        await _fixture.CreateNamedTopicAsync(
            $"{serviceName}_EventRequestResponse");

        using var responder = new KafkaSyntheticResponder(_fixture, requestTopic);
        await responder.StartAsync();
        Task<AsyncEventRequest> capturedRequestTask = Task.Run(() =>
            responder.RespondOnceAsync(_ => Array.Empty<AsyncEventRequestResponse>()));

        string? originalTimeout = Environment.GetEnvironmentVariable(
            "AsyncEventRequestTimeoutSeconds");
        Environment.SetEnvironmentVariable("AsyncEventRequestTimeoutSeconds", "1");
        try
        {
            using Nostify nostify = CreateNostify();
            var projection = new KafkaAsyncProjection
            {
                id = Guid.NewGuid(),
                externalId = Guid.NewGuid()
            };
            var factory = new ExternalDataEventFactory<KafkaAsyncProjection>(
                nostify,
                [projection]);
            factory.WithAsyncEventRequestor(serviceName, item => item.externalId);

            DateTime started = DateTime.UtcNow;
            List<ExternalDataEvent> result = await factory.GetEventsAsync();
            TimeSpan elapsed = DateTime.UtcNow - started;
            AsyncEventRequest capturedRequest = await capturedRequestTask;

            Assert.Empty(result);
            Assert.Equal([projection.externalId], capturedRequest.aggregateRootIds);
            Assert.InRange(elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "AsyncEventRequestTimeoutSeconds",
                originalTimeout);
        }
    }

    [Fact]
    public async Task Immediate_responses_are_not_lost_during_repeated_latest_offset_assignments()
    {
        string serviceName = _fixture.UniqueName("async-assignment-race");
        string requestTopic = await _fixture.CreateNamedTopicAsync(
            $"{serviceName}_EventRequest");
        await _fixture.CreateNamedTopicAsync(
            $"{serviceName}_EventRequestResponse");

        using var responder = new KafkaSyntheticResponder(_fixture, requestTopic);
        await responder.StartAsync();

        // Each iteration creates a new production consumer configured with
        // AutoOffsetReset.Latest, then receives an immediate response. Repetition
        // makes assignment-before-publish regressions observable without sleeps.
        for (int iteration = 0; iteration < 10; iteration++)
        {
            Guid foreignId = Guid.NewGuid();
            Event expected = CreateEvent(
                foreignId,
                $"assignment-{iteration}",
                DateTime.UtcNow);
            Task<AsyncEventRequest> responseTask = Task.Run(() =>
                responder.RespondOnceAsync(request =>
                [
                    new AsyncEventRequestResponse
                    {
                        topic = request.topic,
                        correlationId = request.correlationId,
                        events = [expected],
                        complete = true
                    }
                ]));

            using Nostify nostify = CreateNostify();
            var projection = new KafkaAsyncProjection
            {
                id = Guid.NewGuid(),
                externalId = foreignId
            };
            var factory = new ExternalDataEventFactory<KafkaAsyncProjection>(
                nostify,
                [projection]);
            factory.WithAsyncEventRequestor(serviceName, item => item.externalId);

            List<ExternalDataEvent> result = await factory.GetEventsAsync();
            await responseTask;

            ExternalDataEvent mapped = Assert.Single(result);
            Assert.Equal(expected.id, Assert.Single(mapped.events).id);
        }
    }

    private Nostify CreateNostify()
    {
        // Build requires Cosmos configuration, but this Kafka-only path must never connect to it.
        return (Nostify)NostifyFactory
            .WithCosmos(
                "integration-placeholder-key",
                "nostify-integration-tests",
                "https://localhost:8081")
            .WithKafka(_fixture.Settings.KafkaBootstrapServers)
            .Build();
    }

    private static Event CreateEvent(Guid aggregateRootId, string marker, DateTime timestamp)
    {
        var result = new Event(
            new RuntimeEventType("KafkaAsyncIntegrationEvent"),
            aggregateRootId,
            new { marker },
            Guid.NewGuid(),
            Guid.NewGuid())
        {
            timestamp = timestamp
        };
        return result;
    }

    private sealed class RuntimeEventType : EventType
    {
        public RuntimeEventType(string name)
            : base(name)
        {
        }
    }

    private sealed class KafkaAsyncProjection : NostifyObject, IProjection
    {
        public static string containerName => "KafkaAsyncIntegrationProjections";

        public bool initialized { get; set; }

        public Guid externalId { get; set; }

        public Guid secondaryExternalId { get; set; }
    }
}

/// <summary>Consumes one real request and publishes caller-controlled response chunks.</summary>
internal sealed class KafkaSyntheticResponder : IDisposable
{
    private readonly KafkaIntegrationFixture _fixture;
    private readonly string _requestTopic;
    private readonly IConsumer<string, string> _consumer;
    private readonly IProducer<string, string> _producer;

    public KafkaSyntheticResponder(KafkaIntegrationFixture fixture, string requestTopic)
    {
        _fixture = fixture;
        _requestTopic = requestTopic;
        _consumer = fixture.CreateConsumer(fixture.UniqueName("synthetic-responder"));
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = fixture.Settings.KafkaBootstrapServers
        }).Build();
    }

    /// <summary>Subscribes the responder before the system under test publishes its request.</summary>
    public async Task StartAsync()
    {
        _consumer.Subscribe(_requestTopic);
        await _fixture.WaitForAssignmentAsync(_consumer, _requestTopic);
    }

    /// <summary>Consumes one request and emits each response in sequence.</summary>
    public async Task<AsyncEventRequest> RespondOnceAsync(
        Func<AsyncEventRequest, IReadOnlyList<AsyncEventRequestResponse>> createResponses)
    {
        ConsumeResult<string, string> consumed = _fixture.ConsumeOne(_consumer, _requestTopic);
        AsyncEventRequest request = JsonConvert.DeserializeObject<AsyncEventRequest>(consumed.Message.Value)
            ?? throw new InvalidOperationException(
                $"Kafka request topic '{_requestTopic}' contained an invalid async request.");

        foreach (AsyncEventRequestResponse response in createResponses(request))
        {
            await _producer.ProduceAsync(
                request.responseTopic,
                new Message<string, string>
                {
                    Value = JsonConvert.SerializeObject(response)
                });
        }

        _producer.Flush(_fixture.Settings.OperationTimeout);
        return request;
    }

    public void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
        _producer.Dispose();
    }
}
