using System;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Linq;

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

        public Task ApplyAndPersistAsync(Aggregate aggregate, PersistedEvent persistedEvent);
        public Task PersistAsync(PersistedEvent persistedEvent);
        public Task BulkPersistAsync(List<PersistedEvent> persistedEvents);
        public Task HandleUndeliverableAsync(string functionName, string errorMessage, PersistedEvent persistedEvent);
        public Task PublishEvent(PersistedEvent persistedEvent);
        public Task<Container> GetPersistedEventsContainerAsync(bool allowBulk = false);
        public Task<Container> GetCurrentStateContainerAsync(string partitionKeyPath = "/tenantId");

    }

    ///<summary>
    ///Base class to utilize nostify
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
        ///Nostify constructor
        ///</summary>
        ///<param name="Primarykey">API Key value</param>
        ///<param name="DbName">Name of event store DB</param>
        ///<param name="EndpointUri">Uri of cosmos endpoint, format: https://[instance-name].documents.azure.us:443/</param>
        ///<param name="DefaultPartitionKeyPath">Path to partition key for default created current state container, set null to not create, leave default to partition by tenantId </param>
        ///<param name="DefaultTenantId">Default tenannt id value for use if NOT implementing multi-tenant, if left to default will create one partition in current state container</param>
        public Nostify(string Primarykey, string DbName, string EndpointUri, string DefaultPartitionKeyPath = "/tenantId", int DefaultTenantId = 0)
        {
            Repository = new NostifyCosmosClient(Primarykey, DbName, EndpointUri);
            if (DefaultPartitionKeyPath != null)
            {
                this.DefaultPartitionKeyPath = DefaultPartitionKeyPath;
            }
            this.DefaultTenantId = DefaultTenantId;
        }
        

        ///<summary>
        ///Applys event to Aggregate and persists event 
        ///</summary>
        ///<param name="aggregate">Aggregate to apply event to</param>
        ///<param name="persistedEvent">Event to apply and persist in event store</param>
        public async Task ApplyAndPersistAsync(Aggregate aggregate, PersistedEvent persistedEvent)
        {
            aggregate.Apply(persistedEvent);
            await PersistAsync(persistedEvent);
        }


        ///<summary>
        ///Writes event to event store
        ///</summary>        
        ///<param name="persistedEvent">Event to apply and persist in event store</param>
        public async Task PersistAsync(PersistedEvent persistedEvent)
        {
            try
            {
                var eventContainer = await GetPersistedEventsContainerAsync();

                await eventContainer.CreateItemAsync(persistedEvent, new PartitionKey(persistedEvent.aggregateRootId));
            }
            catch (Exception e)
            {
                await HandleUndeliverableAsync("PersistEvent", e.Message, persistedEvent);
            }
        }

        ///<summary>
        ///Published event to messaging bus
        ///</summary>        
        ///<param name="persistedEvent">Event to apply and persist in event store</param>
        public async Task PublishEvent(PersistedEvent persistedEvent)
        {
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception e)
            {
                await HandleUndeliverableAsync("PublishEvent", e.Message, persistedEvent);
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
                var eventContainer = await GetPersistedEventsContainerAsync(true);

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
            string eventStorePartitionKey = $"{id}";
            var eventContainer = await GetPersistedEventsContainerAsync();
            
            T rehyd = new T();
            List<PersistedEvent> peList = await eventContainer.GetItemLinqQueryable<PersistedEvent>()
                .Where(pe => pe.aggregateRootId == eventStorePartitionKey
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
                (rehyd as Projection).Seed(untilDate);
            }

            return rehyd;
        }


        ///<summary>
        ///Retrieves the event store container
        ///</summary>
        public async Task<Container> GetPersistedEventsContainerAsync(bool allowBulk = false)
        {
            var db = await Repository.GetDatabaseAsync(allowBulk);
            return await db.CreateContainerIfNotExistsAsync(Repository.PersistedEventsContainer, "/aggregateRootId");
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
        public async Task RebuildContainerAsync<T>(Container containerToRebuild) where T : NostifyObject
        {
            //Store data needed for re-creating container
            string containerName = containerToRebuild.Id;
            Database database = containerToRebuild.Database;

            //Remove container to delete all bad data and start from scratch
            ContainerResponse resp = await containerToRebuild.DeleteContainerAsync();

            //Get list of distinct aggregate root ids
            Container eventStore = await GetPersistedEventsContainerAsync();
            List<Guid> uniqueAggregateRootIds = (await eventStore.GetItemLinqQueryable<PersistedEvent>()
                .Select(pe => pe.aggregateRootId)
                .Distinct()
                .ReadAllAsync())
                .Select(g => Guid.Parse(g))
                .ToList();

            List<T> rehydratedAggregates = new List<T>();

            var rehydrate = typeof(NostifyExtensions).GetMethod("RehydrateAsync");
            var aggRef = rehydrate.MakeGenericMethod(typeof(T));

            //Invoke the Rehydrate method to recreate the current state of the projection from the event stream
            uniqueAggregateRootIds.ForEach(async id => {
                var x = await (Task<T>)aggRef.Invoke(null, new object[] {id, null });
                rehydratedAggregates.Add(x);
            });
            
            //Recreate container
            Container rebuiltContainer = await GetProjectionContainerAsync(containerName);

            //Save
            rehydratedAggregates.ForEach(agg => {
                rebuiltContainer.UpsertItemAsync<T>(agg);
            });    
            

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
