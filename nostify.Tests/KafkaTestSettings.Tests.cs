using System.Text.Json;

namespace nostify.Tests;

public sealed class KafkaTestSettingsTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        $"nostify-kafka-settings-{Guid.NewGuid():N}");

    public KafkaTestSettingsTests()
    {
        Directory.CreateDirectory(_settingsDirectory);
    }

    [Fact]
    public void LoadBootstrapServers_WithoutLocalSettings_ReturnsTrackedDefault()
    {
        // Arrange
        WriteSettings("appsettings.json", "localhost:9092");

        // Act
        var result = KafkaTestSettings.LoadBootstrapServers(
            _settingsDirectory,
            includeEnvironmentVariables: false);

        // Assert
        Assert.Equal("localhost:9092", result);
    }

    [Fact]
    public void LoadBootstrapServers_WithLocalSettings_ReturnsLocalOverride()
    {
        // Arrange
        WriteSettings("appsettings.json", "localhost:9092");
        WriteSettings("local.appsettings.json", "localhost:54162");

        // Act
        var result = KafkaTestSettings.LoadBootstrapServers(
            _settingsDirectory,
            includeEnvironmentVariables: false);

        // Assert
        Assert.Equal("localhost:54162", result);
    }

    [Fact]
    public void LoadBootstrapServers_WithBlankSetting_ThrowsClearException()
    {
        // Arrange
        WriteSettings("appsettings.json", "   ");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            KafkaTestSettings.LoadBootstrapServers(
                _settingsDirectory,
                includeEnvironmentVariables: false));

        // Assert
        Assert.Contains(KafkaTestSettings.BootstrapServersKey, exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        // Remove isolated settings created by each test, even when an assertion fails.
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }

    private void WriteSettings(string fileName, string bootstrapServers)
    {
        var settings = new Dictionary<string, string>
        {
            [KafkaTestSettings.BootstrapServersKey] = bootstrapServers
        };

        File.WriteAllText(
            Path.Combine(_settingsDirectory, fileName),
            JsonSerializer.Serialize(settings));
    }
}
