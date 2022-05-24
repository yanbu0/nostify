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
    ///Class to use Cosmos as the repository for persisted events
    ///</summary>
    public class NostifyCosmosClient
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
        ///Optional. Name of event store, defaults to "persistedEvents"
        ///</summary>
        public readonly string PersistedEventsContainer;

        ///<summary>
        ///Optional. Name of undelivered events container, defaults to "undeliverableEvents"
        ///</summary>
        public readonly string UndeliverableEvents;

        ///<summary>
        ///Optional. Name of container holding current state, defaults to "currentState"
        ///</summary>
        public readonly string CurrentStateContainer;

        ///<summary>
        ///Optional. Will default to "AccountEndpoint={this.EndpointUri}/;AccountKey={this.Primarykey};"
        ///</summary>
        public readonly string ConnectionString;

        ///<summary>
        ///Constructor for cosmos client
        ///</summary>
        public NostifyCosmosClient(string ApiKey, 
            string DbName, 
            string EndpointUri = "", 
            string ConnectionString = "", 
            string PersistedEventsContainer = "persistedEvents", 
            string CurrentStateContainer = "currentState",
            string UndeliverableEvents = "undeliverableEvents")
        {
            this.EndpointUri = EndpointUri;
            this.Primarykey = ApiKey;
            this.DbName = DbName;
            this.ConnectionString = (ConnectionString == "") ? $"AccountEndpoint={this.EndpointUri}/;AccountKey={this.Primarykey};" : ConnectionString;
            this.PersistedEventsContainer = PersistedEventsContainer;
            this.UndeliverableEvents = UndeliverableEvents;
            this.CurrentStateContainer = CurrentStateContainer;
        }

        ///<summary>
        ///Gets an instance of CosmosClient
        ///</summary>
        public CosmosClient GetClient(bool allowBulk = false) => new CosmosClient(EndpointUri, Primarykey, new CosmosClientOptions() { AllowBulkExecution = allowBulk });

        ///<summary>
        ///Returns database
        ///</summary>
        public async Task<Database> GetDatabaseAsync(bool allowBulk = false)
        {
            return (await GetClient(allowBulk).CreateDatabaseIfNotExistsAsync(DbName)).Database;
        }

        ///<summary>
        ///Returns event store container
        ///</summary>
        public async Task<Container> GetEventStoreAsync()
        {
            return (await GetClient().CreateDatabaseIfNotExistsAsync(DbName)).Database.GetContainer(this.PersistedEventsContainer);
        }

    }
}