using System;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Linq;
using Newtonsoft.Json;
using Confluent.Kafka;

namespace nostify
{
    ///<summary>
    ///Defines Nostify interface
    ///</summary>
    public interface INostify
    {
        NostifyCosmosClient Repository { get; }
        string DefaultPartitionKeyPath { get; }
        int DefaultTenantId { get; }
        string KafkaUrl { get; }

        public Task PersistAsync(PersistedEvent persistedEvent);
        public Task BulkPersistAsync(List<PersistedEvent> persistedEvents);
        public Task HandleUndeliverableAsync(string functionName, string errorMessage, PersistedEvent persistedEvent);
        public Task PublishEventAsync(string cosmosTriggerOutput);
        public Task<Container> GetEventStoreContainerAsync(bool allowBulk = false);
        public Task<Container> GetCurrentStateContainerAsync(string partitionKeyPath = "/tenantId");
        public Task<Container> GetProjectionContainerAsync(string containerName, string partitionKeyPath = "/tenantId");
        public Task<T> RehydrateAsync<T>(Guid id, DateTime? untilDate = null) where T : NostifyObject, new();

    }

    ///<summary>
    ///Base class to utilize nostify.  Should inject as a singleton in HostBuilder().ConfigureServices() 
    ///</summary>
    public class Nostify : INostify
    {

        ///<summary>
        ///Event store repository
        ///</summary>
        public NostifyCosmosClient Repository { get; }

        ///<summary>
        ///Default partition key
        ///</summary>
        public string DefaultPartitionKeyPath { get; }

        ///<summary>
        ///Default tenannt id value for use if NOT implementing multi-tenant
        ///</summary>
        public int DefaultTenantId { get; }

        ///<summary>
        ///Url for Kafka cluster
        ///</summary>
        public string KafkaUrl { get; }
        

        ///<summary>
        ///Nostify constructor
        ///</summary>
        ///<param name="Primarykey">API Key value</param>
        ///<param name="DbName">Name of event store DB</param>
        ///<param name="CosmosEndpointUri">Uri of cosmos endpoint, format: https://[instance-name].documents.azure.us:443/</param>
        ///<param name="KafkaUrl">Url of Kafka instance, format: localhost:54165</param>
        ///<param name="DefaultPartitionKeyPath">Path to partition key for default created current state container, set null to not create, leave default to partition by tenantId </param>
        ///<param name="DefaultTenantId">Default tenant id value for use if NOT implementing multi-tenant, if left to default will create one partition in current state container per tenant</param>
        public Nostify(string Primarykey, string DbName, string CosmosEndpointUri, string KafkaUrl, string DefaultPartitionKeyPath = "/tenantId", int DefaultTenantId = 0)
        {
            Repository = new NostifyCosmosClient(Primarykey, DbName, CosmosEndpointUri);
            if (DefaultPartitionKeyPath != null)
            {
                this.DefaultPartitionKeyPath = DefaultPartitionKeyPath;
            }
            this.DefaultTenantId = DefaultTenantId;
            this.KafkaUrl = KafkaUrl;
        }


        ///<summary>
        ///Writes event to event store
        ///</summary>        
        ///<param name="persistedEvent">Event to apply and persist in event store</param>
        public async Task PersistAsync(PersistedEvent persistedEvent)
        {
            try
            {
                var eventContainer = await GetEventStoreContainerAsync();

                await eventContainer.CreateItemAsync(persistedEvent, new PartitionKey(persistedEvent.aggregateRootId));
            }
            catch (Exception e)
            {
                await HandleUndeliverableAsync(nameof(PersistAsync), e.Message, persistedEvent);
            }
        }

        ///<summary>
        ///Published event to messaging bus
        ///</summary>        
        ///<param name="cosmosTriggerOutput">String output from CosmosDBTrigger, will be processed into a List of PersistedEvents and published to Kafka</param>
        public async Task PublishEventAsync(string cosmosTriggerOutput)
        {
            var peList = JsonConvert.DeserializeObject<List<PersistedEvent>>(cosmosTriggerOutput);
            if (peList != null)
            {
                foreach (PersistedEvent pe in peList)
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

        ///<summary>
        ///Writes event to event store
        ///</summary>        
        ///<param name="persistedEvents">Events to apply and persist in event store</param>
        public async Task BulkPersistAsync(List<PersistedEvent> persistedEvents)
        {
            PersistedEvent errorPe = null;
            try
            {
                var eventContainer = await GetEventStoreContainerAsync(true);

                List<Task> taskList = new List<Task>();
                persistedEvents.ForEach(pe => {
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

        ///<summary>
        ///Writes PersistedEvent to the undeliverable events container. Use for handling errors to prevent constant retry.
        ///</summary>
        public async Task HandleUndeliverableAsync(string functionName, string errorMessage, PersistedEvent persistedEvent)
        {
            var undeliverableContainer = await GetUndeliverableEventsContainerAsync();

            await undeliverableContainer.CreateItemAsync(new UndeliverableEvent(functionName, errorMessage, persistedEvent), new PartitionKey(persistedEvent.aggregateRootId));
        }

        ///<summary>
        ///Rehydrates data directly from stream of events when querying a Projection isn't feasible
        ///</summary>
        ///<returns>
        ///The projection state rehydrated from create to the specified DateTime.  If no DateTime provided, returns current state.
        ///</returns>
        ///<param name="id">The id (Guid) of the aggregate root to build a projection of</param>
        ///<param name="untilDate">Optional. Will build the aggregate state up to and including this time, if no value provided returns projection of current state</param>
        public async Task<T> RehydrateAsync<T>(Guid id, DateTime? untilDate = null) where T : NostifyObject, new()
        {
            var eventContainer = await GetEventStoreContainerAsync();
            
            T rehyd = new T();
            List<PersistedEvent> peList = await eventContainer.GetItemLinqQueryable<PersistedEvent>()
                .Where(pe => pe.aggregateRootId == id.ToString()
                    && (!untilDate.HasValue || pe.timestamp <= untilDate)
                )
                .ReadAllAsync();

            foreach (var pe in peList) //.OrderBy(pe => pe.sequenceNumber))  //Apply in order
            {
                rehyd.Apply(pe);
            }

            //Go get any neccessary values from external aggregates
            if (rehyd is Projection)
            {
                await (rehyd as Projection).Seed(untilDate);
            }

            return rehyd;
        }


        ///<summary>
        ///Retrieves the event store container
        ///</summary>
        public async Task<Container> GetEventStoreContainerAsync(bool allowBulk = false)
        {
            return await Repository.GetEventStoreAsync();
        }

        ///<summary>
        ///Retrieves the current state container for Aggregate Root
        ///</summary>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public async Task<Container> GetCurrentStateContainerAsync(string partitionKeyPath = "/tenantId")
        {
            var db = await Repository.GetDatabaseAsync();
            return await db.CreateContainerIfNotExistsAsync(Repository.CurrentStateContainer, partitionKeyPath);
        }

        ///<summary>
        ///Retrieves the container for specified Projection
        ///</summary>
        ///<param name="containerName">Name of the container the Projection lives in</param>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public async Task<Container> GetProjectionContainerAsync(string containerName, string partitionKeyPath = "/tenantId")
        {
            var db = await Repository.GetDatabaseAsync();
            return await db.CreateContainerIfNotExistsAsync(containerName, partitionKeyPath);
        }

        ///<summary>
        ///Retrieves the container for specified Projection, bulk update enabled
        ///</summary>
        ///<param name="containerName">Name of the container the Projection lives in</param>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public async Task<Container> GetBulkProjectionContainerAsync(string containerName, string partitionKeyPath = "/tenantId")
        {
            var db = await Repository.GetDatabaseAsync(true);
            return await db.CreateContainerIfNotExistsAsync(containerName, partitionKeyPath);
        }

        
        ///<summary>
        ///Rebuilds the entire container from event stream
        ///</summary>
        ///<param name="containerToRebuild">The Cosmos Container that will be rebuilt from the event stream</param>
        public async Task RebuildContainerAsync<T>(Container containerToRebuild) where T : NostifyObject, new()
        {
            //Store data needed for re-creating container
            string containerName = containerToRebuild.Id;

            //Remove container to delete all bad data and start from scratch
            ContainerResponse resp = await containerToRebuild.DeleteContainerAsync();

            List<T> rehydratedAggregates = new List<T>();

            Container eventStore = await GetEventStoreContainerAsync();
            //Get list of distinct aggregate root ids
            List<string> uniqueAggregateRootIds = await eventStore.GetItemLinqQueryable<PersistedEvent>()
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

                var peList = await eventStore.GetItemLinqQueryable<PersistedEvent>()
                    .Where(pe => aggRange.Contains(pe.aggregateRootId))
                    .ReadAllAsync();
                
                aggRange.ForEach(id => {
                    rehydratedAggregates.Add(Rehydrate<T>(peList.Where(pe => pe.aggregateRootId == id).ToList()));
                });
                i = i + getThisMany;
            }
            
            //Recreate container
            Container rebuiltContainer = await GetProjectionContainerAsync(containerName);

            //Save
            rehydratedAggregates.ForEach(agg => {
                rebuiltContainer.UpsertItemAsync<T>(agg);
            });    
            

        }

        ///<summary>
        ///Rehydrates data directly from stream of events passed from calling method.
        ///</summary>
        ///<returns>
        ///The projection state rehydrated to the extend of the events fed into it.
        ///</returns>
        ///<param name="peList">The event stream for the aggregate to be rehydrated</param>
        private T Rehydrate<T>(List<PersistedEvent> peList) where T : NostifyObject, new()
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
        public async Task<Container> GetUndeliverableEventsContainerAsync()
        {
            var db = await Repository.GetDatabaseAsync();
            return await db.CreateContainerIfNotExistsAsync(Repository.UndeliverableEvents, "/partitionKey");
        }

    }
}
