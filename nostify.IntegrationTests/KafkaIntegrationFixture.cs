using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace nostify.IntegrationTests;

/// <summary>
/// Owns shared Kafka resources and provides bounded, diagnostic broker operations.
/// </summary>
public sealed class KafkaIntegrationFixture : IAsyncLifetime
{
    private readonly ConcurrentBag<string> _topics = [];
    private IAdminClient? _adminClient;

    /// <summary>Gets the integration-test configuration.</summary>
    internal IntegrationTestSettings Settings { get; private set; } = null!;

    /// <summary>Gets metadata captured by the fixture's fail-fast readiness check.</summary>
    public Metadata Metadata { get; private set; } = null!;

    /// <summary>Initializes the broker client and fails explicitly when Kafka is unavailable.</summary>
    public Task InitializeAsync()
    {
        Settings = IntegrationTestSettings.Load(AppContext.BaseDirectory);
        _adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = Settings.KafkaBootstrapServers,
            ClientId = $"nostify-integration-admin-{Guid.NewGuid():N}",
            SocketTimeoutMs = checked((int)Settings.OperationTimeout.TotalMilliseconds)
        }).Build();

        try
        {
            Metadata = _adminClient.GetMetadata(Settings.OperationTimeout);
            if (Metadata.Brokers.Count == 0)
            {
                throw new InvalidOperationException("Kafka returned metadata without any brokers.");
            }
        }
        catch (Exception exception)
        {
            _adminClient.Dispose();
            _adminClient = null;
            throw new InvalidOperationException(
                $"Kafka integration tests were explicitly selected, but broker '{Settings.KafkaBootstrapServers}' " +
                $"was not ready within {Settings.OperationTimeout}. Start Kafka or override " +
                $"'{IntegrationTestSettings.KafkaBootstrapServersKey}'.",
                exception);
        }

        return Task.CompletedTask;
    }

    /// <summary>Deletes temporary topics on a best-effort basis and releases native resources.</summary>
    public async Task DisposeAsync()
    {
        if (_adminClient is not null)
        {
            string[] topics = _topics.Distinct(StringComparer.Ordinal).ToArray();
            if (topics.Length > 0)
            {
                try
                {
                    await _adminClient.DeleteTopicsAsync(
                        topics,
                        new DeleteTopicsOptions { OperationTimeout = Settings.OperationTimeout });
                }
                catch (DeleteTopicsException)
                {
                    // Cleanup must not hide the primary test result; run-unique names prevent collisions.
                }
                catch (KafkaException)
                {
                    // A broker may be shutting down after tests; native resources still need disposal.
                }
            }

            _adminClient.Dispose();
            _adminClient = null;
        }
    }

    /// <summary>Builds a Kafka-safe, run-unique resource name.</summary>
    public string UniqueName(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        string safePurpose = new(purpose
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        return $"nostify-it-{safePurpose}-{Guid.NewGuid():N}";
    }

    /// <summary>Registers a topic created by production code for fixture cleanup.</summary>
    public void TrackTopic(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        _topics.Add(topic);
    }

    /// <summary>Creates a run-unique temporary topic.</summary>
    public Task<string> CreateTopicAsync(string purpose, int? partitions = null)
    {
        return CreateNamedTopicAsync(UniqueName(purpose), partitions);
    }

    /// <summary>
    /// Creates an explicitly named temporary topic and waits until every partition has a leader.
    /// This supports production contracts whose topic names are derived from service names.
    /// </summary>
    public async Task<string> CreateNamedTopicAsync(string topic, int? partitions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        IAdminClient adminClient = _adminClient ?? throw new InvalidOperationException("Fixture is not initialized.");
        int partitionCount = partitions ?? Settings.TopicPartitions;

        await adminClient.CreateTopicsAsync(
            [new TopicSpecification { Name = topic, NumPartitions = partitionCount, ReplicationFactor = 1 }],
            new CreateTopicsOptions { OperationTimeout = Settings.OperationTimeout });
        _topics.Add(topic);

        DateTime deadline = DateTime.UtcNow + Settings.OperationTimeout;
        while (DateTime.UtcNow < deadline)
        {
            Metadata metadata = adminClient.GetMetadata(topic, TimeSpan.FromSeconds(2));
            TopicMetadata? topicMetadata = metadata.Topics.SingleOrDefault(candidate => candidate.Topic == topic);
            if (topicMetadata is not null &&
                topicMetadata.Error.Code == ErrorCode.NoError &&
                topicMetadata.Partitions.Count == partitionCount &&
                topicMetadata.Partitions.All(partition => partition.Leader >= 0))
            {
                return topic;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Kafka topic '{topic}' was not ready with {partitionCount} partitions within {Settings.OperationTimeout}. " +
            $"Bootstrap servers: {Settings.KafkaBootstrapServers}.");
    }

    /// <summary>Creates a consumer configured for isolated integration-test reads.</summary>
    public IConsumer<string, string> CreateConsumer(string groupId, AutoOffsetReset offsetReset = AutoOffsetReset.Earliest)
    {
        return new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Settings.KafkaBootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = offsetReset,
            EnableAutoCommit = false,
            SessionTimeoutMs = 6_000,
            SocketTimeoutMs = checked((int)Settings.OperationTimeout.TotalMilliseconds)
        }).Build();
    }

    /// <summary>Polls until a subscribed consumer has a partition assignment.</summary>
    public async Task WaitForAssignmentAsync(IConsumer<string, string> consumer, string topic)
    {
        DateTime deadline = DateTime.UtcNow + Settings.OperationTimeout;
        while (DateTime.UtcNow < deadline)
        {
            consumer.Consume(TimeSpan.FromMilliseconds(100));
            if (consumer.Assignment.Any(partition => partition.Topic == topic))
            {
                return;
            }

            await Task.Yield();
        }

        throw new TimeoutException(
            $"Consumer was not assigned topic '{topic}' within {Settings.OperationTimeout}. " +
            $"Group subscription: {string.Join(",", consumer.Subscription)}; broker: {Settings.KafkaBootstrapServers}.");
    }

    /// <summary>
    /// Polls all members of one consumer group until each member has an assignment for the topic.
    /// Joint polling is required because adding a group member initiates a rebalance that every member must serve.
    /// </summary>
    public async Task WaitForGroupAssignmentsAsync(
        string topic,
        params IConsumer<string, string>[] consumers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(consumers);
        if (consumers.Length == 0)
        {
            throw new ArgumentException("At least one consumer is required.", nameof(consumers));
        }

        DateTime deadline = DateTime.UtcNow + Settings.OperationTimeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (IConsumer<string, string> consumer in consumers)
            {
                consumer.Consume(TimeSpan.FromMilliseconds(50));
            }

            if (consumers.All(consumer =>
                consumer.Assignment.Any(partition => partition.Topic == topic)))
            {
                return;
            }

            await Task.Yield();
        }

        string assignments = string.Join(
            "; ",
            consumers.Select((consumer, index) =>
                $"member-{index}=[{string.Join(",", consumer.Assignment)}]"));
        throw new TimeoutException(
            $"Not every consumer-group member was assigned topic '{topic}' within {Settings.OperationTimeout}. " +
            $"Assignments: {assignments}; broker: {Settings.KafkaBootstrapServers}.");
    }

    /// <summary>Consumes one record with a bounded timeout and detailed diagnostics.</summary>
    public ConsumeResult<string, string> ConsumeOne(IConsumer<string, string> consumer, string topic)
    {
        DateTime deadline = DateTime.UtcNow + Settings.OperationTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ConsumeResult<string, string>? result = consumer.Consume(TimeSpan.FromMilliseconds(200));
            if (result is not null && result.Topic == topic)
            {
                return result;
            }
        }

        throw new TimeoutException(
            $"No record arrived from topic '{topic}' within {Settings.OperationTimeout}. " +
            $"Assignment: {string.Join(",", consumer.Assignment)}; broker: {Settings.KafkaBootstrapServers}.");
    }

    /// <summary>Consumes an exact number of records across members of a shared consumer group.</summary>
    public IReadOnlyList<ConsumeResult<string, string>> ConsumeAcrossGroup(
        string topic,
        int expectedCount,
        params IConsumer<string, string>[] consumers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedCount);
        ArgumentNullException.ThrowIfNull(consumers);
        if (consumers.Length == 0)
        {
            throw new ArgumentException("At least one consumer is required.", nameof(consumers));
        }

        var results = new List<ConsumeResult<string, string>>(expectedCount);
        DateTime deadline = DateTime.UtcNow + Settings.OperationTimeout;
        while (DateTime.UtcNow < deadline && results.Count < expectedCount)
        {
            foreach (IConsumer<string, string> consumer in consumers)
            {
                ConsumeResult<string, string>? result = consumer.Consume(TimeSpan.FromMilliseconds(50));
                if (result is not null && result.Topic == topic)
                {
                    results.Add(result);
                    if (results.Count == expectedCount)
                    {
                        return results;
                    }
                }
            }
        }

        string assignments = string.Join(
            "; ",
            consumers.Select((consumer, index) =>
                $"member-{index}=[{string.Join(",", consumer.Assignment)}]"));
        throw new TimeoutException(
            $"Received {results.Count} of {expectedCount} records from topic '{topic}' within " +
            $"{Settings.OperationTimeout}. Assignments: {assignments}; broker: {Settings.KafkaBootstrapServers}.");
    }

    /// <summary>Returns current metadata for one topic.</summary>
    public TopicMetadata GetTopicMetadata(string topic)
    {
        IAdminClient adminClient = _adminClient ?? throw new InvalidOperationException("Fixture is not initialized.");
        return adminClient.GetMetadata(topic, Settings.OperationTimeout).Topics.Single(candidate => candidate.Topic == topic);
    }
}

/// <summary>Serializes tests that intentionally share the assembly-scoped broker fixture.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KafkaIntegrationCollection : ICollectionFixture<KafkaIntegrationFixture>
{
    /// <summary>The xUnit collection name.</summary>
    public const string Name = "Kafka integration";
}

/// <summary>Central trait values used to filter external-dependency tests.</summary>
internal static class IntegrationTraits
{
    public const string Category = "Category";
    public const string Integration = "Integration";
    public const string Dependency = "Dependency";
    public const string Kafka = "Kafka";
    public const string Cosmos = "Cosmos";
}
