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
        ///Gets an instance of CosmosClient
        ///</summary>        
        CosmosClient GetClient(bool allowBulk = false);

        ///<summary>
        ///Returns database reference. If allowBulk is true, will return bulk database reference. Single database reference is created for each type of database for the lifetime of the application.
        ///Uses default throughput for database.
        ///</summary>
        ///<returns>Database reference</returns>
        ///<param name="allowBulk">If true, will return bulk database reference</param>
        Task<Database> GetDatabaseAsync(bool allowBulk = false);

        ///<summary>
        ///Returns database reference. If allowBulk is true, will return bulk database reference. Single database reference is created for each type of database for the lifetime of the application.
        ///Uses default throughput for database.
        ///</summary>
        ///<returns>Database reference</returns>
        ///<param name="allowBulk">If true, will return bulk database reference</param>
        ///<param name="throughput">Throughput for database</param>
        Task<Database> GetDatabaseAsync(bool allowBulk, int throughput);

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
        public readonly int DefaultDbThroughput = 4000;

        ///<summary>
        ///Optional. Default throughput for cosmos db when creating new containers
        ///</summary>
        public readonly int DefaultContainerThroughput = 4000;

        ///<summary>
        ///Non-bulk database reference for lower latency
        ///</summary>
        private Database? _database { get; set; } = null;

        ///<summary>
        ///Bulk database reference for higher throughput
        ///</summary>
        private Database? _bulkDatabase { get; set; } = null;

        ///<summary>
        ///Cached CosmosClient instance
        ///</summary>
        private CosmosClient? _cosmosClient { get; set; } = null;

        ///<summary>
        ///Cached bulk enabled CosmosClient instance
        ///</summary>
        private CosmosClient? _bulkCosmosClient { get; set; } = null;

        ///<summary>
        ///List of containers already known to exist so we don't have to check again
        ///</summary>
        public List<string> knownContainers { get; set; } = new List<string>();

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
            int DefaultContainerThroughput = 4000,
            int DefaultDbThroughput = 4000)
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
            InitAsync();
        }

        private async Task InitAsync()
        {            
            //Init bulk and normal database clients for use throughout lifetime of application
            GetDatabaseAsync();
            GetDatabaseAsync(true);
        }

        /// <inheritdoc />
        public CosmosClient GetClient(bool allowBulk = false)
        {
            if (_cosmosClient == null && !allowBulk)
            {
                SocketsHttpHandler handler = new SocketsHttpHandler();
                handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
                var options = new CosmosClientOptions() { 
                    AllowBulkExecution = allowBulk,
                    HttpClientFactory = () => new HttpClient(handler, disposeHandler: false)
                };
                _cosmosClient = new CosmosClient(EndpointUri, Primarykey, options);
            } else if (_bulkCosmosClient == null && allowBulk)
            {
                SocketsHttpHandler handler = new SocketsHttpHandler();
                handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
                var options = new CosmosClientOptions() { 
                    AllowBulkExecution = allowBulk,
                    HttpClientFactory = () => new HttpClient(handler, disposeHandler: false)
                };
                _bulkCosmosClient = new CosmosClient(EndpointUri, Primarykey, options);
            }
            return allowBulk ? _bulkCosmosClient : _cosmosClient;
        } 

        public async Task<Database> GetDatabaseAsync(bool allowBulk = false)
        {
            return await GetDatabaseAsync(allowBulk, this.DefaultDbThroughput);
        }

        /// <inheritdoc />
        public async Task<Database> GetDatabaseAsync(bool allowBulk, int throughput)
        {
            if (!allowBulk && _database == null)
            {
                _database = (await GetClient(allowBulk).CreateDatabaseIfNotExistsAsync(DbName, throughput)).Database;
            }
            if (allowBulk && _bulkDatabase == null)
            {
                _bulkDatabase = (await GetClient(allowBulk).CreateDatabaseIfNotExistsAsync(DbName, throughput)).Database;
            }
            return allowBulk ? _bulkDatabase : _database;
        }

    }
}