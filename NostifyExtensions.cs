using System;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Cosmos.Linq;
using System.Net;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;
using System.Data;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace nostify
{
    ///<summary>
    ///Extension methods for Nostify
    ///</summary>
    public static class NostifyExtensions
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
        ///Helps with creating a partition key from string when there are conflicting class names
        ///</summary>
        public static PartitionKey ToPartitionKey(this Guid value)
        {
            return new PartitionKey(value.ToString());
        }

        ///<summary>
        ///Converts string to Guid if it can
        ///</summary>
        public static Guid ToGuid(this string value)
        {
            return Guid.TryParse(value, out Guid guid) ? guid : throw new FormatException("String is not a Guid");
        }

        ///<summary>
        ///Deletes item from Container
        ///</summary>
        public static async Task<ItemResponse<T>> DeleteItemAsync<T>(this Container c, Guid aggregateRootId, Guid tenantId = default)
        {
            return await c.DeleteItemAsync<T>(aggregateRootId.ToString(), new PartitionKey(tenantId.ToString()));
        }

        ///<summary>
        ///Reads from Stream into <c>dynamic</c>
        ///<para>
        /// Use to read the results from <c>HttpRequestData.Body</c> into an object that can update a Projection or Aggregate by calling <c>Apply()</c>.  Will throw error if no data.
        /// </para>
        ///</summary>
        ///<param name="body">HttpRequestData.Body Stream to read from</param>
        ///<param name="isCreate">Set to true if this should be a Create command.  Will throw error if no "id" property is found or id = null in resulting object.</param>
        ///<returns>dynamic object representing the payload of an <c>Event</c></returns>
        public static async Task<dynamic> ReadFromRequestBodyAsync(this Stream body, bool isCreate = false)
        {
            //Read body, throw error if null
            dynamic updateObj = JsonConvert.DeserializeObject<dynamic>(await new StreamReader(body).ReadToEndAsync()) ?? throw new NostifyException("Body contains no data");
            
            //Check for "id" property, throw error if not exists.  Ignore if isCreate is true since create objects don't have ids yet.
            if (!isCreate && updateObj.id == null)
            {
                throw new NostifyException("No id value found.");
            }

            return updateObj;
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
                aggregate = (await container
                    .GetItemLinqQueryable<T>()
                    .Where(agg => agg.id == idToMatch)
                    .ReadAllAsync())
                    .FirstOrDefault();
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
        public static async Task ApplyAndPersistAsync<T>(this Container container, List<Event> newEvents) where T : NostifyObject, IAggregate, new()
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
        public static async Task ApplyAndPersistAsync<T>(this Container container, Event newEvent, PartitionKey partitionKey) where T : NostifyObject, IAggregate, new()
        {
            await container.ApplyAndPersistAsync<T>(new List<Event>(){newEvent}, partitionKey);
        }

        ///<summary>
        ///Applies Event and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers.
        ///</summary>
        ///<param name="container">Container where the projection to update lives</param>
        ///<param name="newEvent">The Event object to apply and persist.</param>
        public static async Task ApplyAndPersistAsync<T>(this Container container, Event newEvent) where T : NostifyObject, IAggregate, new()
        {
            await container.ApplyAndPersistAsync<T>(new List<Event>(){newEvent}, newEvent.partitionKey.ToPartitionKey());
        }
        
    }
}