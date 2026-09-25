using System.Diagnostics;
using Confluent.Kafka;

namespace nostify.IntegrationTests;

/// <summary>Exercises bounded Kafka failures without disturbing the shared live broker.</summary>
[Collection(KafkaIntegrationCollection.Name)]
[Trait(IntegrationTraits.Category, IntegrationTraits.Integration)]
[Trait(IntegrationTraits.Dependency, IntegrationTraits.Kafka)]
public sealed class KafkaNegativePathTests
{
    private readonly KafkaIntegrationFixture _fixture;

    /// <summary>Creates negative-path tests backed by the initialized Kafka fixture.</summary>
    public KafkaNegativePathTests(KafkaIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Unreachable_bootstrap_server_fails_within_the_configured_bound()
    {
        TimeSpan timeout = TimeSpan.FromSeconds(2);
        using IAdminClient adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            // Port 1 on loopback is intentionally isolated from the configured live broker.
            BootstrapServers = "127.0.0.1:1",
            ClientId = $"nostify-integration-unreachable-{Guid.NewGuid():N}",
            SocketTimeoutMs = checked((int)timeout.TotalMilliseconds)
        }).Build();
        var stopwatch = Stopwatch.StartNew();

        KafkaException exception = Assert.Throws<KafkaException>(() =>
            adminClient.GetMetadata(timeout));

        stopwatch.Stop();
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, timeout + TimeSpan.FromSeconds(3));
        Assert.NotEqual(ErrorCode.NoError, exception.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(exception.Error.Reason));

        // The isolated client must not alter the fixture's verified live endpoint.
        Assert.NotEqual("127.0.0.1:1", _fixture.Settings.KafkaBootstrapServers);
        Assert.NotEmpty(_fixture.Metadata.Brokers);
    }
}
