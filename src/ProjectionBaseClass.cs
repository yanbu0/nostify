
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
///Projections must implement this base class, <see cref="P"/> is the concrete type and <see cref="A"/> is the base Aggregate for the Projection.  
///</summary>
public abstract class ProjectionBaseClass<P,A> : NostifyObject, IProjection<P> where P : ProjectionBaseClass<P,A>, IContainerName, IHasExternalData<P>, new() where A : NostifyObject, IAggregate
{
    ///<inheritdoc/>
    public bool initialized { get; set; } = false;

    ///<inheritdoc/>
    public int ttl { get; set; } = -1;


    ///<inheritdoc />
    public async Task<P> InitAsync(INostify nostify, HttpClient? httpClient = null)
    {
        P initProj = (await InitAsync(new List<P>() { this as P }, nostify, httpClient)).FirstOrDefault();
        return initProj;
    }

    ///<inheritdoc />
    public static async Task<List<P>> InitAsync(Guid id, INostify nostify, HttpClient? httpClient = null)
    {
        return await InitAsync(new List<Guid> { id }, nostify, httpClient);
    }

    ///<inheritdoc />
    public async static Task<List<P>> InitAsync(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null)
    {
        //Get all base aggregates in id list
        Container baseAggregateContainer = nostify.GetCurrentStateContainerAsync<A>().Result;
        List<A> baseAggregates = await baseAggregateContainer.GetItemLinqQueryable<A>().Where(x => idsToInit.Contains(x.id)).ReadAllAsync();
        //Create list of all projections to init
        var projectionList = baseAggregates.Select(a => JsonConvert.DeserializeObject<P>(JsonConvert.SerializeObject(a))).ToList();
        //Call Init
        return await InitAsync(projectionList, nostify, httpClient);
    }

    ///<inheritdoc />
    public async static Task<List<P>> InitAsync(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null) 
    {
        Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();

        //Get all external data events
        List<ExternalDataEvent> externalDataEvents = await P.GetExternalDataEventsAsync(projectionsToInit, nostify, httpClient);
        //Flatten all Events into a single ExternalDataEvent if id's match
        externalDataEvents = externalDataEvents.GroupBy(x => x.aggregateRootId).Select(x => new ExternalDataEvent(x.Key, x.SelectMany(y => y.events).ToList() )).ToList();
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

    ///<inheritdoc />
    public async static Task InitContainerAsync(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 100)
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
        for(int i = 0; i < baseAggregateIds.Count; i += loopSize)
        {
            List<P> projectionList = new List<P>();
            List<Guid> ids = baseAggregateIds.Skip(i).Take(loopSize).ToList();
            List<Event> events = await eventStoreContainer.GetItemLinqQueryable<Event>().Where(x => ids.Contains(x.aggregateRootId)).ReadAllAsync();
            ids.ForEach(id =>
            {
                P newProjection = new P();
                events.Where(e => e.aggregateRootId == id).ToList().ForEach(e => newProjection.Apply(e));
                projectionList.Add(newProjection);
            });
            //Call InitAsync
            await InitAsync(projectionList, nostify, httpClient);
        }

    }

    ///<inheritdoc />
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