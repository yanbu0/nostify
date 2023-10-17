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
    }
}