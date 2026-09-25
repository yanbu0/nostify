using Microsoft.Extensions.Configuration;

namespace nostify.IntegrationTests;

/// <summary>
/// Loads external dependency settings for explicitly selected integration tests.
/// </summary>
internal sealed class IntegrationTestSettings
{
    /// <summary>The environment and JSON key used to select the Kafka broker.</summary>
    public const string KafkaBootstrapServersKey = "KAFKA_UNIT_TESTING_BOOTSTRAP_SERVERS";

    private IntegrationTestSettings(string bootstrapServers, TimeSpan operationTimeout, int topicPartitions)
    {
        KafkaBootstrapServers = bootstrapServers;
        OperationTimeout = operationTimeout;
        TopicPartitions = topicPartitions;
    }

    /// <summary>Gets the configured Kafka bootstrap servers.</summary>
    public string KafkaBootstrapServers { get; }

    /// <summary>Gets the maximum duration of one broker operation.</summary>
    public TimeSpan OperationTimeout { get; }

    /// <summary>Gets the default partition count for temporary topics.</summary>
    public int TopicPartitions { get; }

    /// <summary>
    /// Loads tracked settings, optional developer settings, and finally environment variables.
    /// </summary>
    public static IntegrationTestSettings Load(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("local.appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        string bootstrapServers = configuration[KafkaBootstrapServersKey] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException(
                $"Integration setting '{KafkaBootstrapServersKey}' must identify at least one Kafka broker.");
        }

        int timeoutSeconds = configuration.GetValue("IntegrationTesting:Kafka:OperationTimeoutSeconds", 15);
        int topicPartitions = configuration.GetValue("IntegrationTesting:Kafka:TopicPartitions", 2);
        if (timeoutSeconds <= 0 || topicPartitions <= 0)
        {
            throw new InvalidOperationException("Kafka timeout and partition settings must be positive integers.");
        }

        return new IntegrationTestSettings(
            bootstrapServers,
            TimeSpan.FromSeconds(timeoutSeconds),
            topicPartitions);
    }
}
