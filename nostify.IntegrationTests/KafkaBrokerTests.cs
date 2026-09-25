using Confluent.Kafka;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace nostify.IntegrationTests;

/// <summary>Exercises Nostify behavior against a real, explicitly configured Kafka broker.</summary>
[Collection(KafkaIntegrationCollection.Name)]
[Trait(IntegrationTraits.Category, IntegrationTraits.Integration)]
[Trait(IntegrationTraits.Dependency, IntegrationTraits.Kafka)]
public sealed class KafkaBrokerTests
{
    private readonly KafkaIntegrationFixture _fixture;

    /// <summary>Creates broker tests backed by the shared Kafka fixture.</summary>
    public KafkaBrokerTests(KafkaIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Broker_metadata_reports_at_least_one_reachable_broker()
    {
        Assert.NotEmpty(_fixture.Metadata.Brokers);
        Assert.All(_fixture.Metadata.Brokers, broker => Assert.True(broker.BrokerId >= 0));
    }

    [Fact]
    public async Task PublishEventAsync_routes_an_event_and_preserves_its_payload()
    {
        string topic = await _fixture.CreateTopicAsync("publish-single");
        string group = _fixture.UniqueName("publish-single-group");
        using IConsumer<string, string> consumer = _fixture.CreateConsumer(group);
        consumer.Subscribe(topic);
        await _fixture.WaitForAssignmentAsync(consumer, topic);

        using Nostify nostify = CreateNostify();
        Guid aggregateId = Guid.NewGuid();
        var expected = new Event(
            new RuntimeEventType(topic),
            aggregateId,
            new { name = "broker-round-trip", count = 3 },
            Guid.NewGuid(),
            Guid.NewGuid());

        await nostify.PublishEventAsync(expected);
        ConsumeResult<string, string> consumed = _fixture.ConsumeOne(consumer, topic);
        var actual = JsonConvert.DeserializeObject<Event>(consumed.Message.Value);

        Assert.NotNull(actual);
        Assert.Equal(expected.id, actual.id);
        Assert.Equal(aggregateId, actual.aggregateRootId);
        Assert.Equal(topic, actual.eventType.name);
        JObject payload = Assert.IsType<JObject>(actual.payload);
        Assert.Equal("broker-round-trip", payload.Value<string>("name"));
        Assert.Equal(3, payload.Value<int>("count"));
        Assert.False(consumed.IsPartitionEOF);
    }

    [Fact]
    public async Task PublishEventAsync_publishes_multiple_events_to_their_distinct_topics()
    {
        string firstTopic = await _fixture.CreateTopicAsync("publish-list-a");
        string secondTopic = await _fixture.CreateTopicAsync("publish-list-b");
        using IConsumer<string, string> firstConsumer = _fixture.CreateConsumer(_fixture.UniqueName("list-a-group"));
        using IConsumer<string, string> secondConsumer = _fixture.CreateConsumer(_fixture.UniqueName("list-b-group"));
        firstConsumer.Subscribe(firstTopic);
        secondConsumer.Subscribe(secondTopic);
        await _fixture.WaitForAssignmentAsync(firstConsumer, firstTopic);
        await _fixture.WaitForAssignmentAsync(secondConsumer, secondTopic);

        Event first = CreateEvent(firstTopic, "first");
        Event second = CreateEvent(secondTopic, "second");
        using Nostify nostify = CreateNostify();

        await nostify.PublishEventAsync([first, second]);

        Event firstResult = JsonConvert.DeserializeObject<Event>(_fixture.ConsumeOne(firstConsumer, firstTopic).Message.Value)!;
        Event secondResult = JsonConvert.DeserializeObject<Event>(_fixture.ConsumeOne(secondConsumer, secondTopic).Message.Value)!;
        Assert.Equal(first.id, firstResult.id);
        Assert.Equal(second.id, secondResult.id);
        Assert.Equal(firstTopic, firstResult.eventType.name);
        Assert.Equal(secondTopic, secondResult.eventType.name);
    }

    [Fact]
    public async Task Nostify_consumers_cache_by_group_and_dedicated_groups_each_receive_the_record()
    {
        string topic = await _fixture.CreateTopicAsync("consumer-groups");
        using Nostify nostify = CreateNostify();
        string firstGroup = _fixture.UniqueName("consumer-group-a");
        string secondGroup = _fixture.UniqueName("consumer-group-b");

        IConsumer<string, string> cached = nostify.GetOrCreateKafkaConsumer(firstGroup);
        Assert.Same(cached, nostify.GetOrCreateKafkaConsumer(firstGroup));
        using IConsumer<string, string> dedicated = nostify.CreateKafkaConsumer(secondGroup);
        cached.Subscribe(topic);
        dedicated.Subscribe(topic);
        await _fixture.WaitForAssignmentAsync(cached, topic);
        await _fixture.WaitForAssignmentAsync(dedicated, topic);

        Event expected = CreateEvent(topic, "groups");
        await nostify.PublishEventAsync(expected);

        Assert.Equal(expected.id, Deserialize(_fixture.ConsumeOne(cached, topic)).id);
        Assert.Equal(expected.id, Deserialize(_fixture.ConsumeOne(dedicated, topic)).id);
    }

    [Fact]
    public async Task Consumers_in_the_same_group_partition_records_without_duplication()
    {
        string topic = await _fixture.CreateTopicAsync("consumer-shared-group", partitions: 2);
        string group = _fixture.UniqueName("consumer-shared-group");
        using Nostify nostify = CreateNostify();
        using IConsumer<string, string> first = nostify.CreateKafkaConsumer(group);
        using IConsumer<string, string> second = nostify.CreateKafkaConsumer(group);
        first.Subscribe(topic);
        second.Subscribe(topic);
        await _fixture.WaitForGroupAssignmentsAsync(topic, first, second);

        Event[] expected =
        [
            CreateEvent(topic, "shared-1"),
            CreateEvent(topic, "shared-2"),
            CreateEvent(topic, "shared-3"),
            CreateEvent(topic, "shared-4")
        ];
        await nostify.PublishEventAsync([.. expected]);

        IReadOnlyList<ConsumeResult<string, string>> consumed =
            _fixture.ConsumeAcrossGroup(topic, expected.Length, first, second);
        Guid[] actualIds = consumed.Select(result => Deserialize(result).id).ToArray();

        Assert.Equal(expected.Select(item => item.id).Order(), actualIds.Order());
        Assert.Equal(actualIds.Length, actualIds.Distinct().Count());
        Assert.Empty(first.Assignment.Intersect(second.Assignment));
    }

    [Fact]
    public async Task Disposing_nostify_closes_cached_consumers_but_not_dedicated_consumers()
    {
        string topic = await _fixture.CreateTopicAsync("consumer-disposal");
        string cachedGroup = _fixture.UniqueName("consumer-disposal-cached");
        string dedicatedGroup = _fixture.UniqueName("consumer-disposal-dedicated");
        Nostify nostify = CreateNostify();
        IConsumer<string, string> cached = nostify.GetOrCreateKafkaConsumer(cachedGroup);
        using IConsumer<string, string> dedicated = nostify.CreateKafkaConsumer(dedicatedGroup);
        dedicated.Subscribe(topic);
        await _fixture.WaitForAssignmentAsync(dedicated, topic);

        nostify.Dispose();
        nostify.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cached.Subscribe(topic));

        // Dedicated consumers are caller-owned and remain usable after the Nostify instance is disposed.
        using Nostify publisher = CreateNostify();
        Event expected = CreateEvent(topic, "dedicated-survives-owner-disposal");
        await publisher.PublishEventAsync(expected);
        Assert.Equal(expected.id, Deserialize(_fixture.ConsumeOne(dedicated, topic)).id);
    }

    private Nostify CreateNostify()
    {
        // Cosmos is configured because Build validates all dependencies, but no Kafka-only test contacts it.
        return (Nostify)NostifyFactory
            .WithCosmos("integration-placeholder-key", "nostify-integration-tests", "https://localhost:8081")
            .WithKafka(_fixture.Settings.KafkaBootstrapServers)
            .Build();
    }

    private static Event CreateEvent(string topic, string marker)
    {
        return new Event(
            new RuntimeEventType(topic),
            Guid.NewGuid(),
            new { marker },
            Guid.NewGuid(),
            Guid.NewGuid());
    }

    private static Event Deserialize(ConsumeResult<string, string> result)
    {
        return JsonConvert.DeserializeObject<Event>(result.Message.Value)
            ?? throw new InvalidOperationException("Kafka record did not contain a Nostify event.");
    }

    private sealed class RuntimeEventType : EventType
    {
        public RuntimeEventType(string name)
            : base(name)
        {
        }
    }
}
