using DotNet.Testcontainers.Builders;
using Microsoft.Azure.Cosmos;
using Testcontainers.CosmosDb;

namespace nostify.IntegrationTests;

/// <summary>
/// Starts the Linux Cosmos DB Emulator in Docker and owns an isolated database for one test run.
/// </summary>
public sealed class CosmosDockerIntegrationFixture : IAsyncLifetime
{
    private const string DatabaseNamePrefix = "nostify-docker-integration";
    private const string EmulatorImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview";

    // Pin the image explicitly so emulator upgrades are intentional and reproducible in CI. The
    // preview image reports readiness as "Gateway=OK" rather than the module's legacy "Started".
    private readonly CosmosDbContainer _container = new CosmosDbBuilder(EmulatorImage)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(
            "Gateway=OK",
            options => options.WithTimeout(TimeSpan.FromMinutes(5))))
        .Build();
    private CosmosClient? _client;

    /// <summary>Gets the configured client connected to the Docker emulator.</summary>
    public CosmosClient Client => _client
        ?? throw new InvalidOperationException("The Docker Cosmos client has not been initialized.");

    /// <summary>Gets the isolated database hosted by the Docker emulator.</summary>
    public Database Database { get; private set; } = null!;

    /// <summary>Gets the emulator endpoint advertised through the Testcontainers client.</summary>
    public string Endpoint => Client.Endpoint.ToString().TrimEnd('/');

    /// <summary>Gets the account key from the emulator connection string.</summary>
    public string AccountKey => GetConnectionStringValue("AccountKey");

    /// <summary>
    /// Creates the HTTP client required to route Cosmos SDK requests to the emulator's dynamic port.
    /// </summary>
    public HttpClient CreateHttpClient() => _container.HttpClient;

    /// <summary>Starts the emulator and creates a run-unique database.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            await _container.StartAsync(startupTimeout.Token);

            // The module's handler rewrites the emulator's advertised localhost URI to the
            // random host port and handles the emulator's development certificate.
            var options = new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                HttpClientFactory = () => _container.HttpClient,
                RequestTimeout = TimeSpan.FromSeconds(30),
                // Exercise the same serializer used by NostifyCosmosClient while retaining the
                // Testcontainers HTTP handler required for random-port URI rewriting and TLS.
                Serializer = new NewtonsoftJsonCosmosSerializer()
            };

            _client = new CosmosClient(_container.GetConnectionString(), options);
            string databaseName = $"{DatabaseNamePrefix}-{Guid.NewGuid():N}";
            using var operationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            Database = (await _client.CreateDatabaseAsync(
                databaseName,
                cancellationToken: operationTimeout.Token)).Database;
        }
        catch (Exception exception)
        {
            _client?.Dispose();
            _client = null;
            await _container.DisposeAsync();
            throw new InvalidOperationException(
                "Docker-backed Cosmos integration tests were selected, but the Cosmos emulator " +
                "container could not be started. Ensure Docker is running with Linux containers enabled.",
                exception);
        }
    }

    private string GetConnectionStringValue(string key)
    {
        string prefix = key + "=";
        string? segment = _container.GetConnectionString()
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return segment?[prefix.Length..]
            ?? throw new InvalidOperationException($"The Cosmos emulator connection string does not contain '{key}'.");
    }

    /// <summary>Deletes test data and stops the Docker emulator.</summary>
    public async Task DisposeAsync()
    {
        try
        {
            if (_client is not null && Database is not null)
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await Database.DeleteAsync(cancellationToken: cleanupTimeout.Token);
            }
        }
        finally
        {
            _client?.Dispose();
            _client = null;
            await _container.DisposeAsync();
        }
    }
}

/// <summary>Serializes tests that share the Docker-hosted Cosmos emulator.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CosmosDockerIntegrationCollection : ICollectionFixture<CosmosDockerIntegrationFixture>
{
    /// <summary>The xUnit collection name.</summary>
    public const string Name = "Cosmos Docker integration";
}
