using System;
using System.Threading.Tasks;
using System.Configuration;
using System.Collections.Generic;
using System.Net;
using Microsoft.Azure.Cosmos;
using System.Linq;

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
        ///Returns database
        ///</summary>
        Task<Database> GetDatabaseAsync(bool allowBulk = false);
        ///<summary>
        ///Returns event store container
        ///</summary>
        Task<Container> GetEventStoreAsync();

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
            string UndeliverableEvents = "undeliverableEvents")
        {
            this.EndpointUri = EndpointUri;
            this.Primarykey = ApiKey;
            this.DbName = DbName;
            this.ConnectionString = (ConnectionString == "") ? $"AccountEndpoint={this.EndpointUri}/;AccountKey={this.Primarykey};" : ConnectionString;
            this.EventStorePartitionKey = EventStorePartitionKey;
            this.EventStoreContainer = EventStoreContainer;
            this.UndeliverableEvents = UndeliverableEvents;
        }

        /// <inheritdoc />
        public CosmosClient GetClient(bool allowBulk = false) => new CosmosClient(EndpointUri, Primarykey, new CosmosClientOptions() { AllowBulkExecution = allowBulk });

        /// <inheritdoc />
        public async Task<Database> GetDatabaseAsync(bool allowBulk = false)
        {
            return (await GetClient(allowBulk).CreateDatabaseIfNotExistsAsync(DbName)).Database;
        }

        /// <inheritdoc />
        public async Task<Container> GetEventStoreAsync()
        {
            return await (await GetDatabaseAsync()).CreateContainerIfNotExistsAsync(this.EventStoreContainer, EventStorePartitionKey);
        }

    }
}