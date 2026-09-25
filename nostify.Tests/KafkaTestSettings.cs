using Microsoft.Extensions.Configuration;

namespace nostify.Tests;

/// <summary>
/// Loads the Kafka bootstrap servers used by tests that connect to a real broker.
/// </summary>
public static class KafkaTestSettings
{
    /// <summary>
    /// The configuration and environment-variable name for the Kafka test broker.
    /// </summary>
    public const string BootstrapServersKey = "KAFKA_UNIT_TESTING_BOOTSTRAP_SERVERS";

    private static readonly Lazy<string> ConfiguredBootstrapServers = new(
        () => LoadBootstrapServers(AppContext.BaseDirectory));

    /// <summary>
    /// Gets the configured Kafka bootstrap servers. Environment variables override
    /// local settings, and local settings override the tracked default settings.
    /// </summary>
    public static string BootstrapServers => ConfiguredBootstrapServers.Value;

    /// <summary>
    /// Loads the layered Kafka test configuration from the supplied directory.
    /// </summary>
    /// <param name="basePath">Directory containing the test settings files.</param>
    /// <param name="includeEnvironmentVariables">
    /// Whether environment variables should be applied as the highest-precedence source.
    /// </param>
    /// <returns>The non-empty Kafka bootstrap-server address.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the bootstrap-server setting is absent or blank.
    /// </exception>
    internal static string LoadBootstrapServers(
        string basePath,
        bool includeEnvironmentVariables = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("local.appsettings.json", optional: true, reloadOnChange: false);

        if (includeEnvironmentVariables)
        {
            builder.AddEnvironmentVariables();
        }

        var bootstrapServers = builder.Build()[BootstrapServersKey];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException(
                $"Test setting '{BootstrapServersKey}' must contain at least one Kafka bootstrap server.");
        }

        return bootstrapServers;
    }
}
