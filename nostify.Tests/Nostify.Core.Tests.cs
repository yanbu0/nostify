using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using nostify;
using Confluent.Kafka;
using Moq;
using Newtonsoft.Json;

namespace nostify.Tests;

public class NostifyTests
{
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;

    public NostifyTests()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
    }

    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var primaryKey = "test-primary-key";
        var dbName = "test-db";
        var cosmosEndpointUri = "https://test-cosmos.documents.azure.com";
        var kafkaUrl = "localhost:9092";
        var partitionKeyPath = "/customTenant";
        var tenantId = Guid.NewGuid();

        // Act
        var nostify = new Nostify(
            primaryKey,
            dbName,
            cosmosEndpointUri,
            kafkaUrl,
            _mockHttpClientFactory.Object,
            partitionKeyPath,
            tenantId
        );

        // Assert
        Assert.NotNull(nostify.Repository);
        Assert.Equal(partitionKeyPath, nostify.DefaultPartitionKeyPath);
        Assert.Equal(tenantId, nostify.DefaultTenantId);
        Assert.Equal(kafkaUrl, nostify.KafkaUrl);
        Assert.NotNull(nostify.KafkaProducer);
        Assert.Equal(_mockHttpClientFactory.Object, nostify.HttpClientFactory);
        Assert.NotNull(nostify.ProjectionInitializer);
        Assert.IsType<ProjectionInitializer>(nostify.ProjectionInitializer);
    }

    [Fact]
    public void Constructor_WithDefaults_ShouldUseDefaultValues()
    {
        // Arrange
        var primaryKey = "test-primary-key";
        var dbName = "test-db";
        var cosmosEndpointUri = "https://test-cosmos.documents.azure.com";
        var kafkaUrl = "localhost:9092";

        // Act
        var nostify = new Nostify(
            primaryKey,
            dbName,
            cosmosEndpointUri,
            kafkaUrl,
            _mockHttpClientFactory.Object
        );

        // Assert
        Assert.Equal("/tenantId", nostify.DefaultPartitionKeyPath);
        Assert.Equal(Guid.Empty, nostify.DefaultTenantId);
    }

    [Fact]
    public void Constructor_ShouldCreateKafkaProducer()
    {
        // Arrange
        var primaryKey = "test-primary-key";
        var dbName = "test-db";
        var cosmosEndpointUri = "https://test-cosmos.documents.azure.com";
        var kafkaUrl = "localhost:9092";

        // Act
        var nostify = new Nostify(
            primaryKey,
            dbName,
            cosmosEndpointUri,
            kafkaUrl,
            _mockHttpClientFactory.Object
        );

        // Assert
        Assert.NotNull(nostify.KafkaProducer);
        Assert.IsAssignableFrom<IProducer<string, string>>(nostify.KafkaProducer);
    }

    [Fact]
    public void Constructor_ShouldCreateRepository()
    {
        // Arrange
        var primaryKey = "test-primary-key";
        var dbName = "test-db";
        var cosmosEndpointUri = "https://test-cosmos.documents.azure.com";
        var kafkaUrl = "localhost:9092";

        // Act
        var nostify = new Nostify(
            primaryKey,
            dbName,
            cosmosEndpointUri,
            kafkaUrl,
            _mockHttpClientFactory.Object
        );

        // Assert
        Assert.NotNull(nostify.Repository);
        Assert.IsType<NostifyCosmosClient>(nostify.Repository);
    }

    [Fact]
    public async Task PublishEventAsync_WithNullEventList_ShouldNotThrow()
    {
        // Arrange
        var nostify = CreateTestNostify();
        List<IEvent>? nullEvents = null;

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => nostify.PublishEventAsync(nullEvents!));
        
        Assert.Null(exception);
    }

    [Fact]
    public async Task PublishEventAsync_WithEmptyEventList_ShouldNotThrow()
    {
        // Arrange
        var nostify = CreateTestNostify();
        var emptyEvents = new List<IEvent>();

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => nostify.PublishEventAsync(emptyEvents));
        
        Assert.Null(exception);
    }

    [Fact]
    public void ProjectionInitializer_ShouldBeInitialized()
    {
        // Arrange
        var nostify = CreateTestNostify();

        // Act & Assert
        Assert.NotNull(nostify.ProjectionInitializer);
        Assert.IsType<ProjectionInitializer>(nostify.ProjectionInitializer);
    }

    [Fact]
    public void Properties_ShouldReturnCorrectValues()
    {
        // Arrange
        var primaryKey = "test-primary-key";
        var dbName = "test-database";
        var cosmosEndpointUri = "https://test-cosmos.documents.azure.com";
        var kafkaUrl = "test-kafka:9092";
        var partitionKeyPath = "/customTenant";
        var tenantId = Guid.NewGuid();

        // Act
        var nostify = new Nostify(
            primaryKey,
            dbName,
            cosmosEndpointUri,
            kafkaUrl,
            _mockHttpClientFactory.Object,
            partitionKeyPath,
            tenantId
        );

        // Assert
        Assert.NotNull(nostify.Repository);
        Assert.Equal(partitionKeyPath, nostify.DefaultPartitionKeyPath);
        Assert.Equal(tenantId, nostify.DefaultTenantId);
        Assert.Equal(kafkaUrl, nostify.KafkaUrl);
        Assert.NotNull(nostify.KafkaProducer);
        Assert.Equal(_mockHttpClientFactory.Object, nostify.HttpClientFactory);
    }

    [Theory]
    [InlineData("/tenantId")]
    [InlineData("/customPartition")]
    [InlineData("/userId")]
    public void Constructor_ShouldAcceptDifferentPartitionKeyPaths(string partitionKeyPath)
    {
        // Arrange & Act
        var nostify = new Nostify(
            "test-key",
            "test-db",
            "https://test.documents.azure.com",
            "localhost:9092",
            _mockHttpClientFactory.Object,
            partitionKeyPath
        );

        // Assert
        Assert.Equal(partitionKeyPath, nostify.DefaultPartitionKeyPath);
    }

    [Fact]
    public void Constructor_WithDifferentTenantIds_ShouldSetCorrectly()
    {
        // Arrange
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();

        // Act
        var nostify1 = new Nostify(
            "test-key",
            "test-db",
            "https://test.documents.azure.com",
            "localhost:9092",
            _mockHttpClientFactory.Object,
            "/tenantId",
            tenantId1
        );

        var nostify2 = new Nostify(
            "test-key",
            "test-db",
            "https://test.documents.azure.com",
            "localhost:9092",
            _mockHttpClientFactory.Object,
            "/tenantId",
            tenantId2
        );

        // Assert
        Assert.Equal(tenantId1, nostify1.DefaultTenantId);
        Assert.Equal(tenantId2, nostify2.DefaultTenantId);
        Assert.NotEqual(nostify1.DefaultTenantId, nostify2.DefaultTenantId);
    }

    // This is broken
    // [Fact]
    // public async Task PublishEventAsync_WithStringInput_ShouldDeserializeCorrectly()
    // {
    //     // Arrange
    //     var testEvents = new List<Event>
    //     {
    //         new Event(new TestCommand(), Guid.NewGuid(), new { test = "data1" }),
    //         new Event(new TestCommand(), Guid.NewGuid(), new { test = "data2" })
    //     };
    //     var jsonInput = JsonConvert.SerializeObject(testEvents);
    //     var nostify = CreateTestNostify();

    //     // Act - should not throw even though Kafka might not be available in test
    //     var exception = await Record.ExceptionAsync(() => nostify.PublishEventAsync(jsonInput));

    //     // Assert - for integration testing we just ensure it doesn't crash on deserialization
    //     // The actual Kafka publishing would require integration test setup
    //     Assert.True(exception == null || exception is ProduceException<string, string>);
    // }

    [Fact]
    public async Task PublishEventAsync_WithMalformedJson_ShouldThrowJsonException()
    {
        // Arrange
        var malformedJson = "{ invalid json";
        var nostify = CreateTestNostify();

        // Act & Assert
        await Assert.ThrowsAsync<JsonReaderException>(() => nostify.PublishEventAsync(malformedJson));
    }

    [Fact]
    public void KafkaProducer_ShouldBeProperlyConfigured()
    {
        // Arrange
        var nostify = CreateTestNostify();

        // Act & Assert
        Assert.NotNull(nostify.KafkaProducer);
        
        // Verify it's a real producer instance (not a mock)
        var producerType = nostify.KafkaProducer.GetType();
        Assert.Contains("Producer", producerType.Name);
    }

    private Nostify CreateTestNostify()
    {
        return new Nostify(
            "test-primary-key",
            "test-db",
            "test-cosmos-url",
            "localhost:9092",
            _mockHttpClientFactory.Object,
            "/tenantId",
            Guid.NewGuid()
        );
    }
}

// Test helper classes
public class TestCommand : NostifyCommand
{
    public TestCommand() : base("TestCommand") { }
}
