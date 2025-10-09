using System;
using System.Net.Http;
using Xunit;
using nostify;
using Confluent.Kafka;
using Moq;

namespace nostify.Tests;

public class NostifyFactoryTests
{
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
        Assert.Equal(producerConfig, config.producerConfig);
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
        Assert.Equal(kafkaUrl, config.producerConfig.BootstrapServers);
        Assert.True(config.producerConfig.AllowAutoCreateTopics);
        Assert.Contains("Nostify-", config.producerConfig.ClientId);
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
        Assert.Equal(userName, config.producerConfig.SaslUsername);
        Assert.Equal(password, config.producerConfig.SaslPassword);
        Assert.Equal(SecurityProtocol.SaslSsl, config.producerConfig.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, config.producerConfig.SaslMechanism);
        Assert.True(config.producerConfig.ApiVersionRequest);
    }

    [Fact]
    public void WithKafka_WithoutCredentials_ShouldNotSetSaslSettings()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl);

        // Assert
        Assert.Null(config.producerConfig.SaslUsername);
        Assert.Null(config.producerConfig.SaslPassword);
        Assert.Null(config.producerConfig.SecurityProtocol);
        Assert.Null(config.producerConfig.SaslMechanism);
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
        Assert.NotEqual(config1.producerConfig.ClientId, config2.producerConfig.ClientId);
        Assert.Contains($"Nostify-{dbName}-", config1.producerConfig.ClientId);
        Assert.Contains($"Nostify-{dbName}-", config2.producerConfig.ClientId);
    }

    [Fact]
    public void WithKafka_ShouldSetAllowAutoCreateTopics()
    {
        // Arrange
        var kafkaUrl = "localhost:9092";

        // Act
        var config = NostifyFactory.WithKafka(kafkaUrl);

        // Assert
        Assert.True(config.producerConfig.AllowAutoCreateTopics);
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
        Assert.True(config.producerConfig.AllowAutoCreateTopics);
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
        Assert.Null(config.producerConfig.SaslUsername);
        Assert.Null(config.producerConfig.SecurityProtocol);
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
        Assert.Null(config.producerConfig.SaslUsername);
        Assert.Null(config.producerConfig.SecurityProtocol);
    }

    [Fact]
    public void NostifyConfig_ProducerConfig_ShouldNotBeNull()
    {
        // Arrange & Act
        var config = new NostifyConfig();

        // Assert
        Assert.NotNull(config.producerConfig);
        Assert.IsType<ProducerConfig>(config.producerConfig);
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
        Assert.True(config.producerConfig.ApiVersionRequest);
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
        Assert.Null(config.producerConfig.SaslUsername);
        Assert.Null(config.producerConfig.SaslPassword);
        Assert.Null(config.producerConfig.SecurityProtocol);
        Assert.Null(config.producerConfig.SaslMechanism);
        Assert.Null(config.producerConfig.ApiVersionRequest);
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
        Assert.Equal("test-namespace.servicebus.windows.net:9093", config.producerConfig.BootstrapServers);
        Assert.Equal("$ConnectionString", config.producerConfig.SaslUsername);
        Assert.Equal(connectionString, config.producerConfig.SaslPassword);
        Assert.Equal(SecurityProtocol.SaslSsl, config.producerConfig.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, config.producerConfig.SaslMechanism);
        Assert.Contains("Nostify-", config.producerConfig.ClientId);
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
        Assert.NotEqual(config1.producerConfig.ClientId, config2.producerConfig.ClientId);
        Assert.Contains($"Nostify-{dbName}-", config1.producerConfig.ClientId);
        Assert.Contains($"Nostify-{dbName}-", config2.producerConfig.ClientId);
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
        Assert.Equal("my-eventhubs-namespace", config.eventHubsNamespace);
        Assert.Equal(connectionString, config.eventHubsConnectionString);
    }

    [Fact]
    public void WithEventHubsManagement_ShouldSetAzureCredentials()
    {
        // Arrange
        var config = new NostifyConfig();
        var subscriptionId = "sub-123";
        var resourceGroup = "rg-test";
        var tenantId = "tenant-456";
        var clientId = "client-789";
        var clientSecret = "secret-abc";

        // Act
        var result = config.WithEventHubsManagement(subscriptionId, resourceGroup, tenantId, clientId, clientSecret);

        // Assert
        Assert.Same(config, result);
        Assert.Equal(subscriptionId, config.azureSubscriptionId);
        Assert.Equal(resourceGroup, config.azureResourceGroup);
        Assert.Equal(tenantId, config.azureTenantId);
        Assert.Equal(clientId, config.azureClientId);
        Assert.Equal(clientSecret, config.azureClientSecret);
    }

    [Fact]
    public void FluentApi_WithEventHubsAndManagement_ShouldChainCorrectly()
    {
        // Arrange
        var eventHubsConnectionString = "Endpoint=sb://test-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123";

        // Act
        var config = NostifyFactory
            .WithCosmos("key", "db", "https://test.documents.azure.com:443/")
            .WithEventHubs(eventHubsConnectionString)
            .WithEventHubsManagement("sub-123", "rg-test", "tenant-456", "client-789", "secret-abc")
            .WithHttp(new Mock<IHttpClientFactory>().Object);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("test-namespace", config.eventHubsNamespace);
        Assert.Equal("sub-123", config.azureSubscriptionId);
        Assert.Equal("rg-test", config.azureResourceGroup);
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
