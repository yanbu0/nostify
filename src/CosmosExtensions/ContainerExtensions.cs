

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace nostify;

public static class ContainerExtensions
{
    ///<summary>
    ///Runs query and loops through the FeedResponse to return List of all data
    ///</summary>
    public static async Task<List<T>> SqlQueryAllAsync<T>(this Container container, string query)
    {
        FeedIterator<T> fi = container.GetItemQueryIterator<T>(query);

        return await fi.ReadFeedIteratorAsync<T>();
    }

    ///<summary>
    ///Deletes item from Container
    ///</summary>
    public static async Task<ItemResponse<T>> DeleteItemAsync<T>(this Container c, Guid aggregateRootId, Guid tenantId = default)
    {
        return await c.DeleteItemAsync<T>(aggregateRootId.ToString(), new PartitionKey(tenantId.ToString()));
    }

    ///<summary>
    ///Applies multiple Events and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvents">The Event list to apply and persist.</param>
    ///<param name="partitionKey">The partition to update, by default is tenantId</param>
    ///<param name="projectionBaseAggregateId">Will apply to this id, unless null then will take first in newEvents List</param>
    public static async Task ApplyAndPersistAsync<T>(this Container container, List<Event> newEvents, PartitionKey partitionKey, Guid? projectionBaseAggregateId) where T : NostifyObject, new()
    {
        T? aggregate;
        Event firstEvent = newEvents.First();
        Guid idToMatch = projectionBaseAggregateId ?? firstEvent.aggregateRootId;

        if (firstEvent.command.isNew)
        {
            aggregate = new T();
        }
        else 
        {
            //Update container based off aggregate root id
            aggregate = await container
                .GetItemLinqQueryable<T>()
                .Where(agg => agg.id == idToMatch)
                .FirstOrDefaultAsync();
        }

        //Null means it has been previously deleted
        if (aggregate != null)
        {
            newEvents.ForEach(newEvent => aggregate.Apply(newEvent));
            await container.UpsertItemAsync<T>(aggregate, partitionKey);
        }
    }

    ///<summary>
    ///Applies multiple Events and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvents">The Event list to apply and persist.</param>
    ///<param name="partitionKey">The partition to update, by default is tenantId</param>
    public static async Task ApplyAndPersistAsync<T>(this Container container, List<Event> newEvents, PartitionKey partitionKey) where T : NostifyObject, new()
    {
        await container.ApplyAndPersistAsync<T>(newEvents, partitionKey, null);
    }
    

    ///<summary>
    ///Applies multiple Events and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers. Uses partitionKey from first Event in List.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvents">The Event list to apply and persist.</param>
    public static async Task ApplyAndPersistAsync<T>(this Container container, List<Event> newEvents) where T : NostifyObject, new()
    {
        Event firstEvent = newEvents.First();

        await container.ApplyAndPersistAsync<T>(newEvents, firstEvent.partitionKey.ToPartitionKey());
    }

    ///<summary>
    ///Applies Event and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvent">The Event object to apply and persist.</param>
    ///<param name="partitionKey">The partition to update, by default is tenantId</param>
    public static async Task ApplyAndPersistAsync<T>(this Container container, Event newEvent, PartitionKey partitionKey) where T : NostifyObject, new()
    {
        await container.ApplyAndPersistAsync<T>(new List<Event>(){newEvent}, partitionKey);
    }

    ///<summary>
    ///Applies Event and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvent">The Event object to apply and persist.</param>
    public static async Task ApplyAndPersistAsync<T>(this Container container, Event newEvent) where T : NostifyObject, new()
    {
        await container.ApplyAndPersistAsync<T>(new List<Event>(){newEvent}, newEvent.partitionKey.ToPartitionKey());
    }

    /// <summary>
    /// Bulk creates objects in Projection container from raw string array of KafkaTriggerEvents.  Use in Event Handler.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="bulkContainer">Must have bulk operations set to true</param>
    /// <param name="events">Array of strings from KafkaTrigger</param>
    /// <returns></returns>
    /// <exception cref="NostifyException"></exception>
    public static async Task BulkCreateFromKafkaTriggerEventsAsync<T>(this Container bulkContainer, string[] events) where T : NostifyObject, new()
    {
        List<T> objToUpsertList = new List<T>();
        events.ToList().ForEach(eventStr =>
        {
            NostifyKafkaTriggerEvent triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
            if (triggerEvent == null)
            {
                throw new NostifyException("Event is null");
            }
            Event newEvent = triggerEvent.GetEvent();
            if (!newEvent.command.isNew)
            {
                throw new NostifyException("Event is not a create event");
            }
            T objToUpsert = new T();
            objToUpsert.Apply(newEvent);
            objToUpsertList.Add(objToUpsert);
        });

        await bulkContainer.DoBulkUpsertAsync<T>(objToUpsertList);

    }

    /// <summary>
    /// Bulk upserts a list of items
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="bulkContainer"></param>
    /// <param name="itemList"></param>
    /// <returns></returns>
    public static async Task DoBulkUpsertAsync<T>(this Container bulkContainer, List<T> itemList) where T : IApplyable
    {        
        List<Task> taskList = new List<Task>();
        itemList.ForEach(i => bulkContainer.UpsertItemAsync(i));
        await Task.WhenAll(taskList);
    }

}