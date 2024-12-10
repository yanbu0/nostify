using System;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Linq;
using Newtonsoft.Json;
using Confluent.Kafka;
using System.Net.Http;
using System.Reflection;

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
    ///Kafka producer
    ///</summary>
    IProducer<string, string> KafkaProducer { get; }

    ///<summary>
    ///Writes event to event store
    ///</summary>        
    ///<param name="eventToPersist">Event to apply and persist in event store</param>
    public Task PersistEventAsync(Event eventToPersist);

    ///<summary>
    ///Writes event to event store
    ///</summary>        
    ///<param name="events">Events to apply and persist in event store</param>
    public Task BulkPersistEventAsync(List<Event> events);

    ///<summary>
    ///Writes Event to the undeliverable events container. Use for handling errors to prevent constant retry.
    ///</summary>
    public Task HandleUndeliverableAsync(string functionName, string errorMessage, Event eventToHandle);

    ///<summary>
    ///Published event to messaging bus
    ///</summary>        
    ///<param name="cosmosTriggerOutput">String output from CosmosDBTrigger, will be processed into a List of Events and published to Kafka</param>
    public Task PublishEventAsync(string cosmosTriggerOutput);

    ///<summary>
    ///Retrieves the event store container
    ///</summary>
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
    public Task<Container> GetProjectionContainerAsync<P>(string partitionKeyPath = "/tenantId") where P : IContainerName;

    ///<summary>
    ///Retrieves the container for specified Projection with bulk function turned on
    ///</summary>
    ///<param name="partitionKeyPath">Path to parition key, unless not using tenants, leave default</param>
    public Task<Container> GetBulkProjectionContainerAsync<P>(string partitionKeyPath = "/tenantId") where P : IContainerName;

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
    public Task<P> RehydrateAsync<P,A>(Guid id, HttpClient httpClient) where P : ProjectionBaseClass<P,A>, IContainerName, IHasExternalData<P>, new() where A : NostifyObject, IAggregate, new();

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
    public Task CreateContainersAsync<TTypeInAssembly>(bool localhostOnly = true);

}

///<summary>
///Base class to utilize nostify.  Should inject as a singleton in HostBuilder().ConfigureServices() 
///</summary>
public class Nostify : INostify
{

    /// <inheritdoc />
    public NostifyCosmosClient Repository { get; }
    /// <inheritdoc />
    public string DefaultPartitionKeyPath { get; }
    /// <inheritdoc />
    public Guid DefaultTenantId { get; }
    /// <inheritdoc />
    public string KafkaUrl { get; }
    /// <inheritdoc />
    public IProducer<string, string> KafkaProducer { get; }
    

    ///<summary>
    ///Nostify constructor for development with no username and pwd for Kafka
    ///</summary>
    ///<param name="primaryKey">API Key value</param>
    ///<param name="dbName">Name of event store DB</param>
    ///<param name="cosmosEndpointUri">Uri of cosmos endpoint, format: https://[instance-name].documents.azure.us:443/</param>
    ///<param name="kafkaUrl">Url of Kafka instance, format: localhost:54165</param>
    ///<param name="defaultPartitionKeyPath">Path to partition key for default created current state container, set null to not create, leave default to partition by tenantId </param>
    ///<param name="defaultTenantId">Default tenant id value for use if NOT implementing multi-tenant, if left to default will create one partition in current state container per tenant</param>
    public Nostify(string primaryKey, string dbName, string cosmosEndpointUri, string kafkaUrl, string defaultPartitionKeyPath = "/tenantId", Guid defaultTenantId = default)
    {
        new Nostify(primaryKey, dbName, cosmosEndpointUri, kafkaUrl, null, null, defaultPartitionKeyPath, defaultTenantId);
    }

    ///<summary>
    ///Nostify constructor for production with username and pwd for Kafka
    ///</summary>
    ///<param name="primaryKey">API Key value</param>
    ///<param name="dbName">Name of event store DB</param>
    ///<param name="cosmosEndpointUri">Uri of cosmos endpoint, format: https://[instance-name].documents.azure.us:443/</param>
    ///<param name="kafkaUrl">Url of Kafka instance, format: localhost:54165</param>
    ///<param name="kafkaUserName">Username for Kafka</param>
    ///<param name="kafkaPassword">Password for Kafka</param>
    ///<param name="defaultPartitionKeyPath">Path to partition key for default created current state container, set null to not create, leave default to partition by tenantId </param>
    ///<param name="defaultTenantId">Default tenant id value for use if NOT implementing multi-tenant, if left to default will create one partition in current state container per tenant</param>
    public Nostify(string primaryKey, string dbName, string cosmosEndpointUri, string kafkaUrl, string kafkaUserName = null, string kafkaPassword = null, string defaultPartitionKeyPath = "/tenantId", Guid defaultTenantId = default)
    {
        Repository = new NostifyCosmosClient(primaryKey, dbName, cosmosEndpointUri);
        if (defaultPartitionKeyPath != null)
        {
            this.DefaultPartitionKeyPath = defaultPartitionKeyPath;
        }
        this.DefaultTenantId = defaultTenantId;
        this.KafkaUrl = kafkaUrl;

        bool isDeployed = !string.IsNullOrWhiteSpace(kafkaUserName) && !string.IsNullOrWhiteSpace(kafkaPassword);

        //Build producer instance
        var config = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("bootstrap.servers", KafkaUrl),
            new KeyValuePair<string, string>("session.timeout.ms", "45000"),
            new KeyValuePair<string, string>("client.id", $"Nostifyd-{dbName}-{Guid.NewGuid()}")
        };
        if (isDeployed)
        {
            config.Add(new KeyValuePair<string, string>("sasl.username", kafkaUserName));
            config.Add(new KeyValuePair<string, string>("sasl.password", kafkaPassword));
            config.Add(new KeyValuePair<string, string>("security.protocol", "SASL_SSL"));
            config.Add(new KeyValuePair<string, string>("sasl.mechanisms", "PLAIN"));
        }
        KafkaProducer = new ProducerBuilder<string,string>(config).Build();
    }


    /// <inheritdoc />
    public async Task PersistEventAsync(Event eventToPersist)
    {
        var eventContainer = await GetEventStoreContainerAsync();
        await eventContainer.CreateItemAsync(eventToPersist, eventToPersist.aggregateRootId.ToPartitionKey());
    }

    ///<inheritdoc />
    public async Task PublishEventAsync(string cosmosTriggerOutput)
    {
        var peList = JsonConvert.DeserializeObject<List<Event>>(cosmosTriggerOutput);
        if (peList != null)
        {
            foreach (Event pe in peList)
            {
                string topic = pe.command.name;
                var result = await KafkaProducer.ProduceAsync(topic, new Message<string, string>{  Value = JsonConvert.SerializeObject(pe) });
            }
        }
    }

    ///<inheritdoc />
    public async Task BulkPersistEventAsync(List<Event> events)
    {
        Event errorPe = null;
        try
        {
            var eventContainer = await GetEventStoreContainerAsync(true);

            List<Task> taskList = new List<Task>();
            events.ForEach(pe => {
                taskList.Add(eventContainer.CreateItemAsync(pe,pe.aggregateRootId.ToPartitionKey())
                        .ContinueWith(itemResponse =>
                        {
                            if (!itemResponse.IsCompletedSuccessfully)
                            {
                                errorPe = pe;
                                throw new Exception("Bulk save error");
                            }
                        }));
            });

            await Task.WhenAll(taskList);
        }
        catch (Exception e)
        {
            await HandleUndeliverableAsync("BulkPersistEvent", e.Message, errorPe);
        }
    }

    ///<inheritdoc />
    public async Task HandleUndeliverableAsync(string functionName, string errorMessage, Event eventToHandle)
    {
        var undeliverableContainer = await GetUndeliverableEventsContainerAsync();

        await undeliverableContainer.CreateItemAsync(new UndeliverableEvent(functionName, errorMessage, eventToHandle), eventToHandle.aggregateRootId.ToPartitionKey());
    }

    ///<inheritdoc />
    public async Task<T> RehydrateAsync<T>(Guid id, DateTime? untilDate = null) where T : NostifyObject, IAggregate, new()
    {
        var eventContainer = await GetEventStoreContainerAsync();
        
        T rehyd = new T();
        List<Event> peList = await eventContainer.GetItemLinqQueryable<Event>()
            .Where(pe => pe.aggregateRootId == id
                && (!untilDate.HasValue || pe.timestamp <= untilDate)
            )
            .ReadAllAsync();

        foreach (var pe in peList.OrderBy(pe => pe.timestamp))  //Apply in order
        {
            rehyd.Apply(pe);
        }

        return rehyd;
    }

    ///<inheritdoc />
    public async Task<P> RehydrateAsync<P,A>(Guid id, HttpClient httpClient) where P : ProjectionBaseClass<P,A>, IContainerName, IHasExternalData<P>, new() where A : NostifyObject, IAggregate, new()
    {
        var eventContainer = await GetEventStoreContainerAsync();
        
        P rehydratedProjection = new P();
        List<Event> eventList = await eventContainer.GetItemLinqQueryable<Event>()
            .Where(e => e.aggregateRootId == id)
            .ReadAllAsync();

        foreach (var pe in eventList.OrderBy(pe => pe.timestamp))  //Apply in order
        {
            rehydratedProjection.Apply(pe);
        }

        rehydratedProjection = await rehydratedProjection.InitAsync(this, httpClient);

        return rehydratedProjection;
    }


    ///<inheritdoc />
    public async Task<Container> GetEventStoreContainerAsync(bool allowBulk = false)
    {
        return await GetContainerAsync(Repository.EventStoreContainer, allowBulk, Repository.EventStorePartitionKey);
    }

    ///<inheritdoc />
    public async Task<Container> GetCurrentStateContainerAsync<A>(string partitionKeyPath = "/tenantId") where A : IAggregate
    {
        return await GetContainerAsync(A.currentStateContainerName, false, partitionKeyPath);
    }

    ///<inheritdoc />
    public async Task<Container> GetBulkCurrentStateContainerAsync<A>(string partitionKeyPath = "/tenantId") where A : IAggregate
    {
        return await GetContainerAsync(A.currentStateContainerName, true, partitionKeyPath);
    }

    ///<inheritdoc />
    public async Task<Container> GetProjectionContainerAsync<P>(string partitionKeyPath = "/tenantId") where P : IContainerName
    {
        return await GetContainerAsync(P.containerName, false, partitionKeyPath);
    }

    ///<inheritdoc />
    public async Task<Container> GetBulkProjectionContainerAsync<P>(string partitionKeyPath = "/tenantId") where P : IContainerName
    {
        return await GetContainerAsync(P.containerName, true, partitionKeyPath);
    }

    ///<inheritdoc />
    public async Task<Container> GetContainerAsync(string containerName, bool bulkEnabled, string partitionKeyPath)
    {
        return await Repository.GetContainerAsync(containerName, partitionKeyPath, bulkEnabled);
    }  

    
    ///<inheritdoc />
    public async Task RebuildCurrentStateContainerAsync<T>(string partitionKeyPath = "/tenantId") where T : NostifyObject, IAggregate, new()
    {
        Container containerToRebuild = await GetCurrentStateContainerAsync<T>();

        //Store data needed for re-creating container
        string containerName = containerToRebuild.Id;

        //Remove container to delete all bad data and start from scratch
        ContainerResponse resp = await containerToRebuild.DeleteContainerAsync();

        List<T> rehydratedAggregates = new List<T>();

        Container eventStore = await GetEventStoreContainerAsync();
        //Get list of distinct aggregate root ids
        List<Guid> uniqueAggregateRootIds = await eventStore.GetItemLinqQueryable<Event>()
            .Select(pe => pe.aggregateRootId)
            .Distinct()
            .ReadAllAsync();
        
        //TODO: probably can just use the feed iterator here?
        //Loop through a query the events for 1,000 at a time, then rehydrate 
        const int GET_THIS_MANY = 1000;
        int endOfRange = uniqueAggregateRootIds.Count();
        int i = 0;
        while (i < endOfRange)
        {
            int rangeNum = (i + GET_THIS_MANY >= endOfRange) ? endOfRange - 1 : i + GET_THIS_MANY;
            var aggRange = uniqueAggregateRootIds.GetRange(i,rangeNum);

            var peList = await eventStore.GetItemLinqQueryable<Event>()
                .Where(pe => aggRange.Contains(pe.aggregateRootId))
                .ReadAllAsync();
            
            aggRange.ForEach(id => {
                rehydratedAggregates.Add(Rehydrate<T>(peList.Where(e => e.aggregateRootId == id).OrderBy(e => e.timestamp).ToList()));
            });
            i = i + GET_THIS_MANY;
        }
        
        //Recreate container
        Container rebuiltContainer = await GetContainerAsync(containerName, true, partitionKeyPath);

        //Save
        List<Task> saveTasks = new List<Task>();
        rehydratedAggregates.ForEach(agg => {
            saveTasks.Add(rebuiltContainer.UpsertItemAsync<T>(agg));
        });   
        await Task.WhenAll(saveTasks);
    }

    ///<summary>
    ///Rehydrates data directly from stream of events passed from calling method.
    ///</summary>
    ///<returns>
    ///The projection state rehydrated to the extent of the events fed into it.
    ///</returns>
    ///<param name="peList">The event stream for the aggregate to be rehydrated</param>
    private T Rehydrate<T>(List<Event> peList) where T : NostifyObject, new()
    {            
        T rehyd = new T();
        foreach (var pe in peList) 
        {
            rehyd.Apply(pe);
        }

        return rehyd;
    }
    

    ///<summary>
    ///Retrieves the undeliverable events container
    ///</summary>
    public async Task<Container> GetUndeliverableEventsContainerAsync(string partitionKeyPath = "/tenantId")
    {
        var db = await Repository.GetDatabaseAsync();
        return await GetContainerAsync(Repository.UndeliverableEvents, false, partitionKeyPath);
    }

    ///<inheritdoc />
    public async Task DoBulkUpsertAsync<T>(Container bulkContainer, List<T> itemList) where T : IApplyable
    {        
        await bulkContainer.DoBulkUpsertAsync<T>(itemList);
    }

    ///<inheritdoc />
    public async Task DoBulkUpsertAsync<T>(string containerName, List<T> itemList, string partitionKeyPath = "/tenantId") where T : IApplyable
    {
        var bulkContainer = await GetContainerAsync(containerName, true, partitionKeyPath);
        await DoBulkUpsertAsync<T>(bulkContainer, itemList);
    }


    ///<inheritdoc />
    public async Task CreateContainersAsync<TTypeInAssembly>(bool localhostOnly = true)
    {
        if (localhostOnly && !Repository.IsLocalEmulator)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Repository.ConnectionString))
        {
            Console.WriteLine("Connection string is null or empty. Containers will not be created.");
            return;
        }

        if (Repository.DbName == null)
        {
            Console.WriteLine("Database name is null or empty. Containers will not be created.");
            return;
        }
        
        // get the calling assembly
        var assembly = typeof(TTypeInAssembly).Assembly;

        // Create the event store container
        await CreateContainerAsync("eventStore", "/aggregateRootId");

        // Create the containers for the aggregates and projections
        foreach (var containerName in EnumerateContainerNames(assembly))
        {
            await CreateContainerAsync(containerName);
        }
    }
    

    private async Task CreateContainerAsync(string containerName, string partitionKeyPath = "/tenantId")
    {
        try
        {
            // Create the container if it does not exist
            await Repository.GetContainerAsync(containerName, partitionKeyPath);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"Database not found: {Repository.DbName}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while creating or retrieving the container {containerName}: {ex.Message}");
            throw;
        }
    }

    private static IEnumerable<string> EnumerateContainerNames(Assembly assembly)
    {
        var aggregateTypes = assembly.GetTypes().Where(t => typeof(IAggregate).IsAssignableFrom(t));
        var projectionTypes = assembly.GetTypes().Where(t => typeof(IContainerName).IsAssignableFrom(t));

        if (aggregateTypes != null)
        {
            foreach (var type in aggregateTypes)
            {
                var value = GetPropertyValue(type, "currentStateContainerName");
                if (value != null)
                {
                    yield return value;
                }
            }
        }

        if (projectionTypes != null)
        {
            foreach (var type in projectionTypes)
            {
                var value = GetPropertyValue(type, "containerName");
                if (value != null)
                {
                    yield return value;
                }
            }
        }
    }

    private static string GetPropertyValue(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        if (property != null)
        {
            var value = property.GetValue(null);
            if (value != null)
            {
                return value.ToString();
            }
        }

        return null;
    }
    
}

