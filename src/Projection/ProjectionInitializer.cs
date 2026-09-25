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
    private readonly IQueryExecutor _queryExecutor;
    private readonly Func<Container, RetryOptions, IRetryableContainer> _retryableContainerFactory;
    private readonly Func<TimeSpan, Task> _delayAsync;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectionInitializer"/> class.
    /// </summary>
    public ProjectionInitializer()
        : this(
            CosmosQueryExecutor.Default,
            static (container, options) => container.WithRetry(options),
            static delay => Task.Delay(delay))
    {
    }

    /// <summary>
    /// Initializes an instance with deterministic infrastructure adapters for testing.
    /// </summary>
    /// <param name="queryExecutor">Executes Cosmos LINQ queries.</param>
    /// <param name="retryableContainerFactory">Creates retry-enabled persistence wrappers.</param>
    /// <param name="delayAsync">Schedules the stabilization delay between container checks.</param>
    internal ProjectionInitializer(
        IQueryExecutor queryExecutor,
        Func<Container, RetryOptions, IRetryableContainer> retryableContainerFactory,
        Func<TimeSpan, Task> delayAsync)
    {
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
        _retryableContainerFactory = retryableContainerFactory
            ?? throw new ArgumentNullException(nameof(retryableContainerFactory));
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
    }

    ///<summary>
    ///Initialize the Projection with the specified id.  Will requery all needed data from all services.
    ///</summary>
    public async Task<List<P>> InitAsync<P, A>(Guid id, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(new List<Guid> { id }, nostify, httpClient, pointInTime);
    }

    ///<summary>
    ///Initialize the Projections with the specified ids.  Will requery all needed data from all services.
    ///</summary>
    public async Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //Get all base aggregates in id list
        Container baseAggregateContainer = await nostify.GetCurrentStateContainerAsync<A>();
        IQueryable<A> baseAggregateQuery = baseAggregateContainer
            .GetItemLinqQueryable<A>()
            .Where(x => idsToInit.Contains(x.id));
        List<A> baseAggregates = await _queryExecutor.ReadAllAsync(baseAggregateQuery);
        // Create projections only for aggregates that deserialize successfully.
        List<P> projectionList = baseAggregates
            .Select(a => JsonConvert.DeserializeObject<P>(JsonConvert.SerializeObject(a)))
            .OfType<P>()
            .ToList();
        //Call Init
        return await InitAsync<P>(projectionList, nostify, httpClient, pointInTime);
    }

    /// <summary>
    /// Initializes a list of projections asynchronously. Will requery all needed data from all external services, set <c>initialized = true</c> and update projection container. 
    /// </summary>
    /// <param name="projectionsToInit">List of projections to initialize.</param>
    /// <param name="nostify">Reference to the Nostify singleton.</param>
    /// <param name="httpClient">Optional HttpClient instance for making HTTP requests.</param>
    /// <param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    /// <param name="retryOptions">Retry options for CosmosDB. Defaults to default <see cref="RetryOptions"/> when null.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of initialized projections of type T.</returns>
    public async Task<List<P>> InitAsync<P>(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null, RetryOptions? retryOptions = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
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
            List<Event> eventsToApplyToThisProjection = externalDataEvents.FirstOrDefault(e => e.aggregateRootId == p.id)?.events ?? new List<Event>();
            eventsToApplyToThisProjection.ForEach(e => initInProcess.Apply(e));
            initInProcess.initialized = true;
            initializedProjections.Add(initInProcess);
        });

        //Bulk upsert all projections
        IRetryableContainer retryableContainer = _retryableContainerFactory(
            projectionContainer,
            retryOptions ?? new RetryOptions());
        await retryableContainer.DoBulkUpsertAsync<P>(initializedProjections);
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
    ///<param name="loopSize">Number of items to init at a time.  Defaults to 100.</param>
    ///<param name="pointInTime">Point in time to query external data up to. If null, queries current state.</param>
    public async Task InitContainerAsync<P, A>(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 100, DateTime? pointInTime = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //Delete all items from container
        Container deleteAllFromThis = await nostify.GetBulkProjectionContainerAsync<P>(partitionKeyPath);
        await DeleteAllProjectionsAsync<P>(deleteAllFromThis);

        //Get all Events from eventStore for base Aggregates
        Container eventStoreContainer = await nostify.GetEventStoreContainerAsync();
        //Get ids of all non deleted base Aggregates
        Container baseAggregateContainer = await nostify.GetCurrentStateContainerAsync<A>(partitionKeyPath);
        IQueryable<Guid> baseAggregateIdsQuery = baseAggregateContainer
            .GetItemLinqQueryable<A>()
            .Where(x => !x.isDeleted)
            .Select(x => x.id);
        List<Guid> baseAggregateIds = await _queryExecutor.ReadAllAsync(baseAggregateIdsQuery);

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

            List<Event> events = await _queryExecutor.ReadAllAsync(eventsQuery);

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

    /// <summary>
    /// Deletes all existing projections before a container rebuild.
    /// </summary>
    /// <typeparam name="P">The projection type to delete.</typeparam>
    /// <param name="container">The projection container to clear.</param>
    /// <returns>The number of projections marked for deletion.</returns>
    internal virtual Task<int> DeleteAllProjectionsAsync<P>(Container container)
        where P : NostifyObject
    {
        return container.DeleteAllBulkAsync<P>();
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
        IQueryable<P> uninitializedQuery = projectionContainer
            .GetItemLinqQueryable<P>()
            .Where(x => x.initialized == false);
        List<P> projections = await _queryExecutor.ReadAllAsync(uninitializedQuery);

        //Call InitAsync until all projections are initialized, must call in a loop due to async creation of projections
        while (projections.Count > 0)
        {
            await InitAsync(projections, nostify, httpClient, pointInTime);
            projections = projections.Where(x => x.initialized == false).ToList();
            //If projections == 0 wait a second then check again to see if any new projections were created
            if (projections.Count == 0)
            {
                await _delayAsync(TimeSpan.FromSeconds(1));
                uninitializedQuery = projectionContainer
                    .GetItemLinqQueryable<P>()
                    .Where(x => x.initialized == false);
                projections = await _queryExecutor.ReadAllAsync(uninitializedQuery);
            }
        }
    }

    // Backward compatibility overloads
    /// <inheritdoc cref="InitAsync{P, A}(Guid, INostify, HttpClient, DateTime?)" />
    public async Task<List<P>> InitAsync<P, A>(Guid id, INostify nostify, HttpClient? httpClient = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(id, nostify, httpClient, null);
    }

    /// <inheritdoc cref="InitAsync{P, A}(List{Guid}, INostify, HttpClient, DateTime?)" />
    public async Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P, A>(idsToInit, nostify, httpClient, null);
    }

    /// <inheritdoc cref="InitAsync{P}(List{P}, INostify, HttpClient, DateTime?, RetryOptions?)" />
    public async Task<List<P>> InitAsync<P>(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await InitAsync<P>(projectionsToInit, nostify, httpClient, null);
    }

    /// <inheritdoc cref="InitContainerAsync{P, A}(INostify, HttpClient, string, int, DateTime?)" />
    public async Task InitContainerAsync<P, A>(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 100) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await InitContainerAsync<P, A>(nostify, httpClient, partitionKeyPath, loopSize, null);
    }

    /// <inheritdoc cref="InitAllUninitialized{P}(INostify, HttpClient, int, DateTime?)" />
    public async Task InitAllUninitialized<P>(INostify nostify, HttpClient? httpClient = null, int maxloopSize = 10) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await InitAllUninitialized<P>(nostify, httpClient, maxloopSize, null);
    }
}