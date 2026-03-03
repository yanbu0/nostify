
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace nostify;

/// <summary>
/// Provides default event handlers for common nostify operations.
/// </summary>
public static class DefaultEventHandlers
{
    #region Async Methods

    /// <summary>
    /// Default handler for the Create, Update, Delete events by applying the event to the current state projection of the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="idToApplyToPropertyName">Optional property name in the event payload to extract the projection base aggregate ID from.
    /// Will apply to this aggregate rather than the aggregateRootId of the Event. Use when Events can have effects on other aggregates.</param>
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior.</param>
    /// <param name="logger">Optional logger for structured logging of retry operations. When provided, automatically enables logging on RetryOptions.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task<T?> HandleAggregateEventAsync<T>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, string? idToApplyToPropertyName = null, string? eventTypeFilter = null, RetryOptions? retryOptions = null, ILogger? logger = null) where T : NostifyObject, IAggregate, new()
    {
        Event newEvent = triggerEvent.GetEvent(eventTypeFilter) ?? throw new NostifyException("No event found in trigger event for the specified event type filter");
        try
        {
            // Wire logger into retryOptions if provided
            logger ??= nostify.Logger;
            if (logger != null && retryOptions != null)
            {
                retryOptions.Logger ??= logger;
                retryOptions.LogRetries = true;
            }
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
            
            Task<T?> doApply;
            if (retryOptions != null)
            {
                var retryable = currentStateContainer.WithRetry(retryOptions);
                doApply = projectionBaseAggregateId.HasValue 
                    ? retryable.ApplyAndPersistAsync<T>(newEvent, projectionBaseAggregateId.Value,
                        onExhausted: () => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}:Retry", 
                            $"Not found after {retryOptions.MaxRetries} retries", newEvent),
                        onNotFound: () => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}:NotFound", 
                            "Not found and RetryWhenNotFound is false", newEvent),
                        onException: (ex) => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}", 
                            ex.Message, newEvent))
                    : retryable.ApplyAndPersistAsync<T>(newEvent,
                        onExhausted: () => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}:Retry", 
                            $"Not found after {retryOptions.MaxRetries} retries", newEvent),
                        onNotFound: () => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}:NotFound", 
                            "Not found and RetryWhenNotFound is false", newEvent),
                        onException: (ex) => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}", 
                            ex.Message, newEvent));
            }
            else
            {
                doApply = projectionBaseAggregateId.HasValue 
                    ? currentStateContainer.ApplyAndPersistAsync<T>(newEvent, projectionBaseAggregateId.Value)
                    : currentStateContainer.ApplyAndPersistAsync<T>(newEvent);
            }

            return await doApply;                       
        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}", 
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
    /// <returns>A task containing the number of successfully updated projections.</returns>
    public async static Task<int> HandleMultiApplyEventAsync<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, Func<P, Guid?> foreignIdSelector, string? eventTypeFilter = null, int batchSize = 100) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
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
                //Use MultiApplyAndPersist to update all projections in one go
                List<P> updatedProjections = await nostify.MultiApplyAndPersistAsync<P>(projectionContainer,
                    newEvent,
                    projectionsToUpdate,
                    batchSize);
                // Need to init to fetch any external data for the projections that were updated
                await nostify.InitAllUninitializedAsync<P>();
                
                return updatedProjections.Count;
            }

            return 0;
        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}", 
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
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="httpClient">The HTTP client used to fetch external data for projection initialization. 
    /// If null will not fetch any external data. Use null when no external data is needed to improve performance and
    /// lower resource utilization.</param>
    /// <param name="idToApplyToPropertyName">Optional property name in the event payload to extract the projection base aggregate ID from.
    /// Will apply to this projection rather than the aggregateRootId of the Event. Use when Events can have effects on other projections.</param>
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior.</param>
    /// <param name="logger">Optional logger for structured logging of retry operations. When provided, automatically enables logging on RetryOptions.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task<P?> HandleProjectionEventAsync<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, HttpClient? httpClient, string? idToApplyToPropertyName = null, string? eventTypeFilter = null, RetryOptions? retryOptions = null, ILogger? logger = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        Event newEvent = triggerEvent.GetEvent(eventTypeFilter) ?? throw new NostifyException("No event found in trigger event for the specified event type filter"); 
        try
        {
            // Wire logger into retryOptions if provided
            logger ??= nostify.Logger;
            if (logger != null && retryOptions != null)
            {
                retryOptions.Logger ??= logger;
                retryOptions.LogRetries = true;
            }
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
            P? projection;

            Task<P?> doApply;
            if (retryOptions != null)
            {
                var retryable = currentStateContainer.WithRetry(retryOptions);
                doApply = projectionBaseAggregateId.HasValue 
                    ? retryable.ApplyAndPersistAsync<P>(newEvent, projectionBaseAggregateId.Value,
                        onExhausted: () => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}:Retry", 
                            $"Not found after {retryOptions.MaxRetries} retries", newEvent),
                        onNotFound: () => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}:NotFound", 
                            "Not found and RetryWhenNotFound is false", newEvent),
                        onException: (ex) => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}", 
                            ex.Message, newEvent))
                    : retryable.ApplyAndPersistAsync<P>(newEvent,
                        onExhausted: () => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}:Retry", 
                            $"Not found after {retryOptions.MaxRetries} retries", newEvent),
                        onNotFound: () => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}:NotFound", 
                            "Not found and RetryWhenNotFound is false", newEvent),
                        onException: (ex) => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}", 
                            ex.Message, newEvent));
            }
            else
            {
                doApply = projectionBaseAggregateId.HasValue 
                    ? currentStateContainer.ApplyAndPersistAsync<P>(newEvent, projectionBaseAggregateId.Value)
                    : currentStateContainer.ApplyAndPersistAsync<P>(newEvent);
            }
            projection = await doApply;
            //Initialize projection with external data (null-check - projection may have been deleted)
            if (projection != null)
            {
                await projection.InitAsync(nostify, httpClient);
            }
            return projection;
                                    
        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}", 
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
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleAggregateBulkCreateEventAsync<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkCreateEventAsync<T>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleAggregateBulkCreateEventAsync<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkCreateEventAsync<T>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to create or update current state projections for the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleAggregateBulkCreateEventAsync<T>(INostify nostify, string[] events, List<string> eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        
        try
        {
            // Count events that match the filter (BulkCreate is all-or-nothing on success)
            int matchingEventCount = events.Count(eventStr =>
            {
                var triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                return triggerEvent?.GetEvent(eventTypeFilter) != null;
            });

            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<T>(events, eventTypeFilter);

            return matchingEventCount;
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(T)}", 
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
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleProjectionBulkCreateEventAsync<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkCreateEventAsync<P>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleProjectionBulkCreateEventAsync<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkCreateEventAsync<P>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to create or update projections for the specified projection type.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleProjectionBulkCreateEventAsync<P>(INostify nostify, string[] events, List<string> eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        
        try
        {
            // Count events that match the filter (BulkCreate is all-or-nothing on success)
            int matchingEventCount = events.Count(eventStr =>
            {
                var triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                return triggerEvent?.GetEvent(eventTypeFilter) != null;
            });

            Container currentStateContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<P>(events, eventTypeFilter);
            await nostify.InitAllUninitializedAsync<P>();

            return matchingEventCount;
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{nameof(P)}", 
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
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleAggregateBulkUpdateEventAsync<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkUpdateEventAsync<T>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events without event type filtering, with retry options.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleAggregateBulkUpdateEventAsync<T>(INostify nostify, string[] events, RetryOptions retryOptions) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkUpdateEventAsync<T>(nostify, events, new List<string>(), retryOptions);
    }

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events with a single event type filter.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleAggregateBulkUpdateEventAsync<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkUpdateEventAsync<T>(nostify, events, new List<string>() { eventTypeFilter });
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
    /// <param name="logger">Optional logger for structured logging of retry operations. When provided, automatically enables logging on RetryOptions.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleAggregateBulkUpdateEventAsync<T>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null, ILogger? logger = null) where T : NostifyObject, IAggregate, new()
    {
        retryOptions ??= new RetryOptions();
        // Wire logger into retryOptions if provided
        logger ??= nostify.Logger;
        if (logger != null)
        {
            retryOptions.Logger ??= logger;
            retryOptions.LogRetries = true;
        }
        try
        {
            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            var retryable = currentStateContainer.WithRetry(retryOptions);
            ConcurrentBag<T> updatedAggregates = new ConcurrentBag<T>();
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
                                T? result = await retryable.ApplyAndPersistAsync<T>(newEvent,
                                    onExhausted: () => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleAggregateBulkUpdateEventAsync)}:{nameof(T)}:Retry", 
                                        $"Not found after {retryOptions.MaxRetries} retries",
                                        newEvent),
                                    onNotFound: () => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleAggregateBulkUpdateEventAsync)}:{nameof(T)}:NotFound", 
                                        "Not found and RetryWhenNotFound is false",
                                        newEvent),
                                    onException: (ex) => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleAggregateBulkUpdateEventAsync)}:{nameof(T)}", 
                                        ex.Message ?? "Unknown error",
                                        newEvent)
                                );

                                if (result != null)
                                {
                                    updatedAggregates.Add(result);
                                }
                            }));
                    }
                }
            }
            
            await Task.WhenAll(tasks);
            return updatedAggregates.Count;
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateBulkUpdateEventAsync)}:{nameof(T)}", 
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
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleProjectionBulkUpdateEventAsync<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkUpdateEventAsync<P>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events without event type filtering.
    /// Uses ApplyAndPersistAsync to update each projection's state with retry options.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleProjectionBulkUpdateEventAsync<P>(INostify nostify, string[] events, RetryOptions retryOptions) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkUpdateEventAsync<P>(nostify, events, new List<string>(), retryOptions);
    }

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events with a single event type filter.
    /// Uses ApplyAndPersistAsync to update each projection's state.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleProjectionBulkUpdateEventAsync<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkUpdateEventAsync<P>(nostify, events, new List<string>() { eventTypeFilter });
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
    /// <param name="logger">Optional logger for structured logging of retry operations. When provided, automatically enables logging on RetryOptions.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleProjectionBulkUpdateEventAsync<P>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null, ILogger? logger = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        retryOptions ??= new RetryOptions();
        // Wire logger into retryOptions if provided
        logger ??= nostify.Logger;
        if (logger != null)
        {
            retryOptions.Logger ??= logger;
            retryOptions.LogRetries = true;
        }
        try
        {
            Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            var retryable = projectionContainer.WithRetry(retryOptions);
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
                                P? result = await retryable.ApplyAndPersistAsync<P>(newEvent,
                                    onExhausted: () => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleProjectionBulkUpdateEventAsync)}:{nameof(P)}:Retry", 
                                        $"Not found after {retryOptions.MaxRetries} retries",
                                        newEvent),
                                    onNotFound: () => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleProjectionBulkUpdateEventAsync)}:{nameof(P)}:NotFound", 
                                        "Not found and RetryWhenNotFound is false",
                                        newEvent),
                                    onException: (ex) => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleProjectionBulkUpdateEventAsync)}:{nameof(P)}", 
                                        ex.Message ?? "Unknown error",
                                        newEvent)
                                );

                                if (result != null)
                                {
                                    updatedProjections.Add(result);
                                }
                            }));
                    }
                }
            }

            await Task.WhenAll(tasks);
            
            await nostify.InitAsync<P>(updatedProjections.ToList());

            return updatedProjections.Count;
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionBulkUpdateEventAsync)}:{nameof(P)}", 
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
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleAggregateBulkDeleteEventAsync<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkDeleteEventAsync<T>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleAggregateBulkDeleteEventAsync<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkDeleteEventAsync<T>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to delete current state projections for the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleAggregateBulkDeleteEventAsync<T>(INostify nostify, string[] events, List<string> eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        
        try
        {
            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            int deletedCount = await currentStateContainer.BulkDeleteFromEventsAsync<T>(events);
            return deletedCount;
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateBulkDeleteEventAsync)}:{nameof(T)}", 
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
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleProjectionBulkDeleteEventAsync<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, new()
    {
        return await HandleProjectionBulkDeleteEventAsync<P>(nostify, events, new List<string>());
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleProjectionBulkDeleteEventAsync<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, new()
    {
        return await HandleProjectionBulkDeleteEventAsync<P>(nostify, events, new List<string>() { eventTypeFilter });
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to delete projections for the specified projection type.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleProjectionBulkDeleteEventAsync<P>(INostify nostify, string[] events, List<string> eventTypeFilter) where P : NostifyObject, IProjection, new()
    {
        
        try
        {
            Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            int deletedCount = await projectionContainer.BulkDeleteFromEventsAsync<P>(events);
            return deletedCount;
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionBulkDeleteEventAsync)}:{nameof(P)}", 
                    e.Message, 
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, Guid.Empty)
                    );
            });
            throw;
        }
    }

    #endregion

    #region Deprecated Methods - Use Async versions instead

    /// <summary>
    /// Default handler for the Create, Update, Delete events by applying the event to the current state projection of the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="idToApplyToPropertyName">Optional property name in the event payload to extract the projection base aggregate ID from.
    /// Will apply to this aggregate rather than the aggregateRootId of the Event. Use when Events can have effects on other aggregates.</param>
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateEventAsync instead. This method will be removed in a future version.")]
    public static Task<T?> HandleAggregateEvent<T>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, string? idToApplyToPropertyName = null, string? eventTypeFilter = null, RetryOptions? retryOptions = null) where T : NostifyObject, IAggregate, new()
        => HandleAggregateEventAsync<T>(nostify, triggerEvent, idToApplyToPropertyName, eventTypeFilter, retryOptions);

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
    [Obsolete("Use HandleMultiApplyEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleMultiApplyEvent<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, Func<P, Guid?> foreignIdSelector, string? eventTypeFilter = null, int batchSize = 100) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleMultiApplyEventAsync<P>(nostify, triggerEvent, foreignIdSelector, eventTypeFilter, batchSize);

    /// <summary>
    /// Default handler for Projection events by applying the event to the projection of the specified type.
    /// This handler is used for events that may require external to this service data to be retrieved with HttpClient for projection initialization.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="httpClient">The HTTP client used to fetch external data for projection initialization. 
    /// If null will not fetch any external data. Use null when no external data is needed to improve performance and
    /// lower resource utilization.</param>
    /// <param name="idToApplyToPropertyName">Optional property name in the event payload to extract the projection base aggregate ID from.
    /// Will apply to this projection rather than the aggregateRootId of the Event. Use when Events can have effects on other projections.</param>
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionEventAsync instead. This method will be removed in a future version.")]
    public static Task<P?> HandleProjectionEvent<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, HttpClient? httpClient, string? idToApplyToPropertyName = null, string? eventTypeFilter = null, RetryOptions? retryOptions = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleProjectionEventAsync<P>(nostify, triggerEvent, httpClient, idToApplyToPropertyName, eventTypeFilter, retryOptions);

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkCreateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkCreateEvent<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkCreateEventAsync<T>(nostify, events);

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkCreateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkCreateEvent<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkCreateEventAsync<T>(nostify, events, eventTypeFilter);

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to create or update current state projections for the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkCreateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkCreateEvent<T>(INostify nostify, string[] events, List<string> eventTypeFilter) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkCreateEventAsync<T>(nostify, events, eventTypeFilter);

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkCreateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleProjectionBulkCreateEventAsync<P>(nostify, events);

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkCreateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleProjectionBulkCreateEventAsync<P>(nostify, events, eventTypeFilter);

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to create or update projections for the specified projection type.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkCreateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events, List<string> eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleProjectionBulkCreateEventAsync<P>(nostify, events, eventTypeFilter);

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events without event type filtering.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkUpdateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkUpdateEventAsync<T>(nostify, events);

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events without event type filtering, with retry options.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkUpdateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events, RetryOptions retryOptions) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkUpdateEventAsync<T>(nostify, events, retryOptions);

    /// <summary>
    /// Handles bulk update of aggregate events from Kafka trigger events with a single event type filter.
    /// Uses ApplyAndPersistAsync to update each aggregate's current state.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkUpdateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkUpdateEventAsync<T>(nostify, events, eventTypeFilter);

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
    [Obsolete("Use HandleAggregateBulkUpdateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkUpdateEventAsync<T>(nostify, events, eventTypeFilter, retryOptions);

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events without event type filtering.
    /// Uses ApplyAndPersistAsync to update each projection's state.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkUpdateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleProjectionBulkUpdateEventAsync<P>(nostify, events);

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events without event type filtering.
    /// Uses ApplyAndPersistAsync to update each projection's state with retry options.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkUpdateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events, RetryOptions retryOptions) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleProjectionBulkUpdateEventAsync<P>(nostify, events, retryOptions);

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events with a single event type filter.
    /// Uses ApplyAndPersistAsync to update each projection's state.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkUpdateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleProjectionBulkUpdateEventAsync<P>(nostify, events, eventTypeFilter);

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
    [Obsolete("Use HandleProjectionBulkUpdateEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
        => HandleProjectionBulkUpdateEventAsync<P>(nostify, events, eventTypeFilter, retryOptions);

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkDeleteEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkDeleteEvent<T>(INostify nostify, string[] events) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkDeleteEventAsync<T>(nostify, events);

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkDeleteEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkDeleteEvent<T>(INostify nostify, string[] events, string eventTypeFilter) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkDeleteEventAsync<T>(nostify, events, eventTypeFilter);

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to delete current state projections for the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleAggregateBulkDeleteEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleAggregateBulkDeleteEvent<T>(INostify nostify, string[] events, List<string> eventTypeFilter) where T : NostifyObject, IAggregate, new()
        => HandleAggregateBulkDeleteEventAsync<T>(nostify, events, eventTypeFilter);

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkDeleteEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkDeleteEvent<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, new()
        => HandleProjectionBulkDeleteEventAsync<P>(nostify, events);

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkDeleteEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkDeleteEvent<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, new()
        => HandleProjectionBulkDeleteEventAsync<P>(nostify, events, eventTypeFilter);

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to delete projections for the specified projection type.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Obsolete("Use HandleProjectionBulkDeleteEventAsync instead. This method will be removed in a future version.")]
    public static Task HandleProjectionBulkDeleteEvent<P>(INostify nostify, string[] events, List<string> eventTypeFilter) where P : NostifyObject, IProjection, new()
        => HandleProjectionBulkDeleteEventAsync<P>(nostify, events, eventTypeFilter);

    #endregion
}
