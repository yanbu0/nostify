

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Transactions;
using JsonDiffPatchDotNet;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace nostify;

///<summary>
///Nostify Cosmos Container Extensions
///</summary>
public static class ContainerExtensions
{
    ///<summary>
    ///Applies and persists an event to a list of projections in the specified container.
    ///</summary>
    /// <remarks>
    /// This method applies the given event to each projection in the list, updates their state,
    /// and persists the changes to the specified container. Primarily intended for updates
    /// when an event affects multiple projections.
    /// </remarks>
    public static async Task<List<P>> MultiApplyAndPersistAsync<P>(this Container bulkContainer, INostify nostify, Event eventToApply, List<Guid> projectionIds, int batchSize = 100) where P : NostifyObject, new()
    {
        return await nostify.MultiApplyAndPersistAsync<P>(bulkContainer, eventToApply, projectionIds, batchSize);
    }

    ///<summary>
    ///Applies and persists an event to a list of projections in the specified container.
    ///</summary>
    /// <remarks>
    /// This method applies the given event to each projection in the list, updates their state,
    /// and persists the changes to the specified container. Primarily intended for updates
    /// when an event affects multiple projections.
    /// </remarks>
    public static async Task<List<P>> MultiApplyAndPersistAsync<P>(this Container bulkContainer, INostify nostify, Event eventToApply, List<P> projectionsToUpdate, int batchSize = 100) where P : NostifyObject, new()
    {
        return await nostify.MultiApplyAndPersistAsync<P>(bulkContainer, eventToApply, projectionsToUpdate, batchSize);
    }

    ///<summary>
    ///Checks if Bulk Operations are enabled on a container. If not enabled will throw error if throwIfNotEnabled is true.
    ///</summary>
    ///<param name="container">Container to check</param>
    ///<param name="throwIfNotEnabled">If true will throw error if not enabled</param>
    public static bool ValidateBulkEnabled(this Container container, bool throwIfNotEnabled = false)
    {
        bool bulkEnabled = container.Database.Client.ClientOptions.AllowBulkExecution;
        //Make sure bulk operations are enabled
        if (!bulkEnabled && throwIfNotEnabled)
        {
            throw new NostifyException("Bulk operations must be enabled for this container");
        }
        return bulkEnabled;
    }

    ///<summary>
    ///Bulk deletes items in a container by setting ttl = 1 from a bulk emitted array of KafkaTriggerEvents.  Use in Event Handler.
    ///</summary>
    ///<param name="containerToDeleteFrom">Container to delete items from</param>
    ///<param name="events">Array of strings from KafkaTrigger</param>
    ///<typeparam name="P">Type of Projection to delete</typeparam>
    ///<returns>Number of items deleted</returns>
    public static async Task<int> BulkDeleteFromEventsAsync<P>(this Container containerToDeleteFrom, string[] events) where P : NostifyObject
    {
        List<Guid> projectionIdsToDelete = events
            .Select(e => JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(e)?.GetEvent())
            .Where(e => e != null)
            .Select(e => e!.aggregateRootId)
            .ToList();
        List<P> projectionsToDelete = await containerToDeleteFrom.GetItemLinqQueryable<P>().Where(x => projectionIdsToDelete.Contains(x.id)).ReadAllAsync();
        return await containerToDeleteFrom.BulkDeleteAsync(projectionsToDelete);
    }

    ///<summary>
    ///Bulk deletes items in a container by setting ttl = 1.
    ///</summary>
    ///<param name="containerToDeleteFrom">Container to delete items from</param>
    ///<param name="projectionIdsToDelete">List of projection ids to delete</param>
    ///<typeparam name="P">Type of Projection to delete</typeparam>
    ///<returns>Number of items deleted</returns>
    public static async Task<int> BulkDeleteAsync<P>(this Container containerToDeleteFrom, List<Guid> projectionIdsToDelete) where P : NostifyObject
    {
        List<P> projectionsToDelete = await containerToDeleteFrom.GetItemLinqQueryable<P>().Where(x => projectionIdsToDelete.Contains(x.id)).ReadAllAsync();
        return await containerToDeleteFrom.BulkDeleteAsync(projectionsToDelete);
    }

    ///<summary>
    ///Bulk deletes items in a container by setting ttl = 1.
    ///</summary>
    ///<param name="containerToDeleteFrom">Container to delete items from</param>
    ///<param name="projectionsToDelete">List of projections to delete</param>
    ///<typeparam name="P">Type of Projection to delete</typeparam>
    ///<returns>Number of items deleted</returns>
    public static async Task<int> BulkDeleteAsync<P>(this Container containerToDeleteFrom, List<P> projectionsToDelete) where P : NostifyObject
    {
        containerToDeleteFrom.ValidateBulkEnabled(true);
        var partitionKeyPath = (await containerToDeleteFrom.ReadContainerAsync()).Resource.PartitionKeyPath;

        var response = await containerToDeleteFrom.ReadContainerAsync();
        var containerProps = response.Resource;
        //Make sure TTL is enabled
        if (!containerProps.DefaultTimeToLive.HasValue)
        {
            //Replace with TTL enabled container set to -1
            containerProps.DefaultTimeToLive = 1;
            await containerToDeleteFrom.ReplaceContainerAsync(containerProps);
        }

        int totalUpdated = 0;
        const int batchSize = 100;

        // Loop through batches of 1000
        for (int i = 0; i < projectionsToDelete.Count; i += batchSize)
        {
            try
            {

                var batchItems = projectionsToDelete.Skip(i).Take(batchSize).ToList();
                List<Task> tasks = new List<Task>();

                foreach (var item in batchItems)
                {
                    // Set TTL to 1
                    List<PatchOperation> patchOperations = new List<PatchOperation>(){
                        PatchOperation.Set("/ttl", 1)
                    };
                    // Get partition key from item by using the value in partitionKeyPath
                    var propertyInfo = item.GetType().GetProperty(partitionKeyPath.Trim('/'));
                    if (propertyInfo == null)
                    {
                        throw new NostifyException($"Property '{partitionKeyPath.Trim('/')}' does not exist on type '{item.GetType().Name}'.");
                    }
                    string pk = propertyInfo.GetValue(item)?.ToString();
                    if (string.IsNullOrEmpty(pk))
                    {
                        throw new NostifyException($"Partition key value is null or empty for property '{partitionKeyPath.Trim('/')}' on item of type '{item.GetType().Name}'.");
                    }
                    tasks.Add(containerToDeleteFrom.PatchItemAsync<P>(item.id.ToString(), pk.ToPartitionKey(), patchOperations));
                }
                // Wait for all tasks to complete
                await Task.WhenAll(tasks);
                totalUpdated += tasks.Where(t => t.IsCompletedSuccessfully).Count();
            }
            catch (Exception ex)
            {
                // Handle exception (log it, rethrow it, etc.)
                throw new NostifyException($"An error occurred while deleting items in bulk {i}" + ex.Message);
            }
        }

        return totalUpdated;
    }

    ///<summary>
    ///Deletes all items in a Projection container by setting ttl = 1. Used to clear out when re-initializing. Should not be used in production when in use.
    ///</summary>
    ///<param name="containerToDeleteFrom">Container to delete all items from</param>
    ///<typeparam name="P">Type of Projection to delete</typeparam>
    ///<returns>Number of items deleted</returns>
    public static async Task<int> DeleteAllBulkAsync<P>(this Container containerToDeleteFrom) where P : NostifyObject
    {

        List<P> allProjections = await containerToDeleteFrom.GetItemLinqQueryable<P>().ReadAllAsync();
        return await containerToDeleteFrom.BulkDeleteAsync(allProjections);
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
    ///<param name="projectionBaseAggregateId">Will apply to this id, use when updating a projection from events not originally from the base aggregate.</param>
    public static async Task<T> ApplyAndPersistAsync<T>(this Container container, List<Event> newEvents, PartitionKey partitionKey, Guid? projectionBaseAggregateId) where T : NostifyObject, new()
    {
        T nosObjToUpdate = new T();
        JObject unchangedNosObj = new JObject();
        bool isNew = false;
        Event firstEvent = newEvents.First();
        Guid idToMatch = projectionBaseAggregateId ?? firstEvent.aggregateRootId;

        //Null projectionBaseAggregateId means it is an event from the projection base aggregate
        if (firstEvent.command.isNew && projectionBaseAggregateId == null)
        {
            isNew = true;
        }
        else
        {
            //Update container based off aggregate root id
            try
            {
                nosObjToUpdate = await container.ReadItemAsync<T>(idToMatch.ToString(), partitionKey);
                unchangedNosObj = JObject.Parse(JsonConvert.SerializeObject(nosObjToUpdate));
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"Aggregate not found {idToMatch}");
                nosObjToUpdate = null;
            }
        }

        //Null means it has been previously deleted
        if (nosObjToUpdate != null)
        {
            newEvents.ForEach(newEvent => nosObjToUpdate.Apply(newEvent));

            if (isNew)
            {
                //Do create operation
                try
                {
                    await container.CreateItemAsync<T>(nosObjToUpdate, partitionKey);
                }
                catch (CosmosException ex)
                {
                    throw new NostifyException($"Create failed for {idToMatch} || {ex.Message} || {ex.InnerException?.Message}");
                }
            }
            else
            {
                //For existing records, do patch operation
                try
                {
                    //Compare each property and create patch operation for each changed property
                    List<PatchOperation> patchOperations = new List<PatchOperation>();
                    JObject updatedJObj = JObject.Parse(JsonConvert.SerializeObject(nosObjToUpdate));
                    foreach (var prop in updatedJObj.Properties())
                    {
                        //if (prop.Value.ToString() != unchangedNosObj[prop.Name].ToString())// (JToken.DeepEquals(prop.Value, unchangedNosObj[prop.Name]))
                        var jdp = new JsonDiffPatch();
                        if (jdp.Diff(unchangedNosObj[prop.Name], prop.Value) != null)
                        {
                            patchOperations.Add(PatchOperation.Set($"/{prop.Name}", prop.Value));
                        }
                    }

                    var patchResult = await SafePatchItemAsync<T>(container, nosObjToUpdate.id.ToString(), partitionKey, patchOperations);


                }
                catch (CosmosException ex)
                {
                    throw new NostifyException($"Update failed for {idToMatch} || {ex.Message} || {ex.InnerException?.Message}");
                }
            }
        }

        return nosObjToUpdate;
    }

    ///<summary>
    ///Applies multiple Events and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvents">The Event list to apply and persist.</param>
    ///<param name="partitionKey">The partition to update, by default is tenantId</param>
    public static async Task<T> ApplyAndPersistAsync<T>(this Container container, List<Event> newEvents, PartitionKey partitionKey) where T : NostifyObject, new()
    {
        return await container.ApplyAndPersistAsync<T>(newEvents, partitionKey, null);
    }


    ///<summary>
    ///Applies multiple Events and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers. Uses partitionKey from first Event in List.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvents">The Event list to apply and persist.</param>
    public static async Task<T> ApplyAndPersistAsync<T>(this Container container, List<Event> newEvents) where T : NostifyObject, new()
    {
        Event firstEvent = newEvents.First();

        return await container.ApplyAndPersistAsync<T>(newEvents, firstEvent.partitionKey.ToPartitionKey());
    }

    ///<summary>
    ///Applies Event and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvent">The Event object to apply and persist.</param>
    ///<param name="partitionKey">The partition to update, by default is tenantId</param>
    public static async Task<T> ApplyAndPersistAsync<T>(this Container container, Event newEvent, PartitionKey partitionKey) where T : NostifyObject, new()
    {
        return await container.ApplyAndPersistAsync<T>(new List<Event>() { newEvent }, partitionKey);
    }

    ///<summary>
    ///Applies Event and updates this container. Uses existence of an "isNew" property to key off if is create or not. Primarily used in Event Handlers.
    ///</summary>
    ///<param name="container">Container where the projection to update lives</param>
    ///<param name="newEvent">The Event object to apply and persist.</param>
    public static async Task<T> ApplyAndPersistAsync<T>(this Container container, Event newEvent) where T : NostifyObject, new()
    {
        return await container.ApplyAndPersistAsync<T>(new List<Event>() { newEvent }, newEvent.partitionKey.ToPartitionKey());
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

        await bulkContainer.DoBulkCreateAsync<T>(objToUpsertList);

    }

    /// <summary>
    /// Bulk updates objects from raw string array of KafkaTriggerEvents. Use in Event Handler. NOTE: this only supports updating root-level properties and not nested objects or arrays.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="bulkContainer">Must have bulk operations set to true</param>
    /// <param name="events">Array of strings from KafkaTrigger</param>
    /// <param name="partitionKeyName">Name of partition key, default is tenantId</param>
    /// <returns></returns>
    [Obsolete("BulkUpdateFromKafkaTriggerEventsAsync is deprecated, please use BulkApplyAndPersist instead.")]
    public static async Task<PatchItemResult[]> BulkUpdateFromKafkaTriggerEventsAsync<T>(this Container bulkContainer, string[] events, string partitionKeyName = "tenantId")
    {
        var triggerEvents = events
            .Select(e => JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(e)?.GetEvent())
            .Where(e => e != null)
            .Select(e => e!);

        var patchOperations = triggerEvents
            .Where(evt => evt.payload is JObject)
            .Select(evt =>
            {
                var jobj = (JObject)evt.payload;
                var id = jobj["id"]?.ToString();
                var partitionId = jobj[partitionKeyName]?.ToString();
                var operations = jobj.Properties()
                    .Where(prop => prop.Name != "id" && prop.Name != partitionKeyName)
                    .Select(prop => PatchOperation.Set($"/{prop.Name}", prop.Value))
                    .ToList();
                return (id, partitionId, operations);
            });

        var validPatchOperations = patchOperations
            .Where(patch =>
                !string.IsNullOrWhiteSpace(patch.id) &&
                !string.IsNullOrWhiteSpace(patch.partitionId) &&
                patch.operations.Count > 0);

        var tasks = validPatchOperations
            .Select(patch => SafePatchItemAsync<T>(bulkContainer, patch.id, new PartitionKey(patch.partitionId), patch.operations));

        var results = await Task.WhenAll(tasks);
        return [
            ..results,
            ..patchOperations
                .Where(patch =>
                    string.IsNullOrWhiteSpace(patch.id) ||
                    string.IsNullOrWhiteSpace(patch.partitionId) ||
                    patch.operations.Count == 0)
                .Select(patch => PatchItemResult.InvalidOperationResult(patch))
        ];
    }

    /// <summary>
    /// Safely patches an item in a container. If the item is not found, returns a NotFound result. If an exception occurs, returns an Exception result.
    /// </summary>
    /// <typeparam name="T">The type of item to patch</typeparam>
    /// <param name="container">The container where the item lives</param>
    /// <param name="id">The id of the item to patch</param>
    /// <param name="partitionKey">The partition key of the item</param>
    /// <param name="patchOperations">The patch operations to apply</param>
    /// <returns>A PatchItemResult</returns>
    public static async Task<PatchItemResult> SafePatchItemAsync<T>(this Container container, string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations)
    {
        try
        {
            await container.PatchItemAsync<T>(id, partitionKey, patchOperations);
            return PatchItemResult.SuccessResult(id, partitionKey);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return PatchItemResult.NotFoundResult(id, partitionKey);
        }
        catch (CosmosException ex)
        {
            return PatchItemResult.ExceptionResult(id, partitionKey, ex);
        }
        catch (Exception ex)
        {
            return PatchItemResult.ExceptionResult(id, partitionKey, ex);
        }
    }

    /// <summary>
    /// Bulk upserts a list of items
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="bulkContainer">Container to upsert items to</param>
    /// <param name="itemList">List of items to upsert</param>
    /// <param name="allowRetry">Optional. If true will retry on 429 too many requests errors. Default is false</param>
    /// <returns></returns>
    public static async Task DoBulkUpsertAsync<T>(this Container bulkContainer, List<T> itemList, bool allowRetry = false) where T : IApplyable
    {
        //throw if bulk not enabled
        bulkContainer.ValidateBulkEnabled(true);

        List<Task> taskList = new List<Task>();
        itemList.ForEach(i => bulkContainer.UpsertItemAsync(i).ContinueWith(itemResponse =>
        {
            if (!itemResponse.IsCompletedSuccessfully)
            {
                //Retry if too many requests error
                if (allowRetry && itemResponse.Exception.InnerException is CosmosException ce && ce.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    int waitTime = ce.RetryAfter.HasValue ? (int)ce.RetryAfter.Value.TotalMilliseconds : 1000;
                    Task.Delay(waitTime).ContinueWith(_ => bulkContainer.UpsertItemAsync(i)
                                        .ContinueWith(_ => { throw new NostifyException($"Bulk Upsert Error {itemResponse.Exception.Message}"); }));
                }
                else
                {
                    throw new NostifyException($"Bulk Upsert Error {itemResponse.Exception.Message}");
                }
            }
        }));
        await Task.WhenAll(taskList);
    }

    /// <summary>
    /// Bulk creates a list of items
    /// </summary>
    /// <typeparam name="T">Must be able to Apply an Event</typeparam>
    /// <param name="bulkContainer">Container to create items in, must have bulk enabled</param>
    /// <param name="itemList">List of items to create</param>
    /// <param name="allowRetry">Optional. If true will retry on 429 too many requests errors. Default is false</param>
    /// <returns></returns>
    public static async Task DoBulkCreateAsync<T>(this Container bulkContainer, List<T> itemList, bool allowRetry = false) where T : IApplyable
    {
        //throw if bulk not enabled
        bulkContainer.ValidateBulkEnabled(true);

        List<Task> taskList = new List<Task>();
        itemList.ForEach(i => bulkContainer.CreateItemAsync(i).ContinueWith(itemResponse =>
        {
            if (!itemResponse.IsCompletedSuccessfully)
            {
                //Retry if too many requests error
                if (allowRetry && itemResponse.Exception.InnerException is CosmosException ce && ce.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    int waitTime = ce.RetryAfter.HasValue ? (int)ce.RetryAfter.Value.TotalMilliseconds : 1000;
                    Task.Delay(waitTime).ContinueWith(_ => bulkContainer.CreateItemAsync(i)
                                        .ContinueWith(_ => { throw new NostifyException($"Bulk Create Error {itemResponse.Exception.Message}"); }));
                }
                else
                {
                    throw new NostifyException($"Bulk Create Error {itemResponse.Exception.Message}");
                }
            }
        }));
        await Task.WhenAll(taskList);
    }

}