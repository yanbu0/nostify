
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using nostify;

namespace nostify;

///<summary>
///Projections must implement this interface and <c>IProjection</c>, <see cref="P"/> is the concrete type and <see cref="A"/> is the base Aggregate for the Projection.  
///</summary>
public interface IInitializable<P,A> : IUniquelyIdentifiable where P : class, IApplyable, IInitializable<P,A>, IProjection, new() where A : NostifyObject, IAggregate
{
    
    private static bool _initialized = false;
    ///<summary>
    ///If Projection is initialized or not. Set to false by default, should be set to true after <c>InitAsync()</c> is called.
    ///</summary>
    public bool initialized { get { return _initialized; } set { _initialized = value; } }

    ///<summary>
    ///Initialize this instance of the Projection.  Will requery all needed external data.
    ///</summary>
    public async Task<P> InitAsync(INostify nostify, HttpClient? httpClient = null)
    {
        P initProj = (await InitAsync(new List<P>() { this as P }, nostify, httpClient)).FirstOrDefault();
        return initProj;
    }

    ///<summary>
    ///Initialize the Projection with the specified id.  Will requery all needed data from all services.
    ///<returns>Testing</returns>
    ///</summary>
    public static async Task<List<P>> InitAsync(Guid id, INostify nostify, HttpClient? httpClient = null)
    {
        return await InitAsync(new List<Guid> { id }, nostify, httpClient);
    }

    ///<summary>
    ///Initialize the Projections with the specified ids.  Will requery all needed data from all services.
    ///</summary>
    ///<para>
    ///Must contain all queries to get any necessary values from Aggregates external to base Projection.  Should save using bulk update pattern.
    ///</para>
    public async static Task<List<P>> InitAsync(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null)
    {
        //Get all base aggregates in id list
        Container baseAggregateContainer = nostify.GetCurrentStateContainerAsync<A>().Result;
        List<A> baseAggregates = baseAggregateContainer.GetItemLinqQueryable<A>().Where(x => idsToInit.Contains(x.id)).ToList();
        //Create list of all projections to init
        var projectionList = baseAggregates.Select(a => JsonConvert.DeserializeObject<P>(JsonConvert.SerializeObject(a))).ToList();
        //Call Init
        return await InitAsync(projectionList, nostify, httpClient);
    }

    /// <summary>
    /// Initializes a list of projections asynchronously. Will requery all needed data from all external services, set <c>initialized = true</c> and update projection container. 
    /// </summary>
    /// <param name="projectionsToInit">List of projections to initialize.</param>
    /// <param name="nostify">Reference to the Nostify singleton.</param>
    /// <param name="httpClient">Optional HttpClient instance for making HTTP requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of initialized projections of type T.</returns>
    public async static Task<List<P>> InitAsync(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null) 
    {
        Container projectionContainer = await nostify.GetProjectionContainerAsync<P>();

        //Get all external data events
        List<ExternalDataEvent> externalDataEvents = await P.GetExternalDataEventsAsync(projectionsToInit, nostify, httpClient);
        //Apply each event to it's respective projection matching on aggregateRootId == id
        List<P> initializedProjections = new List<P>();
        projectionsToInit.ForEach(p =>
        {
            P initInProcess = p;
            List<Event> eventsToApplyToThisProjection = externalDataEvents.Where(e => e.aggregateRootId == p.id).SelectMany(e => e.events).ToList();
            eventsToApplyToThisProjection.ForEach(e => initInProcess.Apply(e));
            initInProcess.initialized = true;
            initializedProjections.Add(initInProcess);
        });
        
        //Bulk upsert all projections
        await projectionContainer.DoBulkUpsertAsync<P>(initializedProjections);
        return initializedProjections;
    }

    ///<summary>
    ///Queries any neccessary external data to create a list of Events to update all of the projections in the param. Gets called in InitAsync.
    ///Must return an Event for each Projection in the list which, when Apply is called, will update the Projection to the current state.
    ///Must set <c>initialized</c> to true after each projection is updated.
    ///</summary>
    ///<param name="projectionsToInit">List of projections to query external data for. If empty, will update </param>
    ///<param name="nostify">Nostify instance to use to get current state containers</param>
    ///<param name="httpClient">HttpClient to use to query external data. Can be null if no events external to this service are needed.</param>
    ///<param name="pointInTime">Point in time to query external data.  If null, will query current state. Use when pulling a previous point in time state of Projection.</param>
    public abstract static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<P> projectionsToInit, 
                                                            INostify nostify, 
                                                            HttpClient? httpClient = null, 
                                                            DateTime? pointInTime = null);

    ///<summary>
    ///Recreate container for this Projection.  
    ///<para>Will delete container and recreate it then will query the specified base Aggregate where isDeleted == false and populate all matching properties in the projection. 
    ///Will then call InitAsync to requery all needed data from all external services and update projection container.
    ///</para>
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    ///<param name="partitionKeyPath">Path to the partition key.  Defaults to "/tenantId".</param>
    public async static Task InitContainerAsync(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId")
    {
        string containerName = P.containerName;
        //Delete container
        Container deleteContainer = await nostify.GetContainerAsync(containerName, false, partitionKeyPath);
        _ = await deleteContainer.DeleteContainerAsync();

        //Recreate container
        Container createContainer = await nostify.GetContainerAsync(containerName, true, partitionKeyPath);

        //Get all non deleted base Aggregates
        Container baseAggregateContainer = await nostify.GetCurrentStateContainerAsync<A>(partitionKeyPath);
        List<A> baseAggregates = await baseAggregateContainer.GetItemLinqQueryable<A>().Where(x => x.isDeleted == false).ReadAllAsync();
        //Create list of all projections to init
        var projectionList = baseAggregates.Select(a => JsonConvert.DeserializeObject<P>(JsonConvert.SerializeObject(a))).ToList();

        //Call InitAsync
        await InitAsync(projectionList, nostify, httpClient);
    }

    ///<summary>
    ///Init all non-initialized projections in the container.  Will requery all needed data from all external services by calling InitAsync  
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    public async static Task InitAllUninitialized(INostify nostify, HttpClient? httpClient = null)
    {
        string containerName = P.containerName;
        //Query for all projections in container where initialized == false
        Container projectionContainer = await nostify.GetProjectionContainerAsync<P>();
        List<P> projections = await projectionContainer.GetItemLinqQueryable<P>().Where(x => x.initialized == false).ReadAllAsync();

        //Call InitAsync
        await InitAsync(projections, nostify, httpClient);
    }
}