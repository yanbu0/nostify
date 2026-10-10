using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace nostify.IntegrationTests;

/// <summary>
/// Owns an isolated Cosmos database used to verify queries through the real SDK provider.
/// </summary>
public sealed class CosmosIntegrationFixture : IAsyncLifetime
{
    private const string EndpointKey = "IntegrationTesting:Cosmos:Endpoint";
    private const string KeyKey = "IntegrationTesting:Cosmos:Key";
    private const string DatabaseNameKey = "IntegrationTesting:Cosmos:DatabaseName";

    // This public, well-known key is accepted only by the local Cosmos DB Emulator.
    private const string EmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private CosmosClient? _client;

    /// <summary>Gets the isolated database created for this test run.</summary>
    public Database Database { get; private set; } = null!;

    /// <summary>Connects to Cosmos and creates a run-unique database.</summary>
    public async Task InitializeAsync()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("local.appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        string endpoint = configuration[EndpointKey]
            ?? throw new InvalidOperationException($"Integration setting '{EndpointKey}' is required.");
        string configuredDatabaseName = configuration[DatabaseNameKey]
            ?? throw new InvalidOperationException($"Integration setting '{DatabaseNameKey}' is required.");
        string key = configuration[KeyKey] ?? EmulatorKey;
        string databaseName = $"{configuredDatabaseName}-{Guid.NewGuid():N}";

        var options = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway
        };

        // The local emulator uses a development certificate. Real environments should supply
        // a valid certificate and can override the endpoint/key through environment variables.
        if (endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            options.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        }

        _client = new CosmosClient(endpoint, key, options);
        try
        {
            Database = (await _client.CreateDatabaseAsync(databaseName)).Database;
        }
        catch (Exception exception)
        {
            _client.Dispose();
            _client = null;
            throw new InvalidOperationException(
                $"Cosmos integration tests were explicitly selected, but endpoint '{endpoint}' was unavailable. " +
                "Start the Cosmos DB Emulator or override the IntegrationTesting__Cosmos settings.",
                exception);
        }
    }

    /// <summary>Deletes the isolated database and releases the Cosmos client.</summary>
    public async Task DisposeAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (Database is not null)
            {
                await Database.DeleteAsync();
            }
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }
}

/// <summary>Serializes tests that share the Cosmos emulator fixture.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CosmosIntegrationCollection : ICollectionFixture<CosmosIntegrationFixture>
{
    /// <summary>The xUnit collection name.</summary>
    public const string Name = "Cosmos integration";
}
