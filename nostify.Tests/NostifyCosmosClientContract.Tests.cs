using System.Reflection;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Verifies deterministic configuration and bookkeeping contracts for
/// <see cref="NostifyCosmosClient"/> without connecting to Cosmos DB.
/// </summary>
public sealed class NostifyCosmosClientContractTests
{
    [Fact]
    public void ConfiguredConstructor_WithDefaults_PreservesConfigurationAndBuildsConnectionString()
    {
        var logger = new Mock<ILogger>();

        using var client = new NostifyCosmosClient(
            ApiKey: "test-key",
            DbName: "test-database",
            EndpointUri: "https://example.documents.azure.com",
            EventStorePartitionKey: "/tenantId",
            EventStoreContainer: "events",
            UndeliverableEvents: "failed-events",
            DefaultContainerThroughput: 4_000,
            DefaultDbThroughput: 8_000,
            UseGatewayConnection: true,
            SagaContainer: "sagas",
            SequenceContainer: "sequences",
            logger: logger.Object);

        Assert.Equal("https://example.documents.azure.com", client.EndpointUri);
        Assert.Equal("test-database", client.DbName);
        Assert.Equal(
            "AccountEndpoint=https://example.documents.azure.com/;AccountKey=test-key;",
            client.ConnectionString);
        Assert.Equal("/tenantId", client.EventStorePartitionKey);
        Assert.Equal("events", client.EventStoreContainer);
        Assert.Equal("failed-events", client.UndeliverableEvents);
        Assert.Equal("sagas", client.SagaContainer);
        Assert.Equal("sequences", client.SequenceContainer);
        Assert.Equal(4_000, client.DefaultContainerThroughput);
        Assert.Equal(8_000, client.DefaultDbThroughput);
        Assert.True(client.UseGatewayConnection);
        Assert.Same(logger.Object, client._logger);
    }

    [Theory]
    [InlineData("AccountEndpoint=https://LOCALHOST:8081/;AccountKey=test;", true)]
    [InlineData("AccountEndpoint=https://example.documents.azure.com/;AccountKey=test;", false)]
    public void IsLocalEmulator_DetectsLocalhostCaseInsensitively(
        string connectionString,
        bool expected)
    {
        using var client = new NostifyCosmosClient(
            ApiKey: "ignored-key",
            DbName: "test-database",
            ConnectionString: connectionString);

        Assert.Equal(expected, client.IsLocalEmulator);
        Assert.Equal(connectionString, client.ConnectionString);
    }

    [Fact]
    public void UnconfiguredConstructor_UsesStableCompatibilityDefaults()
    {
        using var client = new NostifyCosmosClient();

        Assert.Equal(string.Empty, client.EndpointUri);
        Assert.Equal(string.Empty, client.DbName);
        Assert.Equal(string.Empty, client.ConnectionString);
        Assert.Equal("/aggregateRootId", client.EventStorePartitionKey);
        Assert.Equal("eventStore", client.EventStoreContainer);
        Assert.Equal("undeliverableEvents", client.UndeliverableEvents);
        Assert.Equal("sagaContainer", client.SagaContainer);
        Assert.Equal("sequenceContainer", client.SequenceContainer);
        Assert.False(client.IsLocalEmulator);
    }

    [Fact]
    public void AddContainer_RecordsEachExactContainerNameOnlyOnce()
    {
        var databaseRef = new DatabaseRef();

        databaseRef.AddContainer("orders");
        databaseRef.AddContainer("orders");
        databaseRef.AddContainer("Orders");

        // Container IDs are case-sensitive, while an exact duplicate must not
        // cause redundant existence checks during subsequent retrievals.
        Assert.Equal(["orders", "Orders"], databaseRef.knownContainers);
    }

    [Fact]
    public void GetClient_CachesSeparateRegularAndBulkClientsWithRequestedModes()
    {
        using var repository = new NostifyCosmosClient(
            ApiKey: Convert.ToBase64String(new byte[64]),
            DbName: "test-database",
            EndpointUri: "https://example.documents.azure.com");

        CosmosClient regular = repository.GetClient(useGatewayConnection: true);
        CosmosClient cachedRegular = repository.GetClient(useGatewayConnection: false);
        CosmosClient bulk = repository.GetClient(allowBulk: true);
        CosmosClient cachedBulk = repository.GetClient(allowBulk: true, useGatewayConnection: true);

        Assert.Same(regular, cachedRegular);
        Assert.Same(bulk, cachedBulk);
        Assert.NotSame(regular, bulk);
        Assert.False(regular.ClientOptions.AllowBulkExecution);
        Assert.Equal(ConnectionMode.Gateway, regular.ClientOptions.ConnectionMode);
        Assert.True(bulk.ClientOptions.AllowBulkExecution);
        Assert.Equal(ConnectionMode.Direct, bulk.ClientOptions.ConnectionMode);
        Assert.IsType<NewtonsoftJsonCosmosSerializer>(regular.ClientOptions.Serializer);
    }

    [Fact]
    public async Task GetContainerAsync_WhenKnown_ReturnsCachedContainerAndLogsDiagnostic()
    {
        var logger = CreateEnabledLogger();
        using var repository = CreateRepositoryWithCachedDatabase(
            out Mock<Database> database,
            logger.Object,
            knownContainers: ["orders"]);
        var expected = new Mock<Container>();
        database.Setup(value => value.GetContainer("orders")).Returns(expected.Object);

        Container actual = await repository.GetContainerAsync("orders", "/tenantId");

        Assert.Same(expected.Object, actual);
        database.Verify(value => value.GetContainer("orders"), Times.Once);
        database.Verify(
            value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyDebugLog(logger, Times.AtLeastOnce());
    }

    [Fact]
    public async Task GetContainerAsync_WhenUnknownAndServerless_CreatesContainerAndCachesName()
    {
        var logger = CreateEnabledLogger();
        using var repository = CreateRepositoryWithCachedDatabase(
            out Mock<Database> database,
            logger.Object);
        var expected = new Mock<Container>();
        database
            .Setup(value => value.CreateContainerIfNotExistsAsync(
                It.Is<ContainerProperties>(properties =>
                    properties.Id == "orders" &&
                    properties.PartitionKeyPath == "/tenantId" &&
                    properties.DefaultTimeToLive == -1),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockContainerResponse(expected.Object));

        Container actual = await repository.GetContainerAsync("orders", "/tenantId");
        DatabaseRef databaseRef = GetPrivateField<DatabaseRef>(repository, "_database");

        Assert.Same(expected.Object, actual);
        Assert.Equal(["orders"], databaseRef.knownContainers);
        VerifyDebugLog(logger, Times.Exactly(2));
    }

    [Fact]
    public async Task GetContainerAsync_WithPositiveThroughput_PassesThroughputToCosmos()
    {
        using var repository = CreateRepositoryWithCachedDatabase(
            out Mock<Database> database,
            logger: null);
        var expected = new Mock<Container>();
        database
            .Setup(value => value.CreateContainerIfNotExistsAsync(
                It.IsAny<ContainerProperties>(),
                4_000,
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockContainerResponse(expected.Object));

        Container actual = await repository.GetContainerAsync(
            "orders",
            "/tenantId",
            throughput: 4_000);

        Assert.Same(expected.Object, actual);
        database.VerifyAll();
    }

    /// <summary>
    /// Creates a repository whose regular Cosmos client and database are cached mocks,
    /// allowing container bookkeeping to be tested without contacting Cosmos DB.
    /// </summary>
    private static NostifyCosmosClient CreateRepositoryWithCachedDatabase(
        out Mock<Database> database,
        ILogger? logger,
        List<string>? knownContainers = null)
    {
        var repository = new NostifyCosmosClient(
            ApiKey: "test-key",
            DbName: "test-database",
            EndpointUri: "https://example.documents.azure.com",
            logger: logger);
        database = new Mock<Database>();
        SetPrivateField(repository, "_cosmosClient", new Mock<CosmosClient>().Object);
        SetPrivateField(
            repository,
            "_database",
            new DatabaseRef
            {
                database = database.Object,
                knownContainers = knownContainers ?? []
            });
        return repository;
    }

    /// <summary>Creates a mocked response containing the supplied container.</summary>
    private static ContainerResponse MockContainerResponse(Container container)
    {
        var response = new Mock<ContainerResponse>();
        response.Setup(value => value.Container).Returns(container);
        return response.Object;
    }

    /// <summary>Creates an enabled logger suitable for generated logging delegates.</summary>
    private static Mock<ILogger> CreateEnabledLogger()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return logger;
    }

    /// <summary>Verifies generated debug logging through the common ILogger boundary.</summary>
    private static void VerifyDebugLog(Mock<ILogger> logger, Times times)
    {
        logger.Verify(
            value => value.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    /// <summary>Sets private cached state used only to isolate an existing external boundary.</summary>
    private static void SetPrivateField<T>(NostifyCosmosClient repository, string name, T value)
    {
        typeof(NostifyCosmosClient)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(repository, value);
    }

    /// <summary>Reads private cached state to assert externally observable cache behavior.</summary>
    private static T GetPrivateField<T>(NostifyCosmosClient repository, string name)
    {
        return (T)typeof(NostifyCosmosClient)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(repository)!;
    }
}
