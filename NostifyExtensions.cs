using System;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Cosmos.Linq;
using System.Net;
using System.Collections.Concurrent;

namespace nostify
{
    ///<summary>
    ///Extension methods for Nostify
    ///</summary>
    public static class NostifyExtensions
    {


        ///<summary>
        ///Applys event to Aggregate and persists event 
        ///</summary>
        ///<param name="nostify">Instance of Nostify class</param>
        ///<param name="aggregate">Aggregate to apply event to</param>
        ///<param name="persistedEvent">Event to apply and persist in event store</param>
        public static async Task ApplyAndPersistAsync(this Nostify nostify, Aggregate aggregate, PersistedEvent persistedEvent)
        {
            aggregate.Apply(persistedEvent);
            await nostify.PersistAsync(persistedEvent);
        }


        ///<summary>
        ///Writes event to event store
        ///</summary>
        
        ///<param name="nostify">Instance of Nostify class</param>
        ///<param name="persistedEvent">Event to apply and persist in event store</param>
        public static async Task PersistAsync(this Nostify nostify, PersistedEvent persistedEvent)
        {
            try
            {
                var eventContainer = await nostify.GetPersistedEventsContainerAsync();

                await eventContainer.CreateItemAsync(persistedEvent, new PartitionKey(persistedEvent.aggregateRootId));
            }
            catch (Exception e)
            {
                await nostify.HandleUndeliverableAsync("PersistEvent", e.Message, persistedEvent);
            }
        }

        ///<summary>
        ///Writes event to event store
        ///</summary>
        
        ///<param name="nostify">Instance of Nostify class</param>
        ///<param name="persistedEvents">Events to apply and persist in event store</param>
        public static async Task BulkPersistAsync(this Nostify nostify, List<PersistedEvent> persistedEvents)
        {
            PersistedEvent errorPe = null;
            try
            {
                var eventContainer = await nostify.GetPersistedEventsContainerAsync(true);

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
                await nostify.HandleUndeliverableAsync("BulkPersistEvent", e.Message, errorPe);
            }
        }

        ///<summary>
        ///Writes PersistedEvent to the undeliverable events container. Use for handling errors to prevent constant retry.
        ///</summary>
        public static async Task HandleUndeliverableAsync(this Nostify nostify, string functionName, string errorMessage, PersistedEvent persistedEvent)
        {
            var undeliverableContainer = await nostify.GetUndeliverableEventsContainerAsync();

            await undeliverableContainer.CreateItemAsync(new UndeliverableEvent(functionName, errorMessage, persistedEvent), new PartitionKey(persistedEvent.aggregateRootId));
        }

        ///<summary>
        ///Rehydrates data directly from stream of events when querying a Projection isn't feasible
        ///</summary>
        ///<returns>
        ///The projection state rehydrated from create to the specified DateTime.  If no DateTime provided, returns current state.
        ///</returns>
        ///<param name="nostify">Singleton instance of Nostify created in startup function</param>
        ///<param name="id">The id (Guid) of the aggregate root to build a projection of</param>
        ///<param name="untilDate">Optional. Will build the aggregate state up to and including this time, if no value provided returns projection of current state</param>
        public static async Task<T> RehydrateAsync<T>(this Nostify nostify, Guid id, DateTime? untilDate = null) where T : NostifyObject, new()
        {
            string eventStorePartitionKey = $"{id}";
            var eventContainer = await nostify.GetPersistedEventsContainerAsync();
            
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
        public static async Task<Container> GetPersistedEventsContainerAsync(this Nostify nostify, bool allowBulk = false)
        {
            var db = await nostify.Repository.GetDatabaseAsync(allowBulk);
            return await db.CreateContainerIfNotExistsAsync(nostify.Repository.PersistedEventsContainer, "/aggregateRootId");
        }

        ///<summary>
        ///Retrieves the current state container for Aggregate Root
        ///</summary>
        ///<param name="nostify">Instance of Nostify class</param>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public static async Task<Container> GetCurrentStateContainerAsync(this Nostify nostify, string partitionKeyPath = "/tenantId")
        {
            var db = await nostify.Repository.GetDatabaseAsync();
            return await db.CreateContainerIfNotExistsAsync(nostify.Repository.CurrentStateContainer, partitionKeyPath);
        }

        ///<summary>
        ///Retrieves the container for specified Projection
        ///</summary>
        ///<param name="nostify">Instance of Nostify class</param>
        ///<param name="containerName">Name of the container the Projection lives in</param>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public static async Task<Container> GetProjectionContainerAsync(this Nostify nostify, string containerName, string partitionKeyPath = "/tenantId")
        {
            var db = await nostify.Repository.GetDatabaseAsync();
            return await db.CreateContainerIfNotExistsAsync(containerName, partitionKeyPath);
        }

        ///<summary>
        ///Retrieves the container for specified Projection, bulk update enabled
        ///</summary>
        ///<param name="nostify">Instance of Nostify class</param>
        ///<param name="containerName">Name of the container the Projection lives in</param>
        ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
        public static async Task<Container> GetBulkProjectionContainerAsync(this Nostify nostify, string containerName, string partitionKeyPath = "/tenantId")
        {
            var db = await nostify.Repository.GetDatabaseAsync(true);
            return await db.CreateContainerIfNotExistsAsync(containerName, partitionKeyPath);
        }

        
        ///<summary>
        ///Rebuilds the entire container from event stream
        ///</summary>
        ///<param name="nostify">Instance of Nostify class</param>
        ///<param name="containerToRebuild">The Cosmos Container that will be rebuilt from the event stream</param>
        public static async Task RebuildContainerAsync<T>(this Nostify nostify, Container containerToRebuild) where T : NostifyObject
        {
            //Store data needed for re-creating container
            string containerName = containerToRebuild.Id;
            Database database = containerToRebuild.Database;

            //Remove container to delete all bad data and start from scratch
            ContainerResponse resp = await containerToRebuild.DeleteContainerAsync();

            //Get list of distinct aggregate root ids
            Container eventStore = await nostify.GetPersistedEventsContainerAsync();
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
                var x = await (Task<T>)aggRef.Invoke(null, new object[] {nostify, id, null });
                rehydratedAggregates.Add(x);
            });
            
            //Recreate container
            Container rebuiltContainer = await nostify.GetProjectionContainerAsync(containerName);

            //Save
            rehydratedAggregates.ForEach(agg => {
                rebuiltContainer.UpsertItemAsync<T>(agg);
            });    
            

        }
        

        ///<summary>
        ///Retrieves the undeliverable events container
        ///</summary>
        public static async Task<Container> GetUndeliverableEventsContainerAsync(this Nostify nostify)
        {
            var db = await nostify.Repository.GetDatabaseAsync();
            return await db.CreateContainerIfNotExistsAsync(nostify.Repository.UndeliverableEvents, "/partitionKey");
        }

        ///<summary>
        ///Runs query and loops through the FeedResponse to return List of all data
        ///</summary>
        public static async Task<List<T>> SqlQueryAllAsync<T>(this Container container, string query)
        {
            FeedIterator<T> fi = container.GetItemQueryIterator<T>(query);

            return await fi.ReadFeedIteratorAsync<T>();
        }

        ///<summary>
        ///Nostify: Runs query through FeedIterator and returns first item that matches criteria or a new instance of the class
        ///</summary>
        public static async Task<T> FirstOrNewAsync<T>(this IQueryable<T> query) where T : new()
        {
            FeedIterator<T> fi = query.ToFeedIterator<T>();
            List<T> list = await fi.ReadFeedIteratorAsync<T>();
            
            return list.FirstOrDefault() ?? new T();
        }

        ///<summary>
        ///Nostify: Runs query through FeedIterator and returns first item that matches criteria
        ///</summary>
        public static async Task<T> FirstOrDefaultAsync<T>(this IQueryable<T> query)
        {
            FeedIterator<T> fi = query.ToFeedIterator<T>();
            List<T> list = await fi.ReadFeedIteratorAsync<T>();
            
            return list.FirstOrDefault();
        }

        ///<summary>
        ///Nostify: Runs query through FeedIterator and loops through the FeedResponse to return List of all data
        ///</summary>
        public static async Task<List<T>> ReadAllAsync<T>(this IQueryable<T> query)
        {
            FeedIterator<T> fi = query.ToFeedIterator<T>();
            return await fi.ReadFeedIteratorAsync<T>();
        }

        ///<summary>
        ///Directly reads FeedIterator, looping to return List of all data
        ///</summary>
        public static async Task<List<T>> ReadFeedIteratorAsync<T>(this FeedIterator<T> fi)
        {
            List<T> retList = new List<T>();
            while (fi.HasMoreResults)
            {
                FeedResponse<T> fs = await fi.ReadNextAsync();
                foreach (var queryResult in fs)
                {
                    retList.Add(queryResult);
                }
            }

            return retList;
        }

        ///<summary>
        ///Gets a typed value from JObject by property name
        ///</summary>
        public static T GetValue<T>(this JObject data, string propertyName)
        {
            JToken jToken = data.Children<JProperty>()
                        .Where(p => p.Name == propertyName)
                        .Select(u => u.Value)
                        .Single();

            T retVal = jToken.ToObject<T>();

            return retVal;
        }

        ///<summary>
        ///Helps with creating a partition key from string when there are conflicting class names
        ///</summary>
        public static PartitionKey ToPartitionKey(this string value)
        {
            return new PartitionKey(value);
        }

        ///<summary>
        ///Helper method to get tenantId out of user claims
        ///</summary>
        // public static int GetTenantId(this Nostify nostify, HttpContext context)
        // {
        //     //Returning 0 will result in no data being pulled due to global query filter
        //     string tenantId = (context != null && context.Request.HttpContext.User.Claims.Count() > 0) ?
        //       context.Request.HttpContext.User.Claims.ToList().Where(c => c.Type == "tenantId").Single().Value
        //       : "0";

        //     return int.Parse(tenantId);
        // }
    }
}