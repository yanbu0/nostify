using System;
using System.Threading.Tasks;
using System.Configuration;
using System.Collections.Generic;
using System.Net;
using Microsoft.Azure.Cosmos;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace nostify
{
    ///<summary>
    ///Defines NostifyCosmosClient interface
    ///</summary>
    public interface INostifyCosmosClient
    {
        ///<summary>
        ///Whether or not the client is using the local Cosmos DB emulator
        ///</summary>
        bool IsLocalEmulator { get; }

        ///<summary>
        ///Gets an instance of CosmosClient
        ///</summary>        
        CosmosClient GetClient(bool allowBulk = false, bool useGatewayConnection = false);

        ///<summary>
        ///Returns database reference. If allowBulk is true, will return bulk database reference. Single database reference is created for each type of database for the lifetime of the application.
        ///Uses default throughput for database.
        ///</summary>
        ///<returns>Database reference</returns>
        ///<param name="allowBulk">If true, will return bulk database reference</param>
        Task<DatabaseRef> GetDatabaseAsync(bool allowBulk = false);

        ///<summary>
        ///Returns database reference. If allowBulk is true, will return bulk database reference. Single database reference is created for each type of database for the lifetime of the application.
        ///Uses default throughput for database.
        ///</summary>
        ///<returns>Database reference</returns>
        ///<param name="allowBulk">If true, will return bulk database reference</param>
        ///<param name="throughput">Throughput for database</param>
        Task<DatabaseRef> GetDatabaseAsync(bool allowBulk, int throughput);

        ///<summary>
        ///Returns container reference. If allowBulk is true, will return bulk container reference. Single container reference is created for each type of container for the lifetime of the application.
        ///</summary>
        ///<returns>Container reference</returns>
        ///<param name="containerName">Name of container</param>
        ///<param name="partitionKeyPath">Partition key path</param>
        ///<param name="allowBulk">If true, will return bulk container reference</param>
        ///<param name="throughput">Throughput for container</param>
        ///<param name="verbose">If true, will print verbose output</param>
        Task<Container> GetContainerAsync(string containerName, string partitionKeyPath, bool allowBulk = false, int? throughput = null, bool verbose = false);


    }

    ///<summary>
    ///Class to use Cosmos as the repository for persisted events
    ///</summary>
    public class NostifyCosmosClient : INostifyCosmosClient, IDisposable
    {
        ///<summary>
        ///Optional. Endpoint url for cosmos db, will have format "https://{DbName}.documents.azure.us:443"
        ///</summary>
        public readonly string EndpointUri;

        ///<summary>
        ///API key
        ///</summary>
        private readonly string Primarykey;

        ///<summary>
        ///Name of cosmos database
        ///</summary>
        public readonly string DbName;

        ///<summary>
        ///Optional. Name of event store, defaults to "eventStore"
        ///</summary>
        public readonly string EventStoreContainer;

        ///<summary>
        ///Optional. Will default to "/aggregateRootId"
        ///</summary>
        public readonly string EventStorePartitionKey;

        ///<summary>
        ///Optional. Name of undelivered events container, defaults to "undeliverableEvents"
        ///</summary>
        public readonly string UndeliverableEvents;

        ///<summary>
        /// Optional. Name of saga container, defaults to "sagaContainer"
        /// </summary>
        public readonly string SagaContainer;

        /// <summary>
        /// Optional. Name of sequence container for storing sequential number generators, defaults to "sequenceContainer"
        /// </summary>
        public readonly string SequenceContainer;

        ///<summary>
        ///Optional. Will default to "AccountEndpoint={this.EndpointUri}/;AccountKey={this.Primarykey};"
        ///</summary>
        public readonly string ConnectionString;

        ///<summary>
        ///Optional. Default throughput for cosmos db when creating new databases
        ///</summary>
        public readonly int DefaultDbThroughput = -1;

        ///<summary>
        ///Optional. Default throughput for cosmos db when creating new containers
        ///</summary>
        public readonly int DefaultContainerThroughput = -1;

        ///<summary>
        ///Optional. If true, will use gateway connection mode
        ///</summary>
        public readonly bool UseGatewayConnection;

        ///<summary>
        ///Optional logger instance for structured logging. When set, replaces Console.WriteLine with structured log output.
        ///</summary>
        public readonly ILogger? _logger;

        private static readonly Action<ILogger, string, Exception?> LogKnownContainer =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(1, nameof(LogKnownContainer)),
                "Container {ContainerName} already exists in known containers list");

        private static readonly Action<ILogger, string, Exception?> LogCreatingContainer =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(2, nameof(LogCreatingContainer)),
                "Creating container {ContainerName}");

        private static readonly Action<ILogger, string, Exception?> LogCreatedContainer =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(3, nameof(LogCreatedContainer)),
                "Created container {ContainerName}");

        ///<summary>
        ///Non-bulk database reference for lower latency
        ///</summary>
        private DatabaseRef? _database { get; set; }

        ///<summary>
        ///Bulk database reference for higher throughput
        ///</summary>
        private DatabaseRef? _bulkDatabase { get; set; }

        ///<summary>
        ///Cached CosmosClient instance
        ///</summary>
        private CosmosClient? _cosmosClient { get; set; }

        ///<summary>
        ///Cached bulk enabled CosmosClient instance
        ///</summary>
        private CosmosClient? _bulkCosmosClient { get; set; }

        // Cosmos clients are expensive, thread-safe singletons for this repository instance.
        private readonly object _clientLock = new();
        private bool _disposed;

        ///<summary>
        ///Creates an unconfigured instance for test doubles.
        ///</summary>
        ///<remarks>
        ///Database operations require a configured instance. This constructor remains public for
        ///compatibility with existing test subclasses and mocking frameworks.
        ///</remarks>
        public NostifyCosmosClient()
        {
            EndpointUri = string.Empty;
            Primarykey = string.Empty;
            DbName = string.Empty;
            ConnectionString = string.Empty;
            EventStorePartitionKey = "/aggregateRootId";
            EventStoreContainer = "eventStore";
            UndeliverableEvents = "undeliverableEvents";
            SagaContainer = "sagaContainer";
            SequenceContainer = "sequenceContainer";
        }

        ///<summary>
        ///Constructor for cosmos client
        ///</summary>
        public NostifyCosmosClient(string ApiKey,
            string DbName,
            string EndpointUri = "",
            string ConnectionString = "",
            string EventStorePartitionKey = "/aggregateRootId",
            string EventStoreContainer = "eventStore",
            string UndeliverableEvents = "undeliverableEvents",
            int DefaultContainerThroughput = -1,
            int DefaultDbThroughput = -1,
            bool UseGatewayConnection = false,
            string SagaContainer = "sagaContainer",
            string SequenceContainer = "sequenceContainer",
            ILogger? logger = null)
        {
            this.EndpointUri = EndpointUri;
            this.Primarykey = ApiKey;
            this.DbName = DbName;
            this.ConnectionString = (ConnectionString == "") ? $"AccountEndpoint={this.EndpointUri}/;AccountKey={this.Primarykey};" : ConnectionString;
            this.EventStorePartitionKey = EventStorePartitionKey;
            this.EventStoreContainer = EventStoreContainer;
            this.UndeliverableEvents = UndeliverableEvents;
            this.DefaultContainerThroughput = DefaultContainerThroughput;
            this.DefaultDbThroughput = DefaultDbThroughput;
            this.UseGatewayConnection = UseGatewayConnection;
            this.SagaContainer = SagaContainer;
            this.SequenceContainer = SequenceContainer;
            this._logger = logger;
        }

        /// <inheritdoc />
        public bool IsLocalEmulator =>
            ConnectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public CosmosClient GetClient(bool allowBulk = false, bool useGatewayConnection = false)
        {
            lock (_clientLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                CosmosClient? cachedClient = allowBulk ? _bulkCosmosClient : _cosmosClient;
                if (cachedClient != null)
                {
                    return cachedClient;
                }

                var options = new CosmosClientOptions
                {
                    AllowBulkExecution = allowBulk,
                    ConnectionMode = useGatewayConnection ? ConnectionMode.Gateway : ConnectionMode.Direct,
                    Serializer = new NewtonsoftJsonCosmosSerializer(),
                };
                var client = new CosmosClient(EndpointUri, Primarykey, options);

                if (allowBulk)
                {
                    _bulkCosmosClient = client;
                }
                else
                {
                    _cosmosClient = client;
                }

                return client;
            }
        }

        /// <summary>
        /// Disposes the cached regular and bulk Cosmos clients owned by this repository.
        /// </summary>
        public void Dispose()
        {
            lock (_clientLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _cosmosClient?.Dispose();
                _bulkCosmosClient?.Dispose();
                _cosmosClient = null;
                _bulkCosmosClient = null;
                _database = null;
                _bulkDatabase = null;
            }

            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public async Task<DatabaseRef> GetDatabaseAsync(bool allowBulk = false)
        {
            return await GetDatabaseAsync(allowBulk, this.DefaultDbThroughput);
        }

        /// <inheritdoc />
        public async Task<DatabaseRef> GetDatabaseAsync(bool allowBulk, int throughput)
        {
            var client = GetClient(allowBulk, this.UseGatewayConnection);
            if (!allowBulk && _database == null)
            {
                //Create database if it doesn't exist, if throughput is 0 or less assume serverless
                var db = throughput > 0 ? (await client.CreateDatabaseIfNotExistsAsync(DbName, throughput)).Database
                    : (await client.CreateDatabaseIfNotExistsAsync(DbName)).Database;
                _database = new() { database = db, knownContainers = new() };
            }
            if (allowBulk && _bulkDatabase == null)
            {
                //Create database if it doesn't exist, if throughput is 0 or less assume serverless
                var bulkDb = throughput > 0 ? (await client.CreateDatabaseIfNotExistsAsync(DbName, throughput)).Database
                    : (await client.CreateDatabaseIfNotExistsAsync(DbName)).Database;
                _bulkDatabase = new() { database = bulkDb, knownContainers = new() };
            }
            return allowBulk
                ? _bulkDatabase ?? throw new InvalidOperationException("Bulk database initialization did not complete.")
                : _database ?? throw new InvalidOperationException("Database initialization did not complete.");
        }

        /// <inheritdoc />
        public async Task<Container> GetContainerAsync(string containerName, string partitionKeyPath, bool allowBulk = false, int? throughput = null, bool verbose = false)
        {
            var db = await GetDatabaseAsync(allowBulk);
            Database database = db.database
                ?? throw new InvalidOperationException("The database reference is not initialized.");
            //Check to see if container already exists in known containers list and skip check if it does but if not create it if needed and add to list
            Container container;
            if (db.knownContainers.Any(c => c == containerName))
            {
                if (_logger != null) LogKnownContainer(_logger, containerName, null);
                else if (verbose) Console.WriteLine($"Container {containerName} already exists in known containers list");
                container = database.GetContainer(containerName);
                db.AddContainer(containerName);
            }
            else
            {
                if (_logger != null) LogCreatingContainer(_logger, containerName, null);
                else if (verbose) Console.WriteLine($"Creating container {containerName}");

                ContainerProperties containerProperties = new()
                {
                    Id = containerName,
                    PartitionKeyPath = partitionKeyPath,
                    DefaultTimeToLive = -1
                };
                //Check if throughput is set, if not use default, if throughput is 0 or less assume serverless
                var tp = throughput.HasValue ? throughput.Value : DefaultContainerThroughput;
                if (tp <= 0)
                {
                    container = await database.CreateContainerIfNotExistsAsync(containerProperties);
                }
                else
                {
                    var throughputValue = ThroughputProperties.CreateAutoscaleThroughput(tp);
                    container = await database.CreateContainerIfNotExistsAsync(containerProperties, tp);
                }

                db.AddContainer(containerName);

                if (_logger != null) LogCreatedContainer(_logger, containerName, null);
                else if (verbose) Console.WriteLine($"Created container {containerName}");
            }
            return container;
        }

    }
}

/// <summary>
/// Represents a reference to a database and its known containers.
/// </summary>
public class DatabaseRef
{
    /// <summary>
    /// Gets or sets the database instance.
    /// </summary>
    public Database? database { get; set; }

    /// <summary>
    /// Gets or sets the list of known container names.
    /// </summary>
    public List<string> knownContainers { get; set; } = new List<string>();

    /// <summary>
    /// Adds a container name to the list of known containers if it does not already exist.
    /// </summary>
    /// <param name="containerName">The name of the container to add.</param>
    public void AddContainer(string containerName)
    {
        if (!knownContainers.Contains(containerName))
        {
            knownContainers.Add(containerName);
        }
    }
}