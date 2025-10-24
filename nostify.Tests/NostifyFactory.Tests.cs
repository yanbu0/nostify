using System;
using System.Linq;
using System.Net.Http;
using Xunit;
using nostify;
using Confluent.Kafka;
using Moq;

namespace nostify.Tests;

public class NostifyFactoryTests
{
    private static string? GetProducerConfigValue(NostifyConfig config, string key)
    {
        return config.producerConfig
            .LastOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static bool? GetProducerConfigBoolValue(NostifyConfig config, string key)
    {
        var value = GetProducerConfigValue(config, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var result) ? result : null;
    }

    [Fact]
    public void WithCosmos_ShouldCreateConfigWithCosmosSettings()
    {
        // Arrange
        var cosmosApiKey = "test-api-key";
        var cosmosDbName = "test-db";
        var cosmosEndpointUri = "https://test.documents.azure.com:443/";

        // Act
        var config = NostifyFactory.WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(cosmosApiKey, config.cosmosApiKey);
        Assert.Equal(cosmosDbName, config.cosmosDbName);
        Assert.Equal(cosmosEndpointUri, config.cosmosEndpointUri);
        Assert.False(config.createContainers);
        Assert.Null(config.containerThroughput);
        Assert.False(config.useGatewayConnection);
    }

    [Fact]
    public void WithCosmos_WithOptionalParameters_ShouldSetAllProperties()
    {
        // Arrange
        var cosmosApiKey = "test-api-key";
        var cosmosDbName = "test-db";
        var cosmosEndpointUri = "https://test.documents.azure.com:443/";
        var createContainers = true;
        var containerThroughput = 1000;
        var useGatewayConnection = true;

        // Act
        var config = NostifyFactory.WithCosmos(
            cosmosApiKey, 
            cosmosDbName, 
            cosmosEndpointUri, 
            createContainers, 
            containerThroughput, 
            useGatewayConnection);

        // Assert
        Assert.Equal(cosmosApiKey, config.cosmosApiKey);
        Assert.Equal(cosmosDbName, config.cosmosDbName);
        Assert.Equal(cosmosEndpointUri, config.cosmosEndpointUri);
        Assert.True(config.createContainers);
        Assert.Equal(containerThroughput, config.containerThroughput);
        Assert.True(config.useGatewayConnection);
    }

    [Fact]
    public void WithCosmos_ExtensionMethod_ShouldChainCorrectly()
    {
        // Arrange
        var existingConfig = new NostifyConfig();
        var cosmosApiKey = "test-api-key";
        var cosmosDbName = "test-db";
        var cosmosEndpointUri = "https://test.documents.azure.com:443/";

        // Act
        var config = existingConfig.WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri);

        // Assert
        Assert.Same(existingConfig, config);
        Assert.Equal(cosmosApiKey, config.cosmosApiKey);
        Assert.Equal(cosmosDbName, config.cosmosDbName);
        Assert.Equal(cosmosEndpointUri, config.cosmosEndpointUri);
    }

    [Fact]
    public void WithKafka_WithProducerConfig_ShouldSetProducerConfig()
    {
        // Arrange
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = "localhost:9092",
            ClientId = "test-client"
        };

        // Act
        var config = NostifyFactory.WithKafka(producerConfig);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("localhost:9092", GetProducerConfigValue(config, "bootstrap.servers"));
        Assert.Equal("test-client", GetProducerConfigValue(config, "client.id"));
    }

    [Fact]
    public void WithKafka_WithUrlOnly_ShouldConfigureBasicSettings()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(kafkaUrl, config.kafkaUrl);
        Assert.Null(config.kafkaUserName);
        Assert.Null(config.kafkaPassword);
        Assert.Equal(kafkaUrl, GetProducerConfigValue(config, "bootstrap.servers"));
        Assert.True(GetProducerConfigBoolValue(config, "allow.auto.create.topics").GetValueOrDefault());
        Assert.Contains("Nostify-", GetProducerConfigValue(config, "client.id"));
    }

    [Fact]
    public void WithKafka_WithCredentials_ShouldConfigureSaslSettings()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";
        var userName = "test-user";
        var password = "test-password";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl, userName, password);

        // Assert
        Assert.Equal(kafkaUrl, config.kafkaUrl);
        Assert.Equal(userName, config.kafkaUserName);
        Assert.Equal(password, config.kafkaPassword);
        Assert.Equal(userName, GetProducerConfigValue(config, "sasl.username"));
        Assert.Equal(password, GetProducerConfigValue(config, "sasl.password"));
        Assert.Equal("SASL_SSL", GetProducerConfigValue(config, "security.protocol"));
        Assert.Equal("PLAIN", GetProducerConfigValue(config, "sasl.mechanism"));
    Assert.True(GetProducerConfigBoolValue(config, "api.version.request").GetValueOrDefault());
    }

    [Fact]
    public void WithKafka_WithoutCredentials_ShouldNotSetSaslSettings()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl);

        // Assert
        Assert.Null(GetProducerConfigValue(config, "sasl.username"));
        Assert.Null(GetProducerConfigValue(config, "sasl.password"));
        Assert.Null(GetProducerConfigValue(config, "security.protocol"));
        Assert.Null(GetProducerConfigValue(config, "sasl.mechanism"));
    }

    [Fact]
    public void WithKafka_ExtensionMethod_ShouldChainCorrectly()
    {
        // Arrange
        var existingConfig = new NostifyConfig();
        var kafkaUrl = "localhost:9092";

        // Act
        var config = existingConfig.WithKafka(kafkaUrl);

        // Assert
        Assert.Same(existingConfig, config);
        Assert.Equal(kafkaUrl, config.kafkaUrl);
    }

    [Fact]
    public void WithHttp_ShouldSetHttpClientFactory()
    {
        // Arrange
        var config = new NostifyConfig();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();

        // Act
        var result = config.WithHttp(mockHttpClientFactory.Object);

        // Assert
        Assert.Same(config, result);
        Assert.Equal(mockHttpClientFactory.Object, config.httpClientFactory);
    }

    [Fact]
    public void FluentApi_ShouldChainMethodsCorrectly()
    {
        // Arrange
        var cosmosApiKey = "test-api-key";
        var cosmosDbName = "test-db";
        var cosmosEndpointUri = "https://test.documents.azure.com:443/";
        var kafkaUrl = "localhost:9092";
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();

        // Act
        var config = NostifyFactory
            .WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri, true, 1000)
            .WithKafka(kafkaUrl, "user", "pass")
            .WithHttp(mockHttpClientFactory.Object);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(cosmosApiKey, config.cosmosApiKey);
        Assert.Equal(cosmosDbName, config.cosmosDbName);
        Assert.Equal(cosmosEndpointUri, config.cosmosEndpointUri);
        Assert.True(config.createContainers);
        Assert.Equal(1000, config.containerThroughput);
        Assert.Equal(kafkaUrl, config.kafkaUrl);
        Assert.Equal("user", config.kafkaUserName);
        Assert.Equal("pass", config.kafkaPassword);
        Assert.Equal(mockHttpClientFactory.Object, config.httpClientFactory);
    }

    [Fact]
    public void Build_ShouldCreateNostifyInstance()
    {
        // Arrange
        var config = NostifyFactory
            .WithCosmos("test-key", "test-db", "https://test.documents.azure.com:443/")
            .WithKafka("localhost:9092")
            .WithHttp(new Mock<IHttpClientFactory>().Object);

        // Act
        var nostify = config.Build();

        // Assert
        Assert.NotNull(nostify);
        Assert.IsAssignableFrom<INostify>(nostify);
        Assert.NotNull(nostify.Repository);
        Assert.NotNull(nostify.KafkaProducer);
        Assert.Equal("localhost:9092", nostify.KafkaUrl);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithCosmos_CreateContainers_ShouldHandleNullableBoolean(bool? createContainers)
    {
        // Arrange
        var cosmosApiKey = "test-api-key";
        var cosmosDbName = "test-db";
        var cosmosEndpointUri = "https://test.documents.azure.com:443/";

        // Act
        var config = NostifyFactory.WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri, createContainers);

        // Assert
        Assert.Equal(createContainers ?? false, config.createContainers);
    }

    [Fact]
    public void WithCosmos_NullCreateContainers_ShouldDefaultToFalse()
    {
        // Arrange
        var cosmosApiKey = "test-api-key";
        var cosmosDbName = "test-db";
        var cosmosEndpointUri = "https://test.documents.azure.com:443/";

        // Act
        var config = NostifyFactory.WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri, null);

        // Assert
        Assert.False(config.createContainers);
    }

    [Fact]
    public void NostifyConfig_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var config = new NostifyConfig();

        // Assert
        Assert.Null(config.cosmosApiKey);
        Assert.Null(config.cosmosDbName);
        Assert.Null(config.cosmosEndpointUri);
        Assert.Null(config.kafkaUrl);
        Assert.Null(config.kafkaUserName);
        Assert.Null(config.kafkaPassword);
        Assert.Null(config.defaultPartitionKeyPath);
        Assert.Equal(Guid.Empty, config.defaultTenantId);
        Assert.NotNull(config.producerConfig);
        Assert.False(config.createContainers);
        Assert.Null(config.containerThroughput);
        Assert.False(config.useGatewayConnection);
        Assert.Null(config.httpClientFactory);
    }

    [Fact]
    public void WithKafka_GeneratedClientId_ShouldBeUnique()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";
        var dbName = "test-db";

        // Act
        var config1 = NostifyFactory.WithCosmos("key", dbName, "uri").WithKafka(kafkaUrl);
        var config2 = NostifyFactory.WithCosmos("key", dbName, "uri").WithKafka(kafkaUrl);

        // Assert
    var clientId1 = GetProducerConfigValue(config1, "client.id");
    var clientId2 = GetProducerConfigValue(config2, "client.id");
    Assert.NotEqual(clientId1, clientId2);
    Assert.Contains($"Nostify-{dbName}-", clientId1);
    Assert.Contains($"Nostify-{dbName}-", clientId2);
    }

    [Fact]
    public void WithKafka_ShouldSetAllowAutoCreateTopics()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl);

        // Assert
    Assert.True(GetProducerConfigBoolValue(config, "allow.auto.create.topics").GetValueOrDefault());
    }

    [Fact]
    public void Build_WithoutHttpClientFactory_ShouldStillWork()
    {
        // Arrange
        var config = NostifyFactory
            .WithCosmos("test-key", "test-db", "https://test.documents.azure.com:443/")
            .WithKafka("localhost:9092");

        // Act
        var nostify = config.Build();

        // Assert
        Assert.NotNull(nostify);
        Assert.Null(nostify.HttpClientFactory);
    }

    [Fact]
    public void BuildGeneric_ConfigurationOnly_ShouldPrepareCorrectly()
    {
        // Arrange
        var config = NostifyFactory
            .WithCosmos("test-key", "test-db", "https://test.documents.azure.com:443/")
            .WithKafka("localhost:9092")
            .WithHttp(new Mock<IHttpClientFactory>().Object);

        // Act & Assert - Just verify the configuration is correct without calling Build<T>
        // which would require external Kafka connection
        Assert.NotNull(config);
        Assert.Equal("test-key", config.cosmosApiKey);
        Assert.Equal("test-db", config.cosmosDbName);
        Assert.Equal("https://test.documents.azure.com:443/", config.cosmosEndpointUri);
        Assert.Equal("localhost:9092", config.kafkaUrl);
        Assert.NotNull(config.httpClientFactory);
    Assert.NotNull(config.producerConfig);
    Assert.True(GetProducerConfigBoolValue(config, "allow.auto.create.topics").GetValueOrDefault());
    }

    [Fact]
    public void WithKafka_WithEmptyCredentials_ShouldNotSetSaslSettings()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl, "", "");

        // Assert
        Assert.Equal("", config.kafkaUserName);
        Assert.Equal("", config.kafkaPassword);
    Assert.Null(GetProducerConfigValue(config, "sasl.username"));
    Assert.Null(GetProducerConfigValue(config, "security.protocol"));
    }

    [Fact]
    public void WithKafka_WithWhitespaceCredentials_ShouldNotSetSaslSettings()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl, "   ", "   ");

        // Assert
        Assert.Equal("   ", config.kafkaUserName);
        Assert.Equal("   ", config.kafkaPassword);
    Assert.Null(GetProducerConfigValue(config, "sasl.username"));
    Assert.Null(GetProducerConfigValue(config, "security.protocol"));
    }

    [Fact]
    public void NostifyConfig_ProducerConfig_ShouldNotBeNull()
    {
        // Arrange & Act
        var config = new NostifyConfig();

        // Assert
    Assert.NotNull(config.producerConfig);
    Assert.IsType<List<KeyValuePair<string, string>>>(config.producerConfig);
    }

    [Fact]
    public void WithCosmos_AllOptionalParameters_ShouldSetCorrectly()
    {
        // Arrange
        var cosmosApiKey = "test-api-key";
        var cosmosDbName = "test-db";
        var cosmosEndpointUri = "https://test.documents.azure.com:443/";

        // Act
        var config = NostifyFactory.WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri, 
            createContainers: true, 
            containerThroughput: 500, 
            useGatewayConnection: true);

        // Assert
        Assert.True(config.createContainers);
        Assert.Equal(500, config.containerThroughput);
        Assert.True(config.useGatewayConnection);
    }

    [Fact]
    public void NostifyConfig_DefaultTenantId_ShouldBeEmpty()
    {
        // Act
        var config = new NostifyConfig();

        // Assert
        Assert.Equal(Guid.Empty, config.defaultTenantId);
    }

    [Fact]
    public void WithKafka_ValidCredentials_ShouldSetApiVersionRequest()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";
        var userName = "validuser";
        var password = "validpass";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl, userName, password);

        // Assert
    Assert.True(GetProducerConfigBoolValue(config, "api.version.request").GetValueOrDefault());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void WithKafka_InvalidCredentials_ShouldNotSetSaslConfig(string? credential)
    {
        // Arrange
        var kafkaUrl = "localhost:9092";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl, credential!, credential!);

        // Assert
        Assert.Equal(credential, config.kafkaUserName);
        Assert.Equal(credential, config.kafkaPassword);
    Assert.Null(GetProducerConfigValue(config, "sasl.username"));
    Assert.Null(GetProducerConfigValue(config, "sasl.password"));
    Assert.Null(GetProducerConfigValue(config, "security.protocol"));
    Assert.Null(GetProducerConfigValue(config, "sasl.mechanism"));
    Assert.Null(GetProducerConfigValue(config, "api.version.request"));
    }

    [Fact]
    public void WithEventHubs_ShouldConfigureEventHubsSettings()
    {
        // Arrange
        var connectionString = "Endpoint=sb://test-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123";

        // Act
        var config = NostifyFactory.WithEventHubs(connectionString);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("test-namespace.servicebus.windows.net:9093", config.kafkaUrl);
    Assert.Equal("test-namespace.servicebus.windows.net:9093", GetProducerConfigValue(config, "bootstrap.servers"));
    Assert.Equal("$ConnectionString", GetProducerConfigValue(config, "sasl.username"));
    Assert.Equal(connectionString, GetProducerConfigValue(config, "sasl.password"));
    Assert.Equal("SASL_SSL", GetProducerConfigValue(config, "security.protocol"));
    Assert.Equal("PLAIN", GetProducerConfigValue(config, "sasl.mechanism"));
    Assert.Contains("Nostify-", GetProducerConfigValue(config, "client.id"));
    }

    [Fact]
    public void WithEventHubs_ExtensionMethod_ShouldChainCorrectly()
    {
        // Arrange
        var existingConfig = new NostifyConfig();
        var connectionString = "Endpoint=sb://test-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123";

        // Act
        var config = existingConfig.WithEventHubs(connectionString);

        // Assert
        Assert.Same(existingConfig, config);
        Assert.Equal("test-namespace.servicebus.windows.net:9093", config.kafkaUrl);
    }

    [Fact]
    public void WithEventHubs_GeneratedClientId_ShouldBeUnique()
    {
        // Arrange
        var connectionString = "Endpoint=sb://test-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123";
        var dbName = "test-db";

        // Act
        var config1 = NostifyFactory.WithCosmos("key", dbName, "uri").WithEventHubs(connectionString);
        var config2 = NostifyFactory.WithCosmos("key", dbName, "uri").WithEventHubs(connectionString);

        // Assert
    var clientId1 = GetProducerConfigValue(config1, "client.id");
    var clientId2 = GetProducerConfigValue(config2, "client.id");
    Assert.NotEqual(clientId1, clientId2);
    Assert.Contains($"Nostify-{dbName}-", clientId1);
    Assert.Contains($"Nostify-{dbName}-", clientId2);
    }

    [Fact]
    public void FluentApi_WithEventHubs_ShouldChainCorrectly()
    {
        // Arrange
        var cosmosApiKey = "test-api-key";
        var cosmosDbName = "test-db";
        var cosmosEndpointUri = "https://test.documents.azure.com:443/";
        var eventHubsConnectionString = "Endpoint=sb://test-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123";
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();

        // Act
        var config = NostifyFactory
            .WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri, true, 1000)
            .WithEventHubs(eventHubsConnectionString)
            .WithHttp(mockHttpClientFactory.Object);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(cosmosApiKey, config.cosmosApiKey);
        Assert.Equal(cosmosDbName, config.cosmosDbName);
        Assert.Equal(cosmosEndpointUri, config.cosmosEndpointUri);
        Assert.True(config.createContainers);
        Assert.Equal(1000, config.containerThroughput);
        Assert.Equal("test-namespace.servicebus.windows.net:9093", config.kafkaUrl);
        Assert.Equal(mockHttpClientFactory.Object, config.httpClientFactory);
    }

    [Fact]
    public void Build_WithEventHubs_ShouldCreateNostifyInstance()
    {
        // Arrange
        var connectionString = "Endpoint=sb://test-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123";
        var config = NostifyFactory
            .WithCosmos("test-key", "test-db", "https://test.documents.azure.com:443/")
            .WithEventHubs(connectionString)
            .WithHttp(new Mock<IHttpClientFactory>().Object);

        // Act
        var nostify = config.Build();

        // Assert
        Assert.NotNull(nostify);
        Assert.IsAssignableFrom<INostify>(nostify);
        Assert.NotNull(nostify.Repository);
        Assert.NotNull(nostify.KafkaProducer);
        Assert.Equal("test-namespace.servicebus.windows.net:9093", nostify.KafkaUrl);
    }

    [Fact]
    public void WithEventHubs_ShouldExtractNamespace()
    {
        // Arrange
        var connectionString = "Endpoint=sb://my-eventhubs-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123";

        // Act
        var config = NostifyFactory.WithEventHubs(connectionString);

        // Assert
        Assert.Equal("my-eventhubs-namespace.servicebus.windows.net:9093", config.kafkaUrl);
        Assert.Equal(connectionString, config.producerConfig.SaslPassword);
    }

}


// Test aggregate for generic Build method testing
public class TestFactoryAggregate : NostifyObject, IAggregate
{
    public string Name { get; set; } = "";
    public bool isDeleted { get; set; }
    public static string aggregateType => "TestFactoryAggregate";
    public static string currentStateContainerName => "TestFactoryAggregates";
    
    public override void Apply(IEvent eventToApply)
    {
        UpdateProperties<TestFactoryAggregate>(eventToApply.payload);
    }
}
