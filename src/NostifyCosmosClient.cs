using System;
using System.Threading.Tasks;
using System.Configuration;
using System.Collections.Generic;
using System.Net;
using Microsoft.Azure.Cosmos;
using System.Linq;
using System.Net.Http;

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
    public class NostifyCosmosClient : INostifyCosmosClient
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
        public readonly bool UseGatewayConnection = false;

        ///<summary>
        ///Non-bulk database reference for lower latency
        ///</summary>
        private DatabaseRef? _database { get; set; } = null;

        ///<summary>
        ///Bulk database reference for higher throughput
        ///</summary>
        private DatabaseRef? _bulkDatabase { get; set; } = null;

        ///<summary>
        ///Cached CosmosClient instance
        ///</summary>
        private CosmosClient? _cosmosClient { get; set; } = null;

        ///<summary>
        ///Cached bulk enabled CosmosClient instance
        ///</summary>
        private CosmosClient? _bulkCosmosClient { get; set; } = null;

        ///<summary>
        ///Parameterless constructor for mock testing
        ///</summary>
        public NostifyCosmosClient()
        {
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
            bool UseGatewayConnection = false)
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
            _ = InitAsync();
        }

        /// <inheritdoc />
        public bool IsLocalEmulator => ConnectionString != null && ConnectionString.Contains("localhost");

        private async Task InitAsync()
        {            
            //Init bulk and normal database clients for use throughout lifetime of application
            GetDatabaseAsync();
            GetDatabaseAsync(true);
        }

        /// <inheritdoc />
        public CosmosClient GetClient(bool allowBulk = false, bool useGatewayConnection = false)
        {
            SocketsHttpHandler handler = new SocketsHttpHandler();
            handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
            var options = new CosmosClientOptions() { 
                    AllowBulkExecution = allowBulk,
                    ConnectionMode = useGatewayConnection ? ConnectionMode.Gateway : ConnectionMode.Direct,
                    HttpClientFactory = () => new HttpClient(handler, disposeHandler: false)
                };

            if (_cosmosClient == null && !allowBulk)
            {         
                _cosmosClient = new CosmosClient(EndpointUri, Primarykey, options);
            } else if (_bulkCosmosClient == null && allowBulk)
            {
                _bulkCosmosClient = new CosmosClient(EndpointUri, Primarykey, options);
            }
            return allowBulk ? _bulkCosmosClient : _cosmosClient;
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
            return allowBulk ? _bulkDatabase : _database;
        }

        /// <inheritdoc />
        public async Task<Container> GetContainerAsync(string containerName, string partitionKeyPath, bool allowBulk = false, int? throughput = null, bool verbose = false)
        {
            var db = await GetDatabaseAsync(allowBulk);
            //Check to see if container already exists in known containers list and skip check if it does but if not create it if needed and add to list
            Container container;
            if (db.knownContainers.Any(c => c == containerName))
            { 
                if (verbose) Console.WriteLine($"Container {containerName} already exists in known containers list");
                container = db.database.GetContainer(containerName);
                db.AddContainer(containerName);
            }
            else 
            {
                if (verbose) Console.WriteLine($"Creating container {containerName}");

                ContainerProperties containerProperties = new() {
                    Id = containerName,
                    PartitionKeyPath = partitionKeyPath,
                    DefaultTimeToLive = -1
                }; 
                //Check if throughput is set, if not use default, if throughput is 0 or less assume serverless
                var tp = throughput.HasValue ? throughput.Value : DefaultContainerThroughput;
                if (tp <= 0)
                {
                    container = await db.database.CreateContainerIfNotExistsAsync(containerProperties);
                }
                else
                {
                    var throughputValue = ThroughputProperties.CreateAutoscaleThroughput(tp);
                    container = await db.database.CreateContainerIfNotExistsAsync(containerProperties, tp);
                }
                
                db.AddContainer(containerName);
                
                if (verbose) Console.WriteLine($"Created container {containerName}");
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
    public Database database { get; set; }

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