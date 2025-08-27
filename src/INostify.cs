using Microsoft.Azure.Cosmos;
using Confluent.Kafka;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Net.Http;

namespace nostify;

///<summary>
///Defines Nostify interface
///</summary>
public interface INostify
{
    ///<summary>
    ///Event store repository
    ///</summary>
    NostifyCosmosClient Repository { get; }

    ///<summary>
    ///Default partition key
    ///</summary>
    string DefaultPartitionKeyPath { get; }

    ///<summary>
    ///Default tenant id value for use if NOT implementing multi-tenant
    ///</summary>
    Guid DefaultTenantId { get; }

    ///<summary>
    ///Url for Kafka cluster
    ///</summary>
    string KafkaUrl { get; }

    ///<summary>
    /// For creating internal HttpClient instances
    /// </summary>
    IHttpClientFactory HttpClientFactory { get; }

    ///<summary>
    ///Kafka producer
    ///</summary>
    IProducer<string, string> KafkaProducer { get; }

    ///<summary>
    /// Projection initializer for Projections that require external data to be queried to update the projection.
    /// This property is used to initialize projections with external data events.
    ///</summary>
    IProjectionInitializer ProjectionInitializer { get; }

    ///<summary>
    ///Writes event to event store
    ///</summary>        
    ///<param name="eventToPersist">Event to apply and persist in event store</param>
    public Task PersistEventAsync(IEvent eventToPersist);

    /// <summary>
    /// Applies and persists an event to a list of projections in the specified container.
    /// </summary>
    /// <remarks>
    /// This method applies the given event to each projection in the list, updates their state,
    /// and persists the changes to the specified container. Primarily intended for updates
    /// when an event affects multiple projections.
    /// </remarks>
    /// <param name="bulkContainer">The container to which the event will be applied and persisted.</param>
    /// <param name="eventToApply">The event to be applied and persisted.</param>
    /// <param name="projectionIds">The list of projection IDs to which the event will be applied.</param>
    /// <param name="batchSize">Optional. Number of projections to update in a batch. Default is 100.</param>
    /// <typeparam name="P">The type of the Nostify object.</typeparam>
    /// <returns>The nostify objects after Events are Applied</returns>
    public Task<List<P>> MultiApplyAndPersistAsync<P>(Container bulkContainer, Event eventToApply, List<Guid> projectionIds, int batchSize = 100) where P : NostifyObject, new();

    /// <summary>
    /// Applies and persists an event to a list of projections in the specified container.
    /// </summary>
    /// <remarks>
    /// This method applies the given event to each projection in the list, updates their state,
    /// and persists the changes to the specified container. Primarily intended for updates
    /// when an event affects multiple projections. You can use this overload when you already have the projection objects,
    /// but there is an overload where you only need to pass a list of the IDs, which is probably what you want to do most of the time.
    /// </remarks>
    /// <param name="bulkContainer">The container to which the event will be applied and persisted.</param>
    /// <param name="eventToApply">The event to be applied and persisted.</param>
    /// <param name="projectionsToUpdate">The list of projections to which the event will be applied.</param>
    /// <param name="batchSize">Optional. Number of projections to update in a batch. Default is 100.</param>
    /// <typeparam name="P">The type of the Nostify object.</typeparam>
    /// <returns>The nostify objects after Events are Applied</returns>
    public Task<List<P>> MultiApplyAndPersistAsync<P>(Container bulkContainer, Event eventToApply, List<P> projectionsToUpdate, int batchSize = 100) where P : NostifyObject, new();

    ///<summary>
    ///Applies and persists a bulk array of events from Kafka to the specified container.
    ///</summary>
    ///<param name="container">The container to which the events will be applied and persisted.</param>
    ///<param name="idPropertyName">The name of the property with either the single Guid id value or a List<Guid> to find the projection to apply to</param>
    ///<param name="events">The events to be applied and persisted.</param>
    ///<param name="allowRetry">Optional. If true, will retry on TooManyRequests error. Default is false.</param>
    ///<param name="publishErrorEvents">Optional. If true, will publish error events to Kafka as well as write to undeliverableEvents container. Default is false.</param>
    ///<typeparam name="P">The type of the Nostify object.</typeparam>
    ///<returns>The nostify objects after Events are Applied</returns>   
    public Task<List<P>> BulkApplyAndPersistAsync<P>(Container container, string idPropertyName, string[] events, bool allowRetry = false, bool publishErrorEvents = false) where P : NostifyObject, new();

    ///<summary>
    ///Writes event to event store
    ///</summary>        
    ///<param name="events">Events to apply and persist in event store</param>
    ///<param name="batchSize">Optional. Number of events to write in a batch.  If null, writes all events in one batch.</param>
    ///<param name="allowRetry">Optional. If true, will retry on TooManyRequests error.  Default is false.</param>
    ///<param name="publishErrorEvents">Optional. If true, will publish error events to Kafka as well as write to undeliverableEvents container.  Default is false.</param>
    public Task BulkPersistEventAsync(List<IEvent> events, int? batchSize = null, bool allowRetry = false, bool publishErrorEvents = false);

    ///<summary>
    ///Writes Event to the undeliverable events container. Use for handling errors to prevent constant retry.
    ///</summary>
    ///<param name="functionName">Name of function that failed, should be able to trace failure back to Azure function</param>
    ///<param name="errorMessage">Error message to capture</param>
    ///<param name="eventToHandle">The event that failed to process</param>
    ///<param name="errorCommand">Optional. The command that failed, if null will not publish to Kafka</param>
    public Task HandleUndeliverableAsync(string functionName, string errorMessage, IEvent eventToHandle, ErrorCommand? errorCommand = null);

    ///<summary>
    ///Published event to messaging bus
    ///</summary>        
    ///<param name="cosmosTriggerOutput">String output from CosmosDBTrigger, will be processed into a List of Events and published to Kafka</param>
    public Task PublishEventAsync(string cosmosTriggerOutput);

    ///<summary>
    ///Published event to messaging bus
    ///</summary>
    ///<param name="peList">List of Events to publish to Kafka</param>
    ///<param name="showOutput">Optional. If true, will write to console the output of each event published.  Default is false.</param>
    public Task PublishEventAsync(List<IEvent> peList, bool showOutput = false);

    ///<summary>
    ///Published event to messaging bus
    ///</summary>
    ///<param name="eventToPublish">Event to publish to Kafka</param>
    public Task PublishEventAsync(IEvent eventToPublish);

    ///<summary>
    ///Retrieves the event store container
    ///</summary>
    /// <param name="allowBulk">If true, will allow bulk operations</param>
    public Task<Container> GetEventStoreContainerAsync(bool allowBulk = false);

    ///<summary>    
    ///Retrieves the current state container for Aggregate Root
    ///</summary>
    ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
    public Task<Container> GetCurrentStateContainerAsync<A>(string partitionKeyPath = "/tenantId") where A : IAggregate;

    ///<summary>    
    ///Retrieves the current state container for Aggregate Root with bulk function turned on.
    ///</summary>
    ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
    public Task<Container> GetBulkCurrentStateContainerAsync<A>(string partitionKeyPath = "/tenantId") where A : IAggregate;

    ///<summary>
    ///Retrieves the container for specified Projection
    ///</summary>
    ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
    public Task<Container> GetProjectionContainerAsync<P>(string partitionKeyPath = "/tenantId") where P : IProjection;

    ///<summary>
    ///Retrieves the container for specified Projection with bulk function turned on
    ///</summary>
    ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
    public Task<Container> GetBulkProjectionContainerAsync<P>(string partitionKeyPath = "/tenantId") where P : IProjection;

    ///<<summary>
    ///Retrieves the undeliverable events container
    ///</summary>>
    public Task<Container> GetUndeliverableEventsContainerAsync();

    ///<summary>
    ///Retrieves the saga container
    ///</summary>
    public Task<Container> GetSagaContainerAsync();

    ///<summary>
    ///Retrieves the container.  Uses the knownContainers list to skip checks if container already exists. Will create if it doesn't exist and update knownContainers list.
    ///</summary>
    ///<param name="containerName">Name of container to retrieve</param>
    ///<param name="bulkEnabled">If bulk operations are enabled</param>
    ///<param name="partitionKeyPath">Path to partition key</param>
    public Task<Container> GetContainerAsync(string containerName, bool bulkEnabled, string partitionKeyPath);

    ///<summary>
    ///Rebuilds the entire container from event stream
    ///</summary>
    public Task RebuildCurrentStateContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : NostifyObject, IAggregate, new();

    ///<summary>
    ///Rehydrates data directly from stream of events when querying a Projection isn't feasible
    ///</summary>
    ///<returns>
    ///The aggregate state rehydrated from create to the specified DateTime.  If no DateTime provided, returns current state.
    ///</returns>
    ///<param name="id">The id (Guid) of the aggregate root to build a projection of</param>
    ///<param name="untilDate">Optional. Will build the aggregate state up to and including this time, if no value provided returns projection of current state</param>
    public Task<T> RehydrateAsync<T>(Guid id, DateTime? untilDate = null) where T : NostifyObject, IAggregate, new();

    ///<summary>
    ///Rehydrates data directly from stream of events and then calls the Projection InitAsync() function to pull any needed data from external services.
    ///</summary>
    ///<param name="id">The id (Guid) of the aggregate root to build a projection of</param>
    ///<param name="httpClient">Instance of HttpClient to query external data for Projection</param>
    ///<returns>
    ///The projection state rehydrated.  Projections may only be rehydrated to the current state.
    ///</returns>
    public Task<P> RehydrateAsync<P, A>(Guid id, HttpClient httpClient) where P : NostifyObject, IProjection, IHasExternalData<P>, new() where A : NostifyObject, IAggregate, new();

    ///<summary>
    ///Performs bulk upsert of list. Internally, creates a <c>List&lt;Task&gt;</c> for you by iterating over the <c>List&lt;T&gt;</c> and calling <c>UpsertItemAsync()</c> and then calls <c>Task.WhenAll()</c>.
    ///</summary>
    public Task DoBulkUpsertAsync<T>(Container container, List<T> itemList) where T : IApplyable;

    ///<summary>
    ///Performs bulk upsert of list. Internally, creates a <c>List&lt;Task&gt;</c> for you by iterating over the <c>List&lt;T&gt;</c> and calling <c>UpsertItemAsync()</c> and then calls <c>Task.WhenAll()</c>.
    ///</summary>
    public Task DoBulkUpsertAsync<T>(string containerName, List<T> itemList, string partitionKeyPath = "/tenantId") where T : IApplyable;

    /// <summary>
    /// Create the containers for the event store, aggregates, and projections if they don't exist; aggregates and projections are searched for in the calling assembly.
    /// </summary>
    /// <remarks>
    /// This method should only be called at startup (e.g. from Program.cs)
    /// </remarks>
    /// <param name="localhostOnly">Only create the database if running on localhost; default is true.</param>
    /// <param name="throughput">The throughput to use, will use the default if nul</param>
    /// <param name="verbose">If true, will write to console the steps taken to create the containers</param>
    public Task CreateContainersAsync<TTypeInAssembly>(bool localhostOnly = true, int? throughput = null, bool verbose = false);

    ///<summary>
    ///Initialize the Projection with the specified id.  Will requery all needed data from all services.
    ///Must have called WithHttp() in the NostifyFactory builder to use this method or it will throw an error.
    ///</summary>
    public Task<List<P>> InitAsync<P, A>(Guid id) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new();

    ///<summary>
    ///Initialize the Projections with the specified ids.  Will requery all needed data from all services.
    ///Must have called WithHttp() in the NostifyFactory builder to use this method or it will throw an error.
    ///</summary>
    public Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new();

    /// <summary>
    /// Initializes a list of projections asynchronously. Will requery all needed data from all external services, set <c>initialized = true</c> and update projection container. 
    /// Must have called WithHttp() in the NostifyFactory builder to use this method or it will throw an error.
    /// </summary>
    /// <param name="projectionsToInit">List of projections to initialize.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of initialized projections of type T.</returns>
    public Task<List<P>> InitAsync<P>(List<P> projectionsToInit) where P : NostifyObject, IProjection, IHasExternalData<P>, new();

    ///<summary>
    ///Recreate container for this Projection.  
    ///Will delete container and recreate it then will query the specified base Aggregate where isDeleted == false and populate all matching properties in the projection. 
    ///<para>
    ///Will loop through all items in the container in batches of <c>loopSize</c> and call InitAsync on each batch to query all needed Events from all external services and update projection container.
    ///Must have called WithHttp() in the NostifyFactory builder to use this method or it will throw an error.
    ///</para>
    ///</summary>
    ///<param name="partitionKeyPath">Path to the partition key.  Defaults to "/tenantId".</param>
    ///<param name="loopSize">Number of items to init at a time.  Defaults to 1000.</param>
    public Task InitContainerAsync<P, A>(string partitionKeyPath = "/tenantId", int loopSize = 1000) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new();

    ///<summary>
    ///Init all non-initialized projections in the container.  Will requery all needed data from all external services by calling InitAsync  
    ///</summary>
    /// <param name="maxloopSize">Maximum size of loops to check for uninitialized projections. Defaults to 10.</param>
    public Task InitAllUninitializedAsync<P>(int maxloopSize = 10) where P : NostifyObject, IProjection, IHasExternalData<P>, new();

}