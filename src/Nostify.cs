using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Confluent.Kafka;
using System.Reflection;
using Confluent.Kafka.Admin;
using System;
using System.Net.Http;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace nostify;

///<summary>
/// Base class to utilize nostify.  Inject this with NostifyFactory to use in your application.
/// This class provides methods to persist and publish events, rehydrate aggregates, and manage containers.
///</summary>
public class Nostify : INostify, IDisposable
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
    public IHttpClientFactory? HttpClientFactory { get; }
    /// <inheritdoc />
    public ILogger? Logger { get; }
    /// <inheritdoc />
    public RetryOptions DefaultRetryOptions { get; }

    private readonly ConsumerConfig? _baseConsumerConfig;
    private readonly ConcurrentDictionary<string, IConsumer<string, string>> _kafkaConsumers = new();
    private int _disposeState;

    // Compiled logging templates avoid repeated message-template parsing on hot paths.
    private static readonly Action<ILogger, string, Exception?> LogKafkaConsumerCreated =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, nameof(LogKafkaConsumerCreated)), "Created Kafka consumer for group {ConsumerGroup}");

    private static readonly Action<ILogger, string, Exception?> LogDedicatedKafkaConsumerCreated =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2, nameof(LogDedicatedKafkaConsumerCreated)), "Created dedicated Kafka consumer for group {ConsumerGroup}");

    private static readonly Action<ILogger, string, Exception?> LogKafkaConsumerDisposalFailure =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3, nameof(LogKafkaConsumerDisposalFailure)), "Error disposing Kafka consumer for group {ConsumerGroup}");

    private static readonly Action<ILogger, string, Exception?> LogResourceDisposalFailure =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4, nameof(LogResourceDisposalFailure)), "Error disposing {ResourceName}");

    private static readonly Action<ILogger, string, Exception?> LogEventPersistenceFailure =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(5, nameof(LogEventPersistenceFailure)), "Failed to persist event in PersistEventAsync. Event: {Event}");

    private static readonly Action<ILogger, string, Exception?> LogUndeliverableWriteFailure =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6, nameof(LogUndeliverableWriteFailure)), "Failed to write undeliverable event in PersistEventAsync. Original event: {Event}");

    private static readonly Action<ILogger, string, Exception?> LogEventsPublished =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(7, nameof(LogEventsPublished)), "Event published to topic(s) {Topics}");

    private static readonly Action<ILogger, string, Exception?> LogEventPublishFailure =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(8, nameof(LogEventPublishFailure)), "Failed to publish event to topic(s) {Topics}");

    private static readonly Action<ILogger, string, Exception?> LogBulkEventPersistenceFailure =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(9, nameof(LogBulkEventPersistenceFailure)), "Failed to persist event in BulkPersistEventAsync. Event: {Event}");

    private static readonly Action<ILogger, string, string, string, Exception?> LogUndeliverableEvent =
        LoggerMessage.Define<string, string, string>(LogLevel.Error, new EventId(10, nameof(LogUndeliverableEvent)), "Undeliverable event in function {FunctionName}. Error: {ErrorMessage}. Event: {Event}");

    private static readonly Action<ILogger, Exception?> LogDefaultEmulatorThroughput =
        LoggerMessage.Define(LogLevel.Warning, new EventId(11, nameof(LogDefaultEmulatorThroughput)), "Using default throughput of 400 for local emulator since none was set. This will probably be really slow.");

    private static readonly Action<ILogger, Exception?> LogContainerCreationSkippedOutsideLocalhost =
        LoggerMessage.Define(LogLevel.Information, new EventId(12, nameof(LogContainerCreationSkippedOutsideLocalhost)), "Not running on localhost. Containers will not be created.");

    private static readonly Action<ILogger, Exception?> LogContainerCreationSkippedWithoutConnectionString =
        LoggerMessage.Define(LogLevel.Warning, new EventId(13, nameof(LogContainerCreationSkippedWithoutConnectionString)), "Connection string is null or empty. Containers will not be created.");

    private static readonly Action<ILogger, Exception?> LogContainerCreationSkippedWithoutDatabaseName =
        LoggerMessage.Define(LogLevel.Warning, new EventId(14, nameof(LogContainerCreationSkippedWithoutDatabaseName)), "Database name is null or empty. Containers will not be created.");

    private static readonly Action<ILogger, string, string, int?, Exception?> LogCreatingContainer =
        LoggerMessage.Define<string, string, int?>(LogLevel.Debug, new EventId(15, nameof(LogCreatingContainer)), "Creating container {ContainerName} with partition key path {PartitionKeyPath} and throughput {Throughput}, if it does not already exist");

    private static readonly Action<ILogger, string?, Exception?> LogDatabaseNotFound =
        LoggerMessage.Define<string?>(LogLevel.Error, new EventId(16, nameof(LogDatabaseNotFound)), "Database not found: {DbName}");

    private static readonly Action<ILogger, string, Exception?> LogContainerCreationFailure =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(17, nameof(LogContainerCreationFailure)), "An error occurred while creating or retrieving the container {ContainerName}");

    ///<summary>
    /// Nostify constructor for development with no username and password for Kafka.
    ///</summary>
    public Nostify(string primaryKey, string dbName, string cosmosEndpointUri, string kafkaUrl, IHttpClientFactory httpClientFactory, string defaultPartitionKeyPath = "/tenantId", Guid defaultTenantId = default)
        : this(primaryKey, dbName, cosmosEndpointUri, kafkaUrl, null, null, httpClientFactory, defaultPartitionKeyPath, defaultTenantId)
    {
    }

    internal Nostify(NostifyCosmosClient repository, string defaultPartitionKeyPath, Guid defaultTenantId, string kafkaUrl, IProducer<string, string> kafkaProducer, IHttpClientFactory? httpClientFactory, ILogger? logger = null, ConsumerConfig? baseConsumerConfig = null, RetryOptions? defaultRetryOptions = null)
    {
        Repository = repository;
        DefaultPartitionKeyPath = defaultPartitionKeyPath;
        DefaultTenantId = defaultTenantId;
        KafkaUrl = kafkaUrl;
        KafkaProducer = kafkaProducer;
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        _baseConsumerConfig = baseConsumerConfig;
        DefaultRetryOptions = defaultRetryOptions ?? new RetryOptions();
    }

    ///<summary>
    /// Nostify constructor for production with username and password for Kafka.
    ///</summary>
    private Nostify(string primaryKey, string dbName, string cosmosEndpointUri, string kafkaUrl, string? kafkaUserName, string? kafkaPassword, IHttpClientFactory httpClientFactory, string defaultPartitionKeyPath, Guid defaultTenantId)
    {
        Repository = new NostifyCosmosClient(primaryKey, dbName, cosmosEndpointUri);
        DefaultPartitionKeyPath = defaultPartitionKeyPath;
        DefaultTenantId = defaultTenantId;
        KafkaUrl = kafkaUrl;
        HttpClientFactory = httpClientFactory;

        // Build producer instance
        var producerConfig = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("bootstrap.servers", KafkaUrl),
            new KeyValuePair<string, string>("client.id", $"Nostify-{dbName}-{Guid.NewGuid()}")
        };
        if (!string.IsNullOrWhiteSpace(kafkaUserName) && !string.IsNullOrWhiteSpace(kafkaPassword))
        {
            producerConfig.Add(new KeyValuePair<string, string>("sasl.username", kafkaUserName));
            producerConfig.Add(new KeyValuePair<string, string>("sasl.password", kafkaPassword));
            producerConfig.Add(new KeyValuePair<string, string>("security.protocol", "SASL_SSL"));
            producerConfig.Add(new KeyValuePair<string, string>("sasl.mechanisms", "PLAIN"));
        }
        KafkaProducer = new ProducerBuilder<string, string>(producerConfig).Build();
        DefaultRetryOptions = new RetryOptions();
    }

    /// <inheritdoc />
    public IConsumer<string, string> GetOrCreateKafkaConsumer(string consumerGroup)
    {
        if (_baseConsumerConfig == null)
        {
            throw new NostifyException("Kafka consumer config is not available. Ensure WithKafka() or WithEventHubs() was called during configuration.");
        }

        return _kafkaConsumers.GetOrAdd(consumerGroup, group =>
        {
            var config = new ConsumerConfig(_baseConsumerConfig)
            {
                GroupId = group
            };
            var consumer = new ConsumerBuilder<string, string>(config).Build();
            if (Logger != null)
            {
                LogKafkaConsumerCreated(Logger, group, null);
            }
            return consumer;
        });
    }

    /// <inheritdoc />
    public IConsumer<string, string> CreateKafkaConsumer(string consumerGroup)
    {
        if (_baseConsumerConfig == null)
        {
            throw new NostifyException("Kafka consumer config is not available. Ensure WithKafka() or WithEventHubs() was called during configuration.");
        }

        var config = new ConsumerConfig(_baseConsumerConfig)
        {
            GroupId = consumerGroup
        };
        var consumer = new ConsumerBuilder<string, string>(config).Build();
        if (Logger != null)
        {
            LogDedicatedKafkaConsumerCreated(Logger, consumerGroup, null);
        }
        return consumer;
    }

    /// <summary>
    /// Disposes all cached Kafka consumers, the producer, and the owned Cosmos repository.
    /// </summary>
    public void Dispose()
    {
        // Make disposal idempotent and prevent concurrent callers from releasing the same native handles.
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        foreach (var kvp in _kafkaConsumers)
        {
            try
            {
                kvp.Value.Close();
            }
            catch (Exception ex)
            {
                LogConsumerDisposalFailure(kvp.Key, ex);
            }

            try
            {
                // Close can fail when the broker is unavailable; native resources must still be released.
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                // A single faulty consumer must not prevent the remaining resources from being released.
                LogConsumerDisposalFailure(kvp.Key, ex);
            }
        }
        _kafkaConsumers.Clear();

        try
        {
            KafkaProducer.Flush(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            LogResourceDisposalFailureIfEnabled("Cosmos repository", ex);
        }

        try
        {
            // Flush failures must not prevent the producer's native resources from being released.
            KafkaProducer.Dispose();
        }
        catch (Exception ex)
        {
            LogResourceDisposalFailureIfEnabled("Kafka producer", ex);
        }

        try
        {
            // Repository cleanup must run even when Kafka cleanup fails.
            Repository.Dispose();
        }
        catch (Exception ex)
        {
            LogResourceDisposalFailureIfEnabled("Kafka producer", ex);
        }

        GC.SuppressFinalize(this);
    }

    private void LogConsumerDisposalFailure(string consumerGroup, Exception exception)
    {
        if (Logger?.IsEnabled(LogLevel.Warning) == true)
        {
            LogKafkaConsumerDisposalFailure(Logger, consumerGroup, exception);
        }
    }

    private void LogResourceDisposalFailureIfEnabled(string resourceName, Exception exception)
    {
        if (Logger?.IsEnabled(LogLevel.Warning) == true)
        {
            LogResourceDisposalFailure(Logger, resourceName, exception);
        }
    }

    /// <inheritdoc />
    public async Task PersistEventAsync(IEvent eventToPersist)
    {
        try
        {
            var eventContainer = await GetEventStoreContainerAsync();
            await eventContainer.CreateItemAsync(eventToPersist, eventToPersist.aggregateRootId.ToPartitionKey());
        }
        catch (Exception ex)
        {
            if (Logger?.IsEnabled(LogLevel.Error) == true)
            {
                LogEventPersistenceFailure(Logger, JsonConvert.SerializeObject(eventToPersist), ex);
            }

            try
            {
                await HandleUndeliverableAsync(nameof(PersistEventAsync), ex.Message, eventToPersist);
            }
            catch (Exception undeliverableEx)
            {
                // Log but do not rethrow: the original persistence exception is re-thrown below,
                // ensuring the HTTP caller receives the actual failure, not a secondary write error.
                if (Logger?.IsEnabled(LogLevel.Error) == true)
                {
                    LogUndeliverableWriteFailure(Logger, JsonConvert.SerializeObject(eventToPersist), undeliverableEx);
                }
            }
            throw;
        }
    }

    ///<inheritdoc />
    public async Task PublishEventAsync(string cosmosTriggerOutput)
    {
        var eventList = JsonConvert.DeserializeObject<List<Event>>(cosmosTriggerOutput) ?? new List<Event>();
        List<IEvent> peList = eventList.Cast<IEvent>().ToList();
        await PublishEventAsync(peList);
    }

    ///<inheritdoc />
    public async Task PublishEventAsync(List<IEvent> peList, bool showOutput = false)
    {
        ArgumentNullException.ThrowIfNull(peList);

        List<Task> publishTasks = new List<Task>();
        foreach (IEvent pe in peList)
        {
            string topic = pe.eventType.name;
            publishTasks.Add(KafkaProducer.ProduceAsync(topic, new Message<string, string> { Value = JsonConvert.SerializeObject(pe) }));
        }

        try
        {
            await Task.WhenAll(publishTasks);
            if (showOutput && Logger?.IsEnabled(LogLevel.Information) == true)
            {
                LogEventsPublished(Logger, string.Join(", ", peList.Select(p => p.eventType.name).Distinct()), null);
            }
        }
        catch (Exception ex)
        {
            if (showOutput && Logger?.IsEnabled(LogLevel.Error) == true)
            {
                LogEventPublishFailure(Logger, string.Join(", ", peList.Select(p => p.eventType.name).Distinct()), ex);
            }

            throw;
        }
    }

    ///<inheritdoc />
    public async Task PublishEventAsync(IEvent eventToPublish)
    {
        List<IEvent> peList = new List<IEvent>() { eventToPublish };
        await PublishEventAsync(peList);
    }

    ///<inheritdoc />
    public async Task<List<P>> MultiApplyAndPersistAsync<P>(Container bulkContainer, IEvent eventToApply, List<Guid> projectionIds, int batchSize = 100, RetryOptions? retryOptions = null) where P : NostifyObject, new()
    {
        //Throw if not bulk container
        bulkContainer.ValidateBulkEnabled(true);

        ConcurrentBag<P> successfulTasks = new ConcurrentBag<P>();

        //Loop through in batches to avoid overwhelming CosmosDB
        for (int i = 0; i < projectionIds.Count; i += batchSize)
        {
            var batch = projectionIds.Skip(i).Take(batchSize).ToList();
            List<Task> batchTasks = new List<Task>();

            batch.ForEach(projId =>
            {
                batchTasks.Add(
                    CreateApplyAndPersistTask<P>(bulkContainer, eventToApply.partitionKey, eventToApply, projId, retryOptions)
                    .ContinueWith(itemResponse =>
                    {
                        if (itemResponse.IsCompletedSuccessfully && itemResponse.Result is not null)
                        {
                            successfulTasks.Add(itemResponse.Result);
                        }
                    })
                );
            });

            await Task.WhenAll(batchTasks);
        }

        //Only return first 1000 results to avoid overwhelming caller
        return successfulTasks.Take(1000).ToList();
    }

    ///<inheritdoc />
    public async Task<List<P>> MultiApplyAndPersistAsync<P>(Container bulkContainer, IEvent eventToApply, List<P> projectionsToUpdate, int batchSize = 100, RetryOptions? retryOptions = null) where P : NostifyObject, new()
    {
        return await MultiApplyAndPersistAsync<P>(bulkContainer, eventToApply, projectionsToUpdate.Select(p => p.id).ToList(), batchSize, retryOptions);
    }

    /// <summary>
    /// Backwards-compatible overload that converts bool allowRetry to RetryOptions.
    /// </summary>
    ///<inheritdoc />
    public async Task<List<P>> BulkApplyAndPersistAsync<P>(Container bulkContainer, string idPropertyName, string[] events, bool allowRetry = false, bool publishErrorEvents = false) where P : NostifyObject, new()
    {
        RetryOptions? retryOptions = allowRetry
            ? new RetryOptions()
            : null;
        return await BulkApplyAndPersistAsync<P>(bulkContainer, idPropertyName, events, retryOptions, publishErrorEvents);
    }

    ///<inheritdoc />
    public async Task<List<P>> BulkApplyAndPersistAsync<P>(Container bulkContainer, string idPropertyName, string[] events, RetryOptions? retryOptions, bool publishErrorEvents = false) where P : NostifyObject, new()
    {
        //Throw if not bulk container
        bulkContainer.ValidateBulkEnabled(true);

        List<IEvent> eventList = events.Select(DeserializeRequiredEvent).ToList();
        List<Guid> partitionKeys = eventList.Select(e => e.partitionKey).Distinct().ToList();

        List<Task> tasks = new List<Task>();
        ConcurrentBag<P> succesfulTasks = new ConcurrentBag<P>();

        //For each partition, create a list of tasks to apply and persist the events based off the list of ids in the property specified
        partitionKeys.ForEach(pk =>
        {
            List<IEvent> partitionEvents = eventList.Where(e => e.partitionKey == pk).ToList();
            partitionEvents.ForEach(pe =>
            {
                //Set up vars for both list and single id properties
                List<Guid>? ids;
                Guid idToApplyTo = Guid.Empty;

                // Applying by a payload property is invalid for null-payload events.
                if (pe.payload == null)
                {
                    throw new NostifyException($"Event payload is null; cannot read ID property '{idPropertyName}'.");
                }

                //Try list first
                if (pe.payload.TryGetValue<List<Guid>>(idPropertyName, out ids) && ids is not null)
                {
                    ids.ForEach(id => tasks.Add(
                        CreateApplyAndPersistTask<P>(bulkContainer, pk, pe, id, retryOptions, publishErrorEvents)
                            .ContinueWith(itemResponse =>
                            {
                                if (itemResponse.IsCompletedSuccessfully && itemResponse.Result is not null)
                                {
                                    succesfulTasks.Add(itemResponse.Result);
                                }
                            })
                    ));
                }
                else //If not list try single id
                {
                    if (pe.payload.TryGetValue<Guid>(idPropertyName, out idToApplyTo))
                    {
                        tasks.Add(
                            CreateApplyAndPersistTask<P>(bulkContainer, pk, pe, idToApplyTo, retryOptions, publishErrorEvents)
                                .ContinueWith(itemResponse =>
                                {
                                    if (itemResponse.IsCompletedSuccessfully && itemResponse.Result is not null)
                                    {
                                        succesfulTasks.Add(itemResponse.Result);
                                    }
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

    /// <summary>
    /// Creates and executes an apply-and-persist task for a single projection item.
    /// When retryOptions is provided, uses RetryableContainer for per-item retry with exponential backoff.
    /// </summary>
    private async Task<P?> CreateApplyAndPersistTask<P>(Container bulkContainer, Guid pk, IEvent pe, Guid id, RetryOptions? retryOptions, bool publishErrorEvents = false) where P : NostifyObject, new()
    {
        if (retryOptions != null)
        {
            var retryable = bulkContainer.WithRetry(retryOptions);
            var result = await retryable.ApplyAndPersistAsync<P>(
                pe,
                id,
                onExhausted: () => HandleUndeliverableAsync(nameof(MultiApplyAndPersistAsync), "Exhausted retries", pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null),
                onNotFound: () => HandleUndeliverableAsync(nameof(MultiApplyAndPersistAsync), "Not found", pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null),
                onException: (ex) => HandleUndeliverableAsync(nameof(MultiApplyAndPersistAsync), ex.Message, pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null)
            );
            return result;
        }
        else
        {
            try
            {
                return await bulkContainer.ApplyAndPersistAsync<P>(new List<IEvent>() { pe }, pk.ToPartitionKey(), id);
            }
            catch (Exception ex)
            {
                await HandleUndeliverableAsync(nameof(MultiApplyAndPersistAsync), ex.Message, pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null);
                throw;
            }
        }
    }

    /// <summary>
    /// Backwards-compatible overload that converts bool allowRetry to RetryOptions.
    /// </summary>
    ///<inheritdoc />
    public async Task BulkPersistEventAsync(List<IEvent> events, int? batchSize = null, bool allowRetry = false, bool publishErrorEvents = false)
    {
        RetryOptions? retryOptions = allowRetry
            ? new RetryOptions()
            : null;
        await BulkPersistEventAsync(events, batchSize, retryOptions, publishErrorEvents);
    }

    ///<inheritdoc />
    public async Task BulkPersistEventAsync(List<IEvent> events, int? batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false)
    {
        var eventContainer = await GetEventStoreContainerAsync(true);

        //If batchSize is not null, set loopSize to batchSize, otherwise loop through all events
        int loopSize = batchSize.HasValue ? batchSize.Value : events.Count;

        //Loop through in batches of batchSize
        for (int i = 0; i < events.Count; i += loopSize)
        {
            var eventBatch = events.Skip(i).Take(loopSize).ToList();

            if (retryOptions != null)
            {
                var retryable = eventContainer.WithRetry(retryOptions);
                await retryable.DoBulkCreateEventAsync(
                    eventBatch,
                    onException: async (pe, ex) =>
                    {
                        if (Logger?.IsEnabled(LogLevel.Error) == true)
                        {
                            LogBulkEventPersistenceFailure(Logger, JsonConvert.SerializeObject(pe), ex);
                        }

                        await HandleUndeliverableAsync(nameof(BulkPersistEventAsync), ex.Message, pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null);
                    }
                );
            }
            else
            {
                IEnumerable<Task> persistenceTasks = eventBatch.Select(async pe =>
                {
                    try
                    {
                        await eventContainer.CreateItemAsync(pe, pe.aggregateRootId.ToPartitionKey());
                    }
                    catch (Exception ex)
                    {
                        if (Logger?.IsEnabled(LogLevel.Error) == true)
                        {
                            LogBulkEventPersistenceFailure(Logger, JsonConvert.SerializeObject(pe), ex);
                        }

                        await HandleUndeliverableAsync(nameof(BulkPersistEventAsync), ex.Message, pe, publishErrorEvents ? ErrorCommand.BulkPersistEvent : null);
                    }
                });
                await Task.WhenAll(persistenceTasks);
            }
        }
    }

    ///<inheritdoc />
    public virtual async Task HandleUndeliverableAsync(string functionName, string errorMessage, IEvent eventToHandle, ErrorCommand? errorCommand = null)
    {
        if (Logger?.IsEnabled(LogLevel.Error) == true)
        {
            LogUndeliverableEvent(Logger, functionName, errorMessage, JsonConvert.SerializeObject(eventToHandle), null);
        }

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
        List<IEvent> peList = await eventContainer.GetItemLinqQueryable<IEvent>()
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
        List<IEvent> eventList = await eventContainer.GetItemLinqQueryable<IEvent>()
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
    public virtual async Task<Container> GetEventStoreContainerAsync(bool allowBulk = false)
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
        Container bulkContainer = await GetBulkCurrentStateContainerAsync<T>(partitionKeyPath);

        //Remove all items using bulk delete to start from scratch
        await bulkContainer.DeleteAllBulkAsync<T>();

        List<T> rehydratedAggregates = new List<T>();

        Container eventStore = await GetEventStoreContainerAsync();
        //Get list of distinct aggregate root ids
        List<Guid> uniqueAggregateRootIds = await eventStore.GetItemLinqQueryable<IEvent>()
            .Select(pe => pe.aggregateRootId)
            .Distinct()
            .ReadAllAsync();

        // Query events for at most 1,000 aggregate roots at a time, then rehydrate them.
        foreach (List<Guid> aggregateRootIdBatch in BatchAggregateRootIds(uniqueAggregateRootIds))
        {
            var peList = await eventStore.GetItemLinqQueryable<IEvent>()
                .Where(pe => aggregateRootIdBatch.Contains(pe.aggregateRootId))
                .ReadAllAsync();

            aggregateRootIdBatch.ForEach(id =>
            {
                rehydratedAggregates.Add(Rehydrate<T>(peList.Where(e => e.aggregateRootId == id).OrderBy(e => e.timestamp).ToList()));
            });
        }

        //Save using bulk operations
        List<Task> saveTasks = new List<Task>();
        rehydratedAggregates.ForEach(agg =>
        {
            saveTasks.Add(bulkContainer.UpsertItemAsync<T>(agg));
        });
        await Task.WhenAll(saveTasks);
    }

    /// <summary>
    /// Splits aggregate root identifiers into bounded query batches without omissions or duplicates.
    /// </summary>
    internal static IEnumerable<List<Guid>> BatchAggregateRootIds(List<Guid> aggregateRootIds, int batchSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(aggregateRootIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        for (int index = 0; index < aggregateRootIds.Count; index += batchSize)
        {
            int count = Math.Min(batchSize, aggregateRootIds.Count - index);
            yield return aggregateRootIds.GetRange(index, count);
        }
    }

    ///<summary>
    ///Rehydrates data directly from stream of events passed from calling method.
    ///</summary>
    ///<returns>
    ///The projection state rehydrated to the extent of the events fed into it.
    ///</returns>
    ///<param name="peList">The event stream for the aggregate to be rehydrated</param>
    private static T Rehydrate<T>(List<IEvent> peList) where T : NostifyObject, new()
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
    public async Task<Container> GetSequenceContainerAsync()
    {
        return await GetContainerAsync(Repository.SequenceContainer, false, "/partitionKey");
    }

    ///<inheritdoc />
    public async Task<long> GetNextSequenceValueAsync(string sequenceName, string partitionKeyValue)
    {
        return await GetNextSequenceValueAsync(sequenceName, partitionKeyValue, 0);
    }

    ///<inheritdoc />
    public async Task<long> GetNextSequenceValueAsync(string sequenceName, string partitionKeyValue, long startingValue)
    {
        var container = await GetSequenceContainerAsync();
        var documentId = Sequence.GenerateId(partitionKeyValue, sequenceName);
        var partitionKey = new PartitionKey(partitionKeyValue);

        const int maxRetries = 3;
        int retryCount = 0;

        while (true)
        {
            try
            {
                // Try to read the existing sequence
                var response = await container.ReadItemAsync<Sequence>(documentId, partitionKey);

                // Sequence exists, increment atomically using patch
                var patchOperations = new List<PatchOperation>
                {
                    PatchOperation.Increment("/currentValue", 1)
                };

                var patchResponse = await container.PatchItemAsync<Sequence>(documentId, partitionKey, patchOperations);
                return patchResponse.Resource.currentValue;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Sequence doesn't exist, create it
                try
                {
                    var newSequence = new Sequence(sequenceName, partitionKeyValue, startingValue);
                    await container.CreateItemAsync(newSequence, partitionKey);

                    // Now increment and return
                    var patchOperations = new List<PatchOperation>
                    {
                        PatchOperation.Increment("/currentValue", 1)
                    };

                    var patchResponse = await container.PatchItemAsync<Sequence>(documentId, partitionKey, patchOperations);
                    return patchResponse.Resource.currentValue;
                }
                catch (CosmosException createEx) when (createEx.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // Another process created the sequence, retry the read
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        throw new NostifyException($"Failed to get next sequence value after {maxRetries} retries due to concurrent creation conflicts.");
                    }

                    // Exponential backoff
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 50));
                    continue;
                }
            }
        }
    }

    ///<inheritdoc />
    public async Task<long> GetNextSequenceValueAsync(string sequenceName, Guid partitionKeyValue)
    {
        return await GetNextSequenceValueAsync(sequenceName, partitionKeyValue.ToString(), 0);
    }

    ///<inheritdoc />
    public async Task<long> GetNextSequenceValueAsync(string sequenceName, Guid partitionKeyValue, long startingValue)
    {
        return await GetNextSequenceValueAsync(sequenceName, partitionKeyValue.ToString(), startingValue);
    }

    ///<inheritdoc />
    public async Task<SequenceRange> GetNextSequenceValuesAsync(string sequenceName, string partitionKeyValue, int count)
    {
        return await GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, count, 0);
    }

    ///<inheritdoc />
    public async Task<SequenceRange> GetNextSequenceValuesAsync(string sequenceName, string partitionKeyValue, int count, long startingValue)
    {
        if (count <= 0)
        {
            throw new ArgumentException("Count must be greater than zero.", nameof(count));
        }

        var container = await GetSequenceContainerAsync();
        var documentId = Sequence.GenerateId(partitionKeyValue, sequenceName);
        var partitionKey = new PartitionKey(partitionKeyValue);

        const int maxRetries = 3;
        int retryCount = 0;

        while (true)
        {
            try
            {
                // Try to read the existing sequence to get the current value before incrementing
                var response = await container.ReadItemAsync<Sequence>(documentId, partitionKey);
                long currentValue = response.Resource.currentValue;

                // Sequence exists, increment atomically by count using patch
                var patchOperations = new List<PatchOperation>
                {
                    PatchOperation.Increment("/currentValue", count)
                };

                await container.PatchItemAsync<Sequence>(documentId, partitionKey, patchOperations);

                // Return the range: from (currentValue + 1) to (currentValue + count)
                return new SequenceRange(currentValue + 1, currentValue + count);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Sequence doesn't exist, create it
                try
                {
                    var newSequence = new Sequence(sequenceName, partitionKeyValue, startingValue);
                    await container.CreateItemAsync(newSequence, partitionKey);

                    // Now increment by count and return
                    var patchOperations = new List<PatchOperation>
                    {
                        PatchOperation.Increment("/currentValue", count)
                    };

                    await container.PatchItemAsync<Sequence>(documentId, partitionKey, patchOperations);

                    // Return the range: from (startingValue + 1) to (startingValue + count)
                    return new SequenceRange(startingValue + 1, startingValue + count);
                }
                catch (CosmosException createEx) when (createEx.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // Another process created the sequence, retry the read
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        throw new NostifyException($"Failed to get next sequence values after {maxRetries} retries due to concurrent creation conflicts.");
                    }

                    // Exponential backoff
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 50));
                    continue;
                }
            }
        }
    }

    ///<inheritdoc />
    public async Task<SequenceRange> GetNextSequenceValuesAsync(string sequenceName, Guid partitionKeyValue, int count)
    {
        return await GetNextSequenceValuesAsync(sequenceName, partitionKeyValue.ToString(), count, 0);
    }

    ///<inheritdoc />
    public async Task<SequenceRange> GetNextSequenceValuesAsync(string sequenceName, Guid partitionKeyValue, int count, long startingValue)
    {
        return await GetNextSequenceValuesAsync(sequenceName, partitionKeyValue.ToString(), count, startingValue);
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
            if (Logger != null) LogDefaultEmulatorThroughput(Logger, null);
            else Console.WriteLine("Using default throughput of 400 for local emulator since none was set. This will probably be really slow.");
        }

        if (localhostOnly && !Repository.IsLocalEmulator)
        {
            if (Logger != null) LogContainerCreationSkippedOutsideLocalhost(Logger, null);
            else Console.WriteLine("Not running on localhost. Containers will not be created.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Repository.ConnectionString))
        {
            if (Logger != null) LogContainerCreationSkippedWithoutConnectionString(Logger, null);
            else Console.WriteLine("Connection string is null or empty. Containers will not be created.");
            return;
        }

        if (Repository.DbName == null)
        {
            if (Logger != null) LogContainerCreationSkippedWithoutDatabaseName(Logger, null);
            else Console.WriteLine("Database name is null or empty. Containers will not be created.");
            return;
        }

        // get the calling assembly
        var assembly = typeof(TTypeInAssembly).Assembly;

        // Create the event store container
        await CreateContainerAsync(Repository.EventStoreContainer, Repository.EventStorePartitionKey, throughput, verbose);

        // Create the sequence container
        await CreateContainerAsync(Repository.SequenceContainer, "/partitionKey", throughput, verbose);

        // Create the undeliverable events container
        await CreateContainerAsync(Repository.UndeliverableEvents, "/aggregateRootId", throughput, verbose);

        // Create the containers for the aggregates and projections
        foreach (var containerName in EnumerateContainerNames(assembly))
        {
            await CreateContainerAsync(containerName, throughput: throughput, verbose: verbose);
        }
    }

    private const string NoHttpClientErrorMessage = "HttpClientFactory is not set. Call .WithHttp() in the NostifyFactory config during startup to use this method.";

    ///<inheritdoc />
    public async Task<List<P>> InitAsync<P, A>(Guid id) where P : NostifyObject, IProjection, IHasExternalData<P>, new() where A : IAggregate
    {
        //throw error if no HttpClientFactory
        if (HttpClientFactory == null)
        {
            throw new InvalidOperationException(NoHttpClientErrorMessage);
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
            throw new InvalidOperationException(NoHttpClientErrorMessage);
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
            throw new InvalidOperationException(NoHttpClientErrorMessage);
        }
        // Use the HttpClientFactory to create a new HttpClient instance
        var httpClient = HttpClientFactory.CreateClient();
        return await ProjectionInitializer.InitAsync<P>(projectionsToInit, this, httpClient);
    }

    ///<inheritdoc />
    public async Task InitContainerAsync<P, A>(string partitionKeyPath = "/tenantId", int loopSize = 100) where A : IAggregate where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        //throw error if no HttpClientFactory
        if (HttpClientFactory == null)
        {
            throw new InvalidOperationException(NoHttpClientErrorMessage);
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
            throw new InvalidOperationException(NoHttpClientErrorMessage);
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
            if (verbose || Logger != null)
            {
                if (Logger != null) LogCreatingContainer(Logger, containerName, partitionKeyPath, throughput, null);
                else Console.WriteLine($"Creating container {containerName} with partition key path {partitionKeyPath} and throughput {throughput}, if it does not already exist");
            }
            await Repository.GetContainerAsync(containerName, partitionKeyPath, throughput: throughput, verbose: verbose);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            if (Logger != null) LogDatabaseNotFound(Logger, Repository.DbName, ex);
            else Console.WriteLine($"Database not found: {Repository.DbName}");
            throw;
        }
        catch (Exception ex)
        {
            if (Logger != null) LogContainerCreationFailure(Logger, containerName, ex);
            else Console.WriteLine($"An error occurred while creating or retrieving the container {containerName}: {ex.Message}");
            throw;
        }
    }

    private static IEvent DeserializeRequiredEvent(string serializedTriggerEvent)
    {
        NostifyKafkaTriggerEvent triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(serializedTriggerEvent)
            ?? throw new NostifyException("Event is null");

        return triggerEvent.GetIEvent() ?? throw new NostifyException("Event is null");
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

    private static string? GetPropertyValue(Type type, string propertyName)
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
