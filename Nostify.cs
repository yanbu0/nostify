using System;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Linq;
using Newtonsoft.Json;
using Confluent.Kafka;
using System.Net.Http;

namespace nostify
{
    ///<summary>
    ///Defines Nostify interface
    ///</summary>
    public interface INostify
    {
        ///<summary>
        ///Event store repository
        ///</summary>
        NostifyCosmosClient Repository { get; }

        ///<summary>
        ///Default partition key
        ///</summary>
        string DefaultPartitionKeyPath { get; }
        
        ///<summary>
        ///Default tenant id value for use if NOT implementing multi-tenant
        ///</summary>
        Guid DefaultTenantId { get; }

        ///<summary>
        ///Url for Kafka cluster
        ///</summary>
        string KafkaUrl { get; }

        ///<summary>
        ///Writes event to event store
        ///</summary>        
        ///<param name="eventToPersist">Event to apply and persist in event store</param>
        public Task PersistEventAsync(Event eventToPersist);
        ///<summary>
        ///Writes event to event store
        ///</summary>        
        ///<param name="events">Events to apply and persist in event store</param>
        public Task BulkPersistEventAsync(List<Event> events);
        ///<summary>
        ///Writes Event to the undeliverable events container. Use for handling errors to prevent constant retry.
        ///</summary>
        public Task HandleUndeliverableAsync(string functionName, string errorMessage, Event eventToHandle);
        ///<summary>
        ///Published event to messaging bus
        ///</summary>        
        ///<param name="cosmosTriggerOutput">String output from CosmosDBTrigger, will be processed into a List of Events and published to Kafka</param>
        public Task PublishEventAsync(string cosmosTriggerOutput);
        ///<summary>
        ///Retrieves the event store container
        ///</summary>
        public Task<Container> GetEventStoreContainerAsync(bool allowBulk = false);
        ///<summary>    
        ///Retrieves the current state container for Aggregate Root
        ///</summary>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public Task<Container> GetCurrentStateContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : IAggregate;
        ///<summary>    
        ///Retrieves the current state container for Aggregate Root with bulk function turned on.
        ///</summary>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public Task<Container> GetBulkCurrentStateContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : IAggregate;
        ///<summary>
        ///Retrieves the container for specified Projection
        ///</summary>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public Task<Container> GetProjectionContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : IProjection;
        ///<summary>
        ///Retrieves the container for specified Projection with bulk function turned on
        ///</summary>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public Task<Container> GetBulkProjectionContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : IProjection;
        ///<summary>
        ///Rebuilds the entire container from event stream
        ///</summary>
        public Task RebuildCurrentStateContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : NostifyObject, IAggregate, new();
        ///<summary>
        ///Rehydrates data directly from stream of events when querying a Projection isn't feasible
        ///</summary>
        ///<returns>
        ///The aggregate state rehydrated from create to the specified DateTime.  If no DateTime provided, returns current state.
        ///</returns>
        ///<param name="id">The id (Guid) of the aggregate root to build a projection of</param>
        ///<param name="untilDate">Optional. Will build the aggregate state up to and including this time, if no value provided returns projection of current state</param>
        public Task<T> RehydrateAsync<T>(Guid id, DateTime? untilDate = null) where T : NostifyObject, IAggregate, new();
        ///<summary>
        ///Rehydrates data directly from stream of events and then calls the Projection SeedExternalDataAsync() function to pull any needed data from external services.
        ///</summary>
        ///<returns>
        ///The projection state rehydrated.  Projections may only be rehydrated to the current state.
        ///</returns>
        ///<param name="id">The id (Guid) of the aggregate root to build a projection of</param>
        ///<param name="httpClient">Instance of HttpClient to query external data for Projection</param>
        public Task<T> RehydrateAsync<T>(Guid id, HttpClient httpClient) where T : NostifyObject, IProjection, new();
        ///<summary>
        ///Performs upsert of list
        ///</summary>
        public Task DoBulkUpsertAsync<T>(Container container, List<T> itemList);
        ///<summary>
        ///Performs upsert of list
        ///</summary>
        public Task DoBulkUpsertAsync<T>(string containerName, List<T> itemList);

    }

    ///<summary>
    ///Base class to utilize nostify.  Should inject as a singleton in HostBuilder().ConfigureServices() 
    ///</summary>
    public class Nostify : INostify
    {

        /// <inheritdoc />
        public NostifyCosmosClient Repository { get; }
        /// <inheritdoc />
        public string DefaultPartitionKeyPath { get; }
        /// <inheritdoc />
        public Guid DefaultTenantId { get; }
        /// <inheritdoc />
        public string KafkaUrl { get; }
        

        ///<summary>
        ///Nostify constructor
        ///</summary>
        ///<param name="primaryKey">API Key value</param>
        ///<param name="dbName">Name of event store DB</param>
        ///<param name="cosmosEndpointUri">Uri of cosmos endpoint, format: https://[instance-name].documents.azure.us:443/</param>
        ///<param name="kafkaUrl">Url of Kafka instance, format: localhost:54165</param>
        ///<param name="defaultPartitionKeyPath">Path to partition key for default created current state container, set null to not create, leave default to partition by tenantId </param>
        ///<param name="defaultTenantId">Default tenant id value for use if NOT implementing multi-tenant, if left to default will create one partition in current state container per tenant</param>
        public Nostify(string primaryKey, string dbName, string cosmosEndpointUri, string kafkaUrl, string defaultPartitionKeyPath = "/tenantId", Guid defaultTenantId = default)
        {
            Repository = new NostifyCosmosClient(primaryKey, dbName, cosmosEndpointUri);
            if (defaultPartitionKeyPath != null)
            {
                this.DefaultPartitionKeyPath = defaultPartitionKeyPath;
            }
            this.DefaultTenantId = defaultTenantId;
            this.KafkaUrl = kafkaUrl;
        }


        /// <inheritdoc />
        public async Task PersistEventAsync(Event eventToPersist)
        {
            var eventContainer = await GetEventStoreContainerAsync();
            await eventContainer.CreateItemAsync(eventToPersist, new PartitionKey(eventToPersist.aggregateRootId));
        }

        ///<inheritdoc />
        public async Task PublishEventAsync(string cosmosTriggerOutput)
        {
            var peList = JsonConvert.DeserializeObject<List<Event>>(cosmosTriggerOutput);
            if (peList != null)
            {
                foreach (Event pe in peList)
                {
                    var config = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("bootstrap.servers", KafkaUrl)
                    };


                    using (var p = new ProducerBuilder<string,string>(config).Build())
                    {
                        string topic = pe.command.name;
                        var result = await p.ProduceAsync(topic, new Message<string, string>{  Value = JsonConvert.SerializeObject(pe) });
                    }         

                }
            }
        }

        ///<inheritdoc />
        public async Task BulkPersistEventAsync(List<Event> events)
        {
            Event errorPe = null;
            try
            {
                var eventContainer = await GetEventStoreContainerAsync(true);

                List<Task> taskList = new List<Task>();
                events.ForEach(pe => {
                    taskList.Add(eventContainer.CreateItemAsync(pe, new PartitionKey(pe.aggregateRootId))
                            .ContinueWith(itemResponse =>
                            {
                                if (!itemResponse.IsCompletedSuccessfully)
                                {
                                    errorPe = pe;
                                    throw new Exception("Bulk save error");
                                }
                            }));
                });

                await Task.WhenAll(taskList);
            }
            catch (Exception e)
            {
                await HandleUndeliverableAsync("BulkPersistEvent", e.Message, errorPe);
            }
        }

        ///<inheritdoc />
        public async Task HandleUndeliverableAsync(string functionName, string errorMessage, Event eventToHandle)
        {
            var undeliverableContainer = await GetUndeliverableEventsContainerAsync();

            await undeliverableContainer.CreateItemAsync(new UndeliverableEvent(functionName, errorMessage, eventToHandle), new PartitionKey(eventToHandle.aggregateRootId));
        }

        ///<inheritdoc />
        public async Task<T> RehydrateAsync<T>(Guid id, DateTime? untilDate = null) where T : NostifyObject, IAggregate, new()
        {
            var eventContainer = await GetEventStoreContainerAsync();
            
            T rehyd = new T();
            List<Event> peList = await eventContainer.GetItemLinqQueryable<Event>()
                .Where(pe => pe.aggregateRootId == id.ToString()
                    && (!untilDate.HasValue || pe.timestamp <= untilDate)
                )
                .ReadAllAsync();

            foreach (var pe in peList.OrderBy(pe => pe.timestamp))  //Apply in order
            {
                rehyd.Apply(pe);
            }

            return rehyd;
        }

        ///<inheritdoc />
        public async Task<T> RehydrateAsync<T>(Guid id, HttpClient httpClient) where T : NostifyObject, IProjection, new()
        {
            var eventContainer = await GetEventStoreContainerAsync();
            
            T rehydratedProjection = new T();
            List<Event> eventList = await eventContainer.GetItemLinqQueryable<Event>()
                .Where(e => e.aggregateRootId == id.ToString())
                .ReadAllAsync();

            //Seed returns an update event and queries all external data
            eventList.Add(await rehydratedProjection.SeedExternalDataAsync(this, httpClient));

            foreach (var pe in eventList.OrderBy(pe => pe.timestamp))  //Apply in order
            {
                rehydratedProjection.Apply(pe);
            }

            return rehydratedProjection;
        }


        ///<inheritdoc />
        public async Task<Container> GetEventStoreContainerAsync(bool allowBulk = false)
        {
            return await Repository.GetEventStoreAsync();
        }

        ///<inheritdoc />
        public async Task<Container> GetCurrentStateContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : IAggregate
        {
            return await GetContainerAsync(T.currentStateContainerName, false, partitionKeyPath);
        }

        ///<inheritdoc />
        public async Task<Container> GetBulkCurrentStateContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : IAggregate
        {
            return await GetContainerAsync(T.currentStateContainerName, true, partitionKeyPath);
        }

        ///<inheritdoc />
        public async Task<Container> GetProjectionContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : IProjection
        {
            return await GetContainerAsync(IProjection.containerName, false, partitionKeyPath);
        }

        ///<inheritdoc />
        public async Task<Container> GetBulkProjectionContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : IProjection
        {
            return await GetContainerAsync(IProjection.containerName, true, partitionKeyPath);
        }

        private async Task<Container> GetContainerAsync(string containerName, bool bulkEnabled, string partitionKeyPath)
        {
            var db = await Repository.GetDatabaseAsync(bulkEnabled);
            return await db.CreateContainerIfNotExistsAsync(containerName, partitionKeyPath);
        }

        
        ///<inheritdoc />
        public async Task RebuildCurrentStateContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : NostifyObject, IAggregate, new()
        {
            Container containerToRebuild = await GetCurrentStateContainerAsync<T>();

            //Store data needed for re-creating container
            string containerName = containerToRebuild.Id;

            //Remove container to delete all bad data and start from scratch
            ContainerResponse resp = await containerToRebuild.DeleteContainerAsync();

            List<T> rehydratedAggregates = new List<T>();

            Container eventStore = await GetEventStoreContainerAsync();
            //Get list of distinct aggregate root ids
            List<string> uniqueAggregateRootIds = await eventStore.GetItemLinqQueryable<Event>()
                .Select(pe => pe.aggregateRootId)
                .Distinct()
                .ReadAllAsync();
            
            //Loop through a query the events for 1,000 at a time, then rehydrate 
            const int getThisMany = 1000;
            int endOfRange = uniqueAggregateRootIds.Count();
            int i = 0;
            while (i < endOfRange)
            {
                int rangeNum = (i + getThisMany >= endOfRange) ? endOfRange - 1 : i + getThisMany;
                var aggRange = uniqueAggregateRootIds.GetRange(i,rangeNum);

                var peList = await eventStore.GetItemLinqQueryable<Event>()
                    .Where(pe => aggRange.Contains(pe.aggregateRootId))
                    .ReadAllAsync();
                
                aggRange.ForEach(id => {
                    rehydratedAggregates.Add(Rehydrate<T>(peList.Where(e => e.aggregateRootId == id).OrderBy(e => e.timestamp).ToList()));
                });
                i = i + getThisMany;
            }
            
            //Recreate container
            Container rebuiltContainer = await GetContainerAsync(containerName, false, partitionKeyPath);

            //Save
            rehydratedAggregates.ForEach(agg => {
                rebuiltContainer.UpsertItemAsync<T>(agg);
            });    
            

        }

        ///<summary>
        ///Rehydrates data directly from stream of events passed from calling method.
        ///</summary>
        ///<returns>
        ///The projection state rehydrated to the extent of the events fed into it.
        ///</returns>
        ///<param name="peList">The event stream for the aggregate to be rehydrated</param>
        private T Rehydrate<T>(List<Event> peList) where T : NostifyObject, new()
        {            
            T rehyd = new T();
            foreach (var pe in peList) 
            {
                rehyd.Apply(pe);
            }

            return rehyd;
        }
        

        ///<summary>
        ///Retrieves the undeliverable events container
        ///</summary>
        public async Task<Container> GetUndeliverableEventsContainerAsync(string partitionKeyPath = "/tenantId")
        {
            var db = await Repository.GetDatabaseAsync();
            return await db.CreateContainerIfNotExistsAsync(Repository.UndeliverableEvents, partitionKeyPath);
        }

        ///<inheritdoc />
        public async Task DoBulkUpsertAsync<T>(Container container, List<T> itemList)
        {
            await DoBulkUpsertAsync<T>(container.Id, itemList);
        }

        ///<inheritdoc />
        public async Task DoBulkUpsertAsync<T>(string containerName, List<T> itemList)
        {
            var db = await this.Repository.GetDatabaseAsync(true);
            var bulkContainer = db.GetContainer(containerName);

            List<Task> taskList = new List<Task>();
            itemList.ForEach(i => bulkContainer.UpsertItemAsync(i));
            await Task.WhenAll(taskList);
        }

    }
}
