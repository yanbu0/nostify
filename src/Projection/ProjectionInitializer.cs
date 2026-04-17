using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace nostify;

/// <summary>
/// Provides methods to initialize projections and manage projection containers.
/// </summary>
public class ProjectionInitializer : IProjectionInitializer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectionInitializer"/> class.
    /// </summary>
    public ProjectionInitializer()
    {
        // Constructor logic can be added here if needed
    }

    ///<summary>
    ///Initialize the Projection with the specified id.  Will requery all needed data from all services.
    ///</summary>
    public async Task<List<P>> InitAsync<P, A>(Guid id, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(new List<Guid> { id }, nostify, httpClient, pointInTime);
    }

    ///<summary>
    ///Initialize the Projection with the specified id, scoped to the given partition key. Will requery all needed data from all services.
    ///</summary>
    public async Task<List<P>> InitAsync<P, A>(Guid id, PartitionKey partitionKey, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(new List<Guid> { id }, partitionKey, nostify, httpClient, pointInTime);
    }

    ///<summary>
    ///Initialize the Projection with the specified id, scoped to the given tenant. Will requery all needed data from all services.
    ///</summary>
    public async Task<List<P>> InitAsync<P, A>(Guid id, Guid tenantId, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(id, new PartitionKey(tenantId.ToString()), nostify, httpClient, pointInTime);
    }

    ///<summary>
    ///Initialize the Projections with the specified ids.  Will requery all needed data from all services.
    ///</summary>
    public async Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //Get all base aggregates in id list
        Container baseAggregateContainer = await nostify.GetCurrentStateContainerAsync<A>();
        List<A> baseAggregates = await baseAggregateContainer.GetItemLinqQueryable<A>().Where(x => idsToInit.Contains(x.id)).ReadAllAsync();
        //Create list of all projections to init
        List<P> projectionList = baseAggregates.Select(a => JsonConvert.DeserializeObject<P>(JsonConvert.SerializeObject(a))).ToList();
        //Call Init
        return await InitAsync<P>(projectionList, nostify, httpClient, pointInTime);
    }

    ///<summary>
    ///Initialize the Projections with the specified ids, scoped to the given partition key. Will requery all needed data from all services.
    ///</summary>
    public async Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, PartitionKey partitionKey, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        Container baseAggregateContainer = await nostify.GetCurrentStateContainerAsync<A>();
        var requestOptions = new QueryRequestOptions { PartitionKey = partitionKey };
        List<A> baseAggregates = await baseAggregateContainer.GetItemLinqQueryable<A>(requestOptions: requestOptions).Where(x => idsToInit.Contains(x.id)).ReadAllAsync();
        List<P> projectionList = baseAggregates.Select(a => JsonConvert.DeserializeObject<P>(JsonConvert.SerializeObject(a))).ToList();
        return await InitAsync<P>(projectionList, nostify, httpClient, pointInTime);
    }

    ///<summary>
    ///Initialize the Projections with the specified ids, scoped to the given tenant. Will requery all needed data from all services.
    ///</summary>
    public async Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, Guid tenantId, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(idsToInit, new PartitionKey(tenantId.ToString()), nostify, httpClient, pointInTime);
    }

    /// <summary>
    /// Initializes a list of projections asynchronously. Will requery all needed data from all external services, set <c>initialized = true</c> and update projection container. 
    /// </summary>
    /// <param name="projectionsToInit">List of projections to initialize.</param>
    /// <param name="nostify">Reference to the Nostify singleton.</param>
    /// <param name="httpClient">Optional HttpClient instance for making HTTP requests.</param>
    /// <param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of initialized projections of type T.</returns>
    public async Task<List<P>> InitAsync<P>(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();

        //Get all external data events
        List<ExternalDataEvent> externalDataEvents = await P.GetExternalDataEventsAsync(projectionsToInit, nostify, httpClient, pointInTime);
        //Flatten all Events into a single ExternalDataEvent if id's match
        externalDataEvents = externalDataEvents.GroupBy(x => x.aggregateRootId).Select(x => new ExternalDataEvent(x.Key, x.SelectMany(y => y.events).ToList())).ToList();
        //Apply each event to it's respective projection matching on aggregateRootId == id
        List<P> initializedProjections = new List<P>();
        projectionsToInit.ForEach(p =>
        {
            P initInProcess = p;
            List<Event> eventsToApplyToThisProjection = externalDataEvents.Where(e => e.aggregateRootId == p.id).FirstOrDefault()?.events ?? new List<Event>();
            eventsToApplyToThisProjection.ForEach(e => initInProcess.Apply(e));
            initInProcess.initialized = true;
            initializedProjections.Add(initInProcess);
        });

        //Bulk upsert all projections
        await projectionContainer.DoBulkUpsertAsync<P>(initializedProjections);
        return initializedProjections;
    }

    ///<summary>
    ///Recreate container for this Projection.  
    ///Will delete container and recreate it then will query the specified base Aggregate where isDeleted == false and populate all matching properties in the projection. 
    ///<para>
    ///Will loop through all items in the container in batches of <c>loopSize</c> and call InitAsync on each batch to query all needed Events from all external services and update projection container.
    ///</para>
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    ///<param name="partitionKeyPath">Path to the partition key.  Defaults to "/tenantId".</param>
    ///<param name="loopSize">Number of items to init at a time.  Defaults to 1000.</param>
    ///<param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    public async Task InitContainerAsync<P, A>(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //Delete all items from container
        Container deleteAllFromThis = await nostify.GetBulkProjectionContainerAsync<P>(partitionKeyPath);
        int deleteResult = await deleteAllFromThis.DeleteAllBulkAsync<P>();

        //Get all Events from eventStore for base Aggregates
        Container eventStoreContainer = await nostify.GetEventStoreContainerAsync();
        //Get ids of all non deleted base Aggregates
        Container baseAggregateContainer = await nostify.GetCurrentStateContainerAsync<A>(partitionKeyPath);
        List<Guid> baseAggregateIds = await baseAggregateContainer.GetItemLinqQueryable<A>().Where(x => !x.isDeleted).Select(x => x.id).ReadAllAsync();

        //Loop through specified number at a time and get all events for each base Aggregate and apply them to a new projection instance
        //Doing this to avoid getting too much data
        for (int i = 0; i < baseAggregateIds.Count; i += loopSize)
        {
            List<P> projectionList = new List<P>();
            List<Guid> ids = baseAggregateIds.Skip(i).Take(loopSize).ToList();
            
            var eventsQuery = eventStoreContainer.GetItemLinqQueryable<Event>().Where(x => ids.Contains(x.aggregateRootId));
            
            // Apply pointInTime filter if provided
            if (pointInTime.HasValue)
            {
                eventsQuery = eventsQuery.Where(x => x.timestamp <= pointInTime.Value);
            }
            
            List<Event> events = await eventsQuery.ReadAllAsync();
            
            ids.ForEach(id =>
            {
                P newProjection = new P();
                events.Where(e => e.aggregateRootId == id).ToList().ForEach(e => newProjection.Apply(e));
                projectionList.Add(newProjection);
            });
            //Call InitAsync
            await InitAsync(projectionList, nostify, httpClient, pointInTime);
        }

    }

    ///<summary>
    ///Recreate all items in the specified partition of the Projection container.
    ///Will delete existing items in that partition then will query the specified base Aggregate where isDeleted == false and populate all matching properties in the projection.
    ///<para>
    ///Will loop through all items in batches of <c>loopSize</c> and call InitAsync on each batch to query all needed Events from all external services and update projection container.
    ///</para>
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="partitionKey">The partition key value to scope this operation to.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    ///<param name="partitionKeyPath">Path to the partition key.  Defaults to "/tenantId".</param>
    ///<param name="loopSize">Number of items to init at a time.  Defaults to 1000.</param>
    ///<param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    public async Task InitContainerAsync<P, A>(INostify nostify, PartitionKey partitionKey, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        var requestOptions = new QueryRequestOptions { PartitionKey = partitionKey };

        // Delete all items in the specified partition
        Container deleteAllFromThis = await nostify.GetBulkProjectionContainerAsync<P>(partitionKeyPath);
        List<P> existingProjections = await deleteAllFromThis.GetItemLinqQueryable<P>(requestOptions: requestOptions).ReadAllAsync();
        await deleteAllFromThis.BulkDeleteAsync(existingProjections);

        // Get event store and base aggregate IDs for this partition
        Container eventStoreContainer = await nostify.GetEventStoreContainerAsync();
        Container baseAggregateContainer = await nostify.GetCurrentStateContainerAsync<A>(partitionKeyPath);
        List<Guid> baseAggregateIds = await baseAggregateContainer.GetItemLinqQueryable<A>(requestOptions: requestOptions).Where(x => !x.isDeleted).Select(x => x.id).ReadAllAsync();

        for (int i = 0; i < baseAggregateIds.Count; i += loopSize)
        {
            List<P> projectionList = new List<P>();
            List<Guid> ids = baseAggregateIds.Skip(i).Take(loopSize).ToList();

            var eventsQuery = eventStoreContainer.GetItemLinqQueryable<Event>().Where(x => ids.Contains(x.aggregateRootId));

            if (pointInTime.HasValue)
            {
                eventsQuery = eventsQuery.Where(x => x.timestamp <= pointInTime.Value);
            }

            List<Event> events = await eventsQuery.ReadAllAsync();

            ids.ForEach(id =>
            {
                P newProjection = new P();
                events.Where(e => e.aggregateRootId == id).ToList().ForEach(e => newProjection.Apply(e));
                projectionList.Add(newProjection);
            });
            await InitAsync(projectionList, nostify, httpClient, pointInTime);
        }
    }

    ///<summary>
    ///Recreate all items in the given tenant's partition of the Projection container.
    ///Will delete existing items in that partition then will query the specified base Aggregate where isDeleted == false and populate all matching properties in the projection.
    ///<para>
    ///Will loop through all items in batches of <c>loopSize</c> and call InitAsync on each batch to query all needed Events from all external services and update projection container.
    ///</para>
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="tenantId">The tenant id to scope this operation to.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    ///<param name="partitionKeyPath">Path to the partition key.  Defaults to "/tenantId".</param>
    ///<param name="loopSize">Number of items to init at a time.  Defaults to 1000.</param>
    ///<param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    public async Task InitContainerAsync<P, A>(INostify nostify, Guid tenantId, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await InitContainerAsync<P, A>(nostify, new PartitionKey(tenantId.ToString()), httpClient, partitionKeyPath, loopSize, pointInTime);
    }

    ///<summary>
    ///Init all non-initialized projections in the container.  Will requery all needed data from all external services by calling InitAsync  
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    /// <param name="maxloopSize">Maximum size of loops to check for uninitialized projections. Defaults to 10.</param>
    /// <param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    public async Task InitAllUninitialized<P>(INostify nostify, HttpClient? httpClient = null, int maxloopSize = 100, DateTime? pointInTime = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //Query for all projections in container where initialized == false
        Container projectionContainer = await nostify.GetProjectionContainerAsync<P>();
        List<P> projections = await projectionContainer.GetItemLinqQueryable<P>().Where(x => x.initialized == false).ReadAllAsync();

        //Call InitAsync until all projections are initialized, must call in a loop due to async creation of projections
        while (projections.Count > 0)
        {
            await InitAsync(projections, nostify, httpClient, pointInTime);
            projections = projections.Where(x => x.initialized == false).ToList();
            //If projections == 0 wait a second then check again to see if any new projections were created
            if (projections.Count == 0)
            {
                await Task.Delay(1000);
                projections = await projectionContainer.GetItemLinqQueryable<P>().Where(x => x.initialized == false).ReadAllAsync();
            }
        }
    }

    ///<summary>
    ///Init all non-initialized projections in the given partition.  Will requery all needed data from all external services by calling InitAsync  
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="partitionKey">The partition key value to scope this operation to.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    ///<param name="maxloopSize">Maximum size of loops to check for uninitialized projections. Defaults to 100.</param>
    ///<param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    public async Task InitAllUninitialized<P>(INostify nostify, PartitionKey partitionKey, HttpClient? httpClient = null, int maxloopSize = 100, DateTime? pointInTime = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        var requestOptions = new QueryRequestOptions { PartitionKey = partitionKey };
        Container projectionContainer = await nostify.GetProjectionContainerAsync<P>();
        List<P> projections = await projectionContainer.GetItemLinqQueryable<P>(requestOptions: requestOptions).Where(x => x.initialized == false).ReadAllAsync();

        while (projections.Count > 0)
        {
            await InitAsync(projections, nostify, httpClient, pointInTime);
            projections = projections.Where(x => x.initialized == false).ToList();
            if (projections.Count == 0)
            {
                await Task.Delay(1000);
                projections = await projectionContainer.GetItemLinqQueryable<P>(requestOptions: requestOptions).Where(x => x.initialized == false).ReadAllAsync();
            }
        }
    }

    ///<summary>
    ///Init all non-initialized projections for the given tenant.  Will requery all needed data from all external services by calling InitAsync  
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="tenantId">The tenant id to scope this operation to.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    ///<param name="maxloopSize">Maximum size of loops to check for uninitialized projections. Defaults to 100.</param>
    ///<param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    public async Task InitAllUninitialized<P>(INostify nostify, Guid tenantId, HttpClient? httpClient = null, int maxloopSize = 100, DateTime? pointInTime = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await InitAllUninitialized<P>(nostify, new PartitionKey(tenantId.ToString()), httpClient, maxloopSize, pointInTime);
    }

    // Backward compatibility overloads
    public async Task<List<P>> InitAsync<P, A>(Guid id, INostify nostify, HttpClient? httpClient = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(id, nostify, httpClient, null);
    }

    public async Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(idsToInit, nostify, httpClient, null);
    }

    public async Task<List<P>> InitAsync<P>(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P>(projectionsToInit, nostify, httpClient, null);
    }

    public async Task InitContainerAsync<P, A>(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await InitContainerAsync<P, A>(nostify, httpClient, partitionKeyPath, loopSize, null);
    }

    public async Task InitAllUninitialized<P>(INostify nostify, HttpClient? httpClient = null, int maxloopSize = 10) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await InitAllUninitialized<P>(nostify, httpClient, maxloopSize, null);
    }
}