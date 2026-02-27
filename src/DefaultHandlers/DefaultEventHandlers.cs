
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace nostify;

/// <summary>
/// Provides default event handlers for common nostify operations.
/// </summary>
public static class DefaultEventHandlers
{
    /// <summary>
    /// Default handler for the Create, Update, Delete events by applying the event to the current state projection of the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="idToApplyToPropertyName">Optional property name in the event payload to extract the projection base aggregate ID from.
    /// Will apply to this aggregate rather than the aggregateRootId of the Event. Use when Events can have effects on other aggregates.</param>
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateEvent<T>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, string? idToApplyToPropertyName = null, string? eventTypeFilter = null) where T : NostifyObject, IAggregate, new()
    {
        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
        try
        {
            if (newEvent != null)
            {
                // If idToApplyToPropertyName is provided, use it to determine the projectionBaseAggregateId
                Guid? projectionBaseAggregateId = null;
                if (!string.IsNullOrEmpty(idToApplyToPropertyName))
                {
                    var payloadDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(newEvent.payload));
                    if (payloadDict != null && payloadDict.TryGetValue(idToApplyToPropertyName, out var idValue) && Guid.TryParse(idValue.ToString(), out var parsedId))
                    {
                        projectionBaseAggregateId = parsedId;
                    }
                }
                //Update aggregate current state projection
                Container currentStateContainer = await nostify.GetCurrentStateContainerAsync<T>();
                Task doApply = projectionBaseAggregateId.HasValue ? currentStateContainer.ApplyAndPersistAsync<T>(newEvent, projectionBaseAggregateId.Value)
                    : currentStateContainer.ApplyAndPersistAsync<T>(newEvent);
                await doApply;
            }                           
        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEvent)}:{nameof(T)}", 
                e.Message, 
                newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleAggregateEvent, Guid.Empty)
                );
            throw;
        }
    }

    /// <summary>
    /// Default handler that applies a single event to multiple projection instances selected by a filter expression.
    /// Will query the projection container for all projections where the foreign key matches the event's aggregateRootId, 
    /// and apply the event to each of those projections in batches.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="foreignIdSelector">Function that extracts the foreign key from a projection to match against the event's aggregateRootId.</param>
    /// <param name="eventTypeFilter">Optional filter specifying which event type to process.</param>
    /// <param name="batchSize">Maximum number of projections to apply per batch.</param>
    /// <returns>A task representing the asynchronous multi-apply operation.</returns>
    public async static Task HandleMultiApplyEvent<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, Func<P, Guid?> foreignIdSelector, string? eventTypeFilter = null, int batchSize = 100) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
        try
        {
            if (newEvent != null)
            {
                //Update projection container
                Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
                //Get all projection ids that need to be updated
                List<Guid> projectionsToUpdate = await projectionContainer
                    .FilteredQuery<P>(newEvent.partitionKey, p => foreignIdSelector(p) == newEvent.aggregateRootId)
                    .Select(p => p.id)
                    .ReadAllAsync();
                //Use MtultiApplyAndPersist to update all projections in one go
                await nostify.MultiApplyAndPersistAsync<P>(projectionContainer,
                    newEvent,
                    projectionsToUpdate,
                    batchSize);
                // Need to init to fetch any external data for the projections that were updated
                await nostify.InitAllUninitializedAsync<P>();
                
            }                       

        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEvent)}:{nameof(P)}", 
                e.Message, 
                newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleMultiApplyEvent, Guid.Empty)
                );
            throw;
        }
    }

    /// <summary>
    /// Default handler for Projection events by applying the event to the projection of the specified type.
    /// This handler is used for events that may require external to this service data to be retrieved with HttpClient for projection initialization.
    /// </summary>
    /// <typeparam name="P">The aggregate type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="httpClient">The HTTP client used to fetch external data for projection initialization. 
    /// If null will not fetch any external data. Use null when no external data is needed to improve performance and
    /// lower resource utilization.</param>
    /// <param name="idToApplyToPropertyName">Optional property name in the event payload to extract the projection base aggregate ID from.
    /// Will apply to this projection rather than the aggregateRootId of the Event. Use when Events can have effects on other projections.</param>
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionEvent<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, HttpClient? httpClient, string? idToApplyToPropertyName = null, string? eventTypeFilter = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter); 
        try
        {
            if (newEvent != null)
            {
                // If idToApplyToPropertyName is provided, use it to determine the projectionBaseAggregateId
                Guid? projectionBaseAggregateId = null;
                if (!string.IsNullOrEmpty(idToApplyToPropertyName))
                {
                    var payloadDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(newEvent.payload));
                    if (payloadDict != null && payloadDict.TryGetValue(idToApplyToPropertyName, out var idValue) && Guid.TryParse(idValue.ToString(), out var parsedId))
                    {
                        projectionBaseAggregateId = parsedId;
                    }
                }
                //Update projection
                Container currentStateContainer = await nostify.GetProjectionContainerAsync<P>();
                P projection = projectionBaseAggregateId.HasValue 
                    ? await currentStateContainer.ApplyAndPersistAsync<P>(newEvent, projectionBaseAggregateId.Value)
                    : await currentStateContainer.ApplyAndPersistAsync<P>(newEvent);
                //Initialize projection with external data
                await projection.InitAsync(nostify, httpClient);
            }                           
        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEvent)}:{nameof(P)}", 
                e.Message, 
                newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, Guid.Empty)
                );
            throw;
        }
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkCreateEvent<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
    {
        await HandleAggregateBulkCreateEvent<T>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkCreateEvent<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        await HandleAggregateBulkCreateEvent<T>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to create or update current state projections for the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkCreateEvent<T>(INostify nostify, string[] events, List<string> eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        
        try
        {
            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<T>(events, eventTypeFilter);
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEvent)}:{nameof(T)}", 
                    e.Message, 
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, Guid.Empty)
                    );
            });
            throw;
        }
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await HandleProjectionBulkCreateEvent<P>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await HandleProjectionBulkCreateEvent<P>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to create or update projections for the specified projection type.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events, List<string> eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        
        try
        {
            Container currentStateContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<P>(events, eventTypeFilter);
            await nostify.InitAllUninitializedAsync<P>();
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEvent)}:{nameof(P)}", 
                    e.Message, 
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, Guid.Empty)
                    );
            });
            throw;
        }
    }

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events without event type filtering.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
    {
        await HandleAggregateBulkUpdateEvent<T>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events without event type filtering, with retry options.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events, RetryOptions retryOptions) where T : NostifyObject, IAggregate, new()
    {
        await HandleAggregateBulkUpdateEvent<T>(nostify, events, new List<string>(), retryOptions);
    }

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events with a single event type filter.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        await HandleAggregateBulkUpdateEvent<T>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events with multiple event type filters.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state projection.
    /// Supports configurable retry behavior for handling eventual consistency scenarios.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where T : NostifyObject, IAggregate, new()
    {
        retryOptions ??= new RetryOptions();
        try
        {
            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            List<Task> tasks = new List<Task>();
            
            foreach (var eventStr in events)
            {
                NostifyKafkaTriggerEvent? triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                if (triggerEvent is not null)
                {
                    Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
                    if (newEvent is not null)
                    {
                        tasks.Add(Task.Run(async () =>
                            {
                                for (int attempt = 0; attempt <= retryOptions.MaxRetries; attempt++)
                                {
                                    try
                                    {
                                        T? result = await currentStateContainer.ApplyAndPersistAsync<T>(newEvent);
                                        
                                        // ApplyAndPersistAsync returns null when the item is not found in the container
                                        // (it catches CosmosException NotFound internally and returns null)
                                        if (result != null)
                                        {
                                            return;
                                        }
                                        
                                        // Result is null - the aggregate was not found
                                        if (retryOptions.RetryWhenNotFound)
                                        {
                                            if (attempt < retryOptions.MaxRetries)
                                            {
                                                await Task.Delay(retryOptions.Delay);
                                            }
                                            else
                                            {
                                                // Exhausted all retries, handle as undeliverable
                                                await nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateBulkUpdateEvent)}:{nameof(T)}:Retry", 
                                                    $"Not found after {retryOptions.MaxRetries} retries",
                                                    newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleAggregateEvent, Guid.Empty)
                                                    );
                                            }
                                        }
                                        else
                                        {
                                            // Not configured to retry on not found, handle as undeliverable immediately
                                            await nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateBulkUpdateEvent)}:{nameof(T)}:NotFound", 
                                                $"Not found and RetryWhenNotFound is false",
                                                newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleAggregateEvent, Guid.Empty)
                                                );
                                            return;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // For any other exceptions, handle as undeliverable immediately (no retry)
                                        await nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateBulkUpdateEvent)}:{nameof(T)}", 
                                            ex.Message ?? "Unknown error",
                                            newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleAggregateEvent, newEvent?.aggregateRootId ?? Guid.Empty)
                                            );
                                        return;
                                    }
                                }
                            }));
                    }
                }
            }
            
            await Task.WhenAll(tasks);
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateBulkUpdateEvent)}:{nameof(T)}", 
                    e.Message, 
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleAggregateEvent, @event.aggregateRootId)
                    );
            });
            throw;
        }
    }

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events without event type filtering.
    /// Uses ApplyAndPersistAsync to update each projection's state.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await HandleProjectionBulkUpdateEvent<P>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events without event type filtering.
    /// Uses ApplyAndPersistAsync to update each projection's state with retry options.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events, RetryOptions retryOptions) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await HandleProjectionBulkUpdateEvent<P>(nostify, events, new List<string>(), retryOptions);
    }

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events with a single event type filter.
    /// Uses ApplyAndPersistAsync to update each projection's state.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        await HandleProjectionBulkUpdateEvent<P>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events with multiple event type filters.
    /// Uses ApplyAndPersistAsync to update each projection's state.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        retryOptions ??= new RetryOptions();
        try
        {
            Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            List<Task> tasks = new List<Task>();            

            // Need to use thread safe collection here
            ConcurrentBag<P> updatedProjections = new ConcurrentBag<P>();
            foreach (var eventStr in events)
            {
                NostifyKafkaTriggerEvent? triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                if (triggerEvent is not null)
                {
                    Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
                    if (newEvent is not null)
                    {
                        tasks.Add(Task.Run(async () =>
                            {
                                for (int attempt = 0; attempt <= retryOptions.MaxRetries; attempt++)
                                {
                                    try
                                    {
                                        P? result = await projectionContainer.ApplyAndPersistAsync<P>(newEvent);
                                        
                                        // ApplyAndPersistAsync returns null when the item is not found in the container
                                        // (it catches CosmosException NotFound internally and returns null)
                                        if (result != null)
                                        {
                                            // Add the updated projection to the bag so we don't have to query them again to Init
                                            updatedProjections.Add(result);
                                            return;
                                        }
                                        
                                        // Result is null - the projection was not found
                                        if (retryOptions.RetryWhenNotFound)
                                        {
                                            // If the projection to update was not found, we may need to retry if the projection is eventually consistent and may not have been created yet
                                            // This can happen when we have a Create event followed by an Update event for the same projection in quick succession, and the Update is processed before the Create has fully propagated to the point where it can be read by the Update
                                            // In this case we will retry the Update up to MaxRetries times with a delay in between to give the Create time to propagate
                                            if (attempt < retryOptions.MaxRetries)
                                            {
                                                await Task.Delay(retryOptions.Delay);
                                            }
                                            else
                                            {
                                                // Exhausted all retries, handle as undeliverable
                                                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionBulkUpdateEvent)}:{nameof(P)}:Retry", 
                                                    $"Not found after {retryOptions.MaxRetries} retries",
                                                    newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, Guid.Empty)
                                                    );
                                            }
                                        }
                                        else
                                        {
                                            // Not configured to retry on not found, handle as undeliverable immediately
                                            await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionBulkUpdateEvent)}:{nameof(P)}:NotFound", 
                                                $"Not found and RetryWhenNotFound is false",
                                                newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, Guid.Empty)
                                                );
                                            return;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // For any other exceptions, handle as undeliverable immediately (no retry)
                                        await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionBulkUpdateEvent)}:{nameof(P)}", 
                                            ex.Message ?? "Unknown error",
                                            newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, newEvent?.aggregateRootId ?? Guid.Empty)
                                            );
                                        return;
                                    }
                                }
                            }));
                    }
                }
            }

            await Task.WhenAll(tasks);
            
            await nostify.InitAsync<P>(updatedProjections.ToList());
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionBulkUpdateEvent)}:{nameof(P)}", 
                    e.Message, 
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, @event?.aggregateRootId ?? Guid.Empty)
                    );
            });
            throw;
        }
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkDeleteEvent<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
    {
        await HandleAggregateBulkDeleteEvent<T>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkDeleteEvent<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        await HandleAggregateBulkDeleteEvent<T>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to delete current state projections for the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkDeleteEvent<T>(INostify nostify, string[] events, List<string> eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        
        try
        {
            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            await currentStateContainer.BulkDeleteFromEventsAsync<T>(events);
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateBulkDeleteEvent)}:{nameof(T)}", 
                    e.Message, 
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleAggregateEvent, Guid.Empty)
                    );
            });
            throw;
        }
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkDeleteEvent<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, new()
    {
        await HandleProjectionBulkDeleteEvent<P>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkDeleteEvent<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, new()
    {
        await HandleProjectionBulkDeleteEvent<P>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to delete projections for the specified projection type.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkDeleteEvent<P>(INostify nostify, string[] events, List<string> eventTypeFilter) where P : NostifyObject, IProjection, new()
    {
        
        try
        {
            Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            await projectionContainer.BulkDeleteFromEventsAsync<P>(events);
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionBulkDeleteEvent)}:{nameof(P)}", 
                    e.Message, 
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, Guid.Empty)
                    );
            });
            throw;
        }
    }
}