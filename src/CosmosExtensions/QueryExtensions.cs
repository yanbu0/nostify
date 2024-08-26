

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Newtonsoft.Json;

namespace nostify;

public static class QueryExtensions
{
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

}