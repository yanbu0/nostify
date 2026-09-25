using Confluent.Kafka;
using nostify.IntegrationTestModels;

namespace nostify.IntegrationTests;

/// <summary>Validates production startup topic creation against a real Kafka admin API.</summary>
[Collection(KafkaIntegrationCollection.Name)]
[Trait(IntegrationTraits.Category, IntegrationTraits.Integration)]
[Trait(IntegrationTraits.Dependency, IntegrationTraits.Kafka)]
public sealed class KafkaTopicDiscoveryTests
{
    private static readonly string[] ExpectedTopics =
    [
        "Create_NostifyIntegrationDiscovery",
        "Update_NostifyIntegrationDiscovery",
        "NostifyIntegrationDiscovery_EventRequest",
        "NostifyIntegrationDiscovery_EventRequestResponse"
    ];

    private readonly KafkaIntegrationFixture _fixture;

    /// <summary>Creates topic-discovery tests backed by the shared Kafka fixture.</summary>
    public KafkaTopicDiscoveryTests(KafkaIntegrationFixture fixture)
    {
        _fixture = fixture;
        foreach (string topic in ExpectedTopics)
        {
            _fixture.TrackTopic(topic);
        }
    }

    [Fact]
    public void Generic_build_creates_expected_topics_and_is_idempotent()
    {
        NostifyConfig firstConfig = CreateConfig();
        using Nostify first = (Nostify)firstConfig.Build<KafkaDiscoveryAggregate>();

        foreach (string topic in ExpectedTopics)
        {
            var metadata = _fixture.GetTopicMetadata(topic);
            Assert.Equal(ErrorCode.NoError, metadata.Error.Code);
            Assert.Equal(3, metadata.Partitions.Count);
        }

        // A second production startup must treat all existing topics as a no-op.
        NostifyConfig secondConfig = CreateConfig();
        using Nostify second = (Nostify)secondConfig.Build<KafkaDiscoveryAggregate>();

        Assert.All(ExpectedTopics, topic =>
            Assert.Equal(3, _fixture.GetTopicMetadata(topic).Partitions.Count));
    }

    private NostifyConfig CreateConfig()
    {
        return NostifyFactory
            .WithCosmos("integration-placeholder-key", "nostify-integration-tests", "https://localhost:8081")
            .WithKafka(_fixture.Settings.KafkaBootstrapServers, kafkaTopicAutoCreatePartitions: 3)
            .WithAsyncEventRequest();
    }
}
