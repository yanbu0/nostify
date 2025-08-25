using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Confluent.Kafka;
using System.Reflection;
using Confluent.Kafka.Admin;
using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace nostify;

///<summary>
/// Base class to utilize nostify.  Inject this with NostifyFactory to use in your application.
/// This class provides methods to persist and publish events, rehydrate aggregates, and manage containers.
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
    /// <inheritdoc />
    public IProjectionInitializer ProjectionInitializer { get; } = new ProjectionInitializer();
    /// <inheritdoc />
    public IHttpClientFactory HttpClientFactory { get; }

    ///<summary>
    /// Nostify constructor for development with no username and password for Kafka.
    ///</summary>
    public Nostify(string primaryKey, string dbName, string cosmosEndpointUri, string kafkaUrl, IHttpClientFactory httpClientFactory, string defaultPartitionKeyPath = "/tenantId", Guid defaultTenantId = default)
    {
        new Nostify(primaryKey, dbName, cosmosEndpointUri, kafkaUrl, null, null, httpClientFactory, defaultPartitionKeyPath, defaultTenantId);
    }

    internal Nostify(NostifyCosmosClient repository, string defaultPartitionKeyPath, Guid defaultTenantId, string kafkaUrl, IProducer<string, string> kafkaProducer, IHttpClientFactory httpClientFactory)
    {
        Repository = repository;
        DefaultPartitionKeyPath = defaultPartitionKeyPath;
        DefaultTenantId = defaultTenantId;
        KafkaUrl = kafkaUrl;
        KafkaProducer = kafkaProducer;
        HttpClientFactory = httpClientFactory;
    }

    ///<summary>
    /// Nostify constructor for production with username and password for Kafka.
    ///</summary>
    private Nostify(string primaryKey, string dbName, string cosmosEndpointUri, string kafkaUrl, string kafkaUserName, string kafkaPassword, IHttpClientFactory httpClientFactory, string defaultPartitionKeyPath, Guid defaultTenantId)
    {
        Repository = new NostifyCosmosClient(primaryKey, dbName, cosmosEndpointUri);
        if (defaultPartitionKeyPath != null)
        {
            DefaultPartitionKeyPath = defaultPartitionKeyPath;
        }
        DefaultTenantId = defaultTenantId;
        KafkaUrl = kafkaUrl;
        HttpClientFactory = httpClientFactory;

        bool isDeployed = !string.IsNullOrWhiteSpace(kafkaUserName) && !string.IsNullOrWhiteSpace(kafkaPassword);

        // Build producer instance
        var producerConfig = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("bootstrap.servers", KafkaUrl),
            new KeyValuePair<string, string>("session.timeout.ms", "45000"),
            new KeyValuePair<string, string>("client.id", $"Nostifyd-{dbName}-{Guid.NewGuid()}")
        };
        if (isDeployed)
        {
            producerConfig.Add(new KeyValuePair<string, string>("sasl.username", kafkaUserName));
            producerConfig.Add(new KeyValuePair<string, string>("sasl.password", kafkaPassword));
            producerConfig.Add(new KeyValuePair<string, string>("security.protocol", "SASL_SSL"));
            producerConfig.Add(new KeyValuePair<string, string>("sasl.mechanisms", "PLAIN"));
        }
        KafkaProducer = new ProducerBuilder<string, string>(producerConfig).Build();
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
        await PublishEventAsync(peList);
    }

    ///<inheritdoc />
    public async Task PublishEventAsync(List<Event> peList, bool showOutput = false)
    {
        if (peList != null)
        {
            foreach (Event pe in peList)
            {
                string topic = pe.command.name;
                var result = await KafkaProducer.ProduceAsync(topic, new Message<string, string> { Value = JsonConvert.SerializeObject(pe) });

                if (showOutput) Console.WriteLine($"Event published to topic {topic} with key {result.Key} and value {result.Value}");
            }
        }
    }

    ///<inheritdoc />
    public async Task PublishEventAsync(Event eventToPublish)
    {
        List<Event> peList = new List<Event>() { eventToPublish };
        await PublishEventAsync(peList);
    }

    ///<inheritdoc />
    public async Task<List<P>> MultiApplyAndPersistAsync<P>(Container bulkContainer, Event eventToApply, List<Guid> projectionIds, int batchSize = 100) where P : NostifyObject, new()
    {
        //Throw if not bulk container
        bulkContainer.ValidateBulkEnabled(true);

        List<Task> tasks = new List<Task>();
        List<P> succesfulTasks = new List<P>();

        //Loop through in batches to avoid overwhelming CosmosDB
        for (int i = 0; i < projectionIds.Count; i += batchSize)
        {
            var batch = projectionIds.Skip(i).Take(batchSize).ToList();
            List<Task> batchTasks = new List<Task>();

            batch.ForEach(projId =>
            {
                batchTasks.Add(
                    CreateApplyAndPersistTask<P>(bulkContainer, eventToApply.partitionKey, eventToApply, projId, false, false)
                    .ContinueWith(itemResponse =>
                    {
                        if (itemResponse.IsCompletedSuccessfully) succesfulTasks.Add(itemResponse.Result);
                    })
                );
            });

            await Task.WhenAll(batchTasks);
        }

        //Only return first 1000 results to avoid overwhelming caller
        return succesfulTasks.Take(1000).ToList();
    }

    ///<inheritdoc />
    public async Task<List<P>> MultiApplyAndPersistAsync<P>(Container bulkContainer, Event eventToApply, List<P> projectionsToUpdate, int batchSize = 100) where P : NostifyObject, new()
    {
        return await MultiApplyAndPersistAsync<P>(bulkContainer, eventToApply, projectionsToUpdate.Select(p => p.id).ToList(), batchSize);
    }

    ///<inheritdoc />
    public async Task<List<P>> BulkApplyAndPersistAsync<P>(Container bulkContainer, string idPropertyName, string[] events, bool allowRetry = false, bool publishErrorEvents = false) where P : NostifyObject, new()
    {
        //Throw if not bulk container
        bulkContainer.ValidateBulkEnabled(true);

        List<Event> eventList = events.Select(e => JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(e).GetEvent()).ToList();
        List<Guid> partitionKeys = eventList.Select(e => e.partitionKey).Distinct().ToList();

        List<Task> tasks = new List<Task>();
        List<P> succesfulTasks = new List<P>();

        //For each partition, create a list of tasks to apply and persist the events based off the list of ids in the property specified
        partitionKeys.ForEach(pk =>
        {
            List<Event> partitionEvents = eventList.Where(e => e.partitionKey == pk).ToList();
            partitionEvents.ForEach(pe =>
            {
                //Set up vars for both list and single id properties
                List<Guid> ids = new List<Guid>();
                Guid idToApplyTo = Guid.Empty;

                //Try list first
                if (pe.payload.TryGetValue<List<Guid>>(idPropertyName, out ids))
                {
                    ids.ForEach(id => tasks.Add(
                        CreateApplyAndPersistTask<P>(bulkContainer, pk, pe, id, allowRetry, publishErrorEvents)
                            .ContinueWith(itemResponse =>
                            {
                                if (itemResponse.IsCompletedSuccessfully) succesfulTasks.Add(itemResponse.Result);
                            })
                    ));
                }
                else //If not list try single id
                {
                    if (pe.payload.TryGetValue<Guid>(idPropertyName, out idToApplyTo))
                    {
                        tasks.Add(
                            CreateApplyAndPersistTask<P>(bulkContainer, pk, pe, idToApplyTo, allowRetry, publishErrorEvents)
                                .ContinueWith(itemResponse =>
                                {
                                    if (itemResponse.IsCompletedSuccessfully) succesfulTasks.Add(itemResponse.Result);
                                })
                        );
                    }
                }
            });
        });

        await Task.WhenAll(tasks);

        //Only return first 1000 results to avoid overwhelming caller
        return succesfulTasks.Take(1000).ToList();
    }

    private Task<P> CreateApplyAndPersistTask<P>(Container bulkContainer, Guid pk, Event pe, Guid id, bool allowRetry, bool publishErrorEvents) where P : NostifyObject, new()
    {
        return bulkContainer.ApplyAndPersistAsync<P>(
                                new List<Event>() { pe }, pk.ToPartitionKey(), id
                            ).ContinueWith(itemResponse =>
                            {
                                if (!itemResponse.IsCompletedSuccessfully)
                                {
                                    //Retry if too many requests error
                                    if (allowRetry && itemResponse.Exception.InnerException is CosmosException ce && ce.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                                    {
                                        //Wait the specified amount of time or one second then retry, write to undeliverable events if still fails
                                        int waitTime = ce.RetryAfter.HasValue ? (int)ce.RetryAfter.Value.TotalMilliseconds : 1000;
                                        Task.Delay(waitTime).ContinueWith(_ => bulkContainer.CreateItemAsync(pe, pe.aggregateRootId.ToPartitionKey())
                                            .ContinueWith(_ => HandleUndeliverableAsync(nameof(BulkPersistEventAsync), itemResponse.Exception.Message, pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null)));
                                    }
                                    else
                                    {
                                        //This will cause a record to get written to the undeliverable events container for retry later if needed
                                        _ = HandleUndeliverableAsync(nameof(BulkPersistEventAsync), itemResponse.Exception.Message, pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null);
                                    }
                                }

                                return itemResponse.Result;
                            });
    }

    ///<inheritdoc />
    public async Task BulkPersistEventAsync(List<Event> events, int? batchSize = null, bool allowRetry = false, bool publishErrorEvents = false)
    {
        var eventContainer = await GetEventStoreContainerAsync(true);

        //If batchSize is not null, set loopSize to batchSize, otherwise loop through all events
        int loopSize = batchSize.HasValue ? batchSize.Value : events.Count;

        //Loop through in batches of batchSize
        for (int i = 0; i < events.Count; i += loopSize)
        {
            var eventBatch = events.Skip(i).Take(loopSize).ToList();

            List<Task> taskList = new List<Task>();
            eventBatch.ForEach(pe =>
            {
                taskList.Add(eventContainer.CreateItemAsync(pe, pe.aggregateRootId.ToPartitionKey())
                        .ContinueWith(itemResponse =>
                        {
                            if (!itemResponse.IsCompletedSuccessfully)
                            {
                                //Retry if too many requests error
                                if (allowRetry && itemResponse.Exception.InnerException is CosmosException ce && ce.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                                {
                                    //Wait the specified amount of time or one second then retry, write to undeliverable events if still fails
                                    int waitTime = ce.RetryAfter.HasValue ? (int)ce.RetryAfter.Value.TotalMilliseconds : 1000;
                                    Task.Delay(waitTime).ContinueWith(_ => eventContainer.CreateItemAsync(pe, pe.aggregateRootId.ToPartitionKey())
                                        .ContinueWith(_ => HandleUndeliverableAsync(nameof(BulkPersistEventAsync), itemResponse.Exception.Message, pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null)));
                                }
                                else
                                {
                                    //This will cause a record to get written to the undeliverable events container for retry later if needed
                                    _ = HandleUndeliverableAsync(nameof(BulkPersistEventAsync), itemResponse.Exception.Message, pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null);
                                }
                            }
                        }));
            });

            await Task.WhenAll(taskList);
        }
    }

    ///<inheritdoc />
    public async Task HandleUndeliverableAsync(string functionName, string errorMessage, Event eventToHandle, ErrorCommand? errorCommand = null)
    {
        var undeliverableContainer = await GetUndeliverableEventsContainerAsync();

        await undeliverableContainer.CreateItemAsync(new UndeliverableEvent(functionName, errorMessage, eventToHandle), eventToHandle.aggregateRootId.ToPartitionKey());
        if (errorCommand is not null)
        {
            var errorPayload = new ErrorPayload(errorMessage, eventToHandle);
            //Publish error event to kafka
            await PublishEventAsync(new NostifyErrorEvent(errorCommand, eventToHandle.aggregateRootId, errorPayload, eventToHandle.userId, eventToHandle.partitionKey));
        }
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
    public async Task<P> RehydrateAsync<P, A>(Guid id, HttpClient httpClient) where P : NostifyObject, IProjection, IHasExternalData<P>, new() where A : NostifyObject, IAggregate, new()
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
    public async Task<Container> GetProjectionContainerAsync<P>(string partitionKeyPath = "/tenantId") where P : IProjection
    {
        return await GetContainerAsync(P.containerName, false, partitionKeyPath);
    }

    ///<inheritdoc />
    public async Task<Container> GetBulkProjectionContainerAsync<P>(string partitionKeyPath = "/tenantId") where P : IProjection
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
            var aggRange = uniqueAggregateRootIds.GetRange(i, rangeNum);

            var peList = await eventStore.GetItemLinqQueryable<Event>()
                .Where(pe => aggRange.Contains(pe.aggregateRootId))
                .ReadAllAsync();

            aggRange.ForEach(id =>
            {
                rehydratedAggregates.Add(Rehydrate<T>(peList.Where(e => e.aggregateRootId == id).OrderBy(e => e.timestamp).ToList()));
            });
            i = i + GET_THIS_MANY;
        }

        //Recreate container
        Container rebuiltContainer = await GetContainerAsync(containerName, true, partitionKeyPath);

        //Save
        List<Task> saveTasks = new List<Task>();
        rehydratedAggregates.ForEach(agg =>
        {
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


    ///<inheritdoc />
    public async Task<Container> GetUndeliverableEventsContainerAsync()
    {
        return await GetContainerAsync(Repository.UndeliverableEvents, false, "/aggregateRootId");
    }

    ///<inheritdoc />
    public async Task<Container> GetSagaContainerAsync()
    {
        return await GetContainerAsync(Repository.SagaContainer, false, "/id");
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
    public async Task CreateContainersAsync<TTypeInAssembly>(bool localhostOnly = true, int? throughput = null, bool verbose = false)
    {
        if (Repository.IsLocalEmulator && !throughput.HasValue)
        {
            throughput = 400; // Set a default throughput for local emulator
            Console.WriteLine("Using default throughput of 400 for local emulator since none was set. This will probably be really slow.");
        }

        if (localhostOnly && !Repository.IsLocalEmulator)
        {
            Console.WriteLine("Not running on localhost. Containers will not be created.");
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
        await CreateContainerAsync(Repository.EventStoreContainer, Repository.EventStorePartitionKey, throughput, verbose);

        // Create the containers for the aggregates and projections
        foreach (var containerName in EnumerateContainerNames(assembly))
        {
            await CreateContainerAsync(containerName, throughput: throughput, verbose: verbose);
        }
    }

    private string _noHttpClientErrorMessage = "HttpClientFactory is not set. Call .WithHttp() in the NostifyFactory config during startup to use this method.";

    ///<inheritdoc />
    public async Task<List<P>> InitAsync<P, A>(Guid id) where P : NostifyObject, IProjection, IHasExternalData<P>, new() where A : IAggregate
    {
        //throw error if no HttpClientFactory
        if (HttpClientFactory == null)
        {
            throw new InvalidOperationException(_noHttpClientErrorMessage);
        }
        // Use the HttpClientFactory to create a new HttpClient instance
        var httpClient = HttpClientFactory.CreateClient();
        return await ProjectionInitializer.InitAsync<P, A>(id, this, httpClient);
    }

    ///<inheritdoc />
    public async Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //throw error if no HttpClientFactory
        if (HttpClientFactory == null)
        {
            throw new InvalidOperationException(_noHttpClientErrorMessage);
        }
        // Use the HttpClientFactory to create a new HttpClient instance
        var httpClient = HttpClientFactory.CreateClient();
        return await ProjectionInitializer.InitAsync<P, A>(idsToInit, this, httpClient);
    }

    ///<inheritdoc/>
    public async Task<List<P>> InitAsync<P>(List<P> projectionsToInit) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //throw error if no HttpClientFactory
        if (HttpClientFactory == null)
        {
            throw new InvalidOperationException(_noHttpClientErrorMessage);
        }
        // Use the HttpClientFactory to create a new HttpClient instance
        var httpClient = HttpClientFactory.CreateClient();
        return await ProjectionInitializer.InitAsync<P>(projectionsToInit, this, httpClient);
    }

    ///<inheritdoc />
    public async Task InitContainerAsync<P, A>(string partitionKeyPath = "/tenantId", int loopSize = 1000) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //throw error if no HttpClientFactory
        if (HttpClientFactory == null)
        {
            throw new InvalidOperationException(_noHttpClientErrorMessage);
        }
        // Use the HttpClientFactory to create a new HttpClient instance
        var httpClient = HttpClientFactory.CreateClient();
        await ProjectionInitializer.InitContainerAsync<P, A>(this, httpClient, partitionKeyPath, loopSize);
    }

    ///<inheritdoc />
    public async Task InitAllUninitializedAsync<P>(int maxloopSize = 10) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //throw error if no HttpClientFactory
        if (HttpClientFactory == null)
        {
            throw new InvalidOperationException(_noHttpClientErrorMessage);
        }
        // Use the HttpClientFactory to create a new HttpClient instance
        var httpClient = HttpClientFactory.CreateClient();
        await ProjectionInitializer.InitAllUninitialized<P>(this, httpClient, maxloopSize);
    }

    private async Task CreateContainerAsync(string containerName, string partitionKeyPath = "/tenantId", int? throughput = null, bool verbose = false)
    {
        try
        {
            // Create the container if it does not exist
            if (verbose) Console.WriteLine($"Creating container {containerName} with partition key path {partitionKeyPath} and throughput {throughput}, if it does not already exist");
            await Repository.GetContainerAsync(containerName, partitionKeyPath, throughput: throughput, verbose: verbose);
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
        var projectionTypes = assembly.GetTypes().Where(t => typeof(IProjection).IsAssignableFrom(t));

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


