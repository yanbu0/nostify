
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json;

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
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateEvent<T>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, string? eventTypeFilter = null) where T : NostifyObject, IAggregate, new()
    {
        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
        try
        {
            if (newEvent != null)
            {
                //Update aggregate current state projection
                Container currentStateContainer = await nostify.GetCurrentStateContainerAsync<T>();
                await currentStateContainer.ApplyAndPersistAsync<T>(newEvent);
            }                           
        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEvent)}:{nameof(T)}", 
                e.Message, 
                newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleAggregateEvent, Guid.Empty)
                );
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
    public async static Task HandleMultiApplyEvent<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, Func<P, Guid?> foreignIdSelector, string? eventTypeFilter = null, int batchSize = 100) where P : NostifyObject, IProjection, new()
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
                
            }                       

        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEvent)}:{nameof(P)}", 
                e.Message, 
                newEvent ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleMultiApplyEvent, Guid.Empty)
                );
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
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionEvent<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, HttpClient? httpClient, string? eventTypeFilter = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter); 
        try
        {
            if (newEvent != null)
            {
                //Update aggregate current state projection
                Container currentStateContainer = await nostify.GetProjectionContainerAsync<P>();
                P projection = await currentStateContainer.ApplyAndPersistAsync<P>(newEvent);
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
        }
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, new()
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
    public async static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, new()
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
    public async static Task HandleProjectionBulkCreateEvent<P>(INostify nostify, string[] events, List<string> eventTypeFilter) where P : NostifyObject, IProjection, new()
    {
        
        try
        {
            Container currentStateContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<P>(events, eventTypeFilter);
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
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleAggregateBulkUpdateEvent<T>(INostify nostify, string[] events, List<string> eventTypeFilter) where T : NostifyObject, IAggregate, new()
    {
        
        try
        {
            Container currentStateContainer = await nostify.GetCurrentStateContainerAsync<T>();
            List<Task> tasks = new List<Task>();
            
            foreach (var eventStr in events)
            {
                NostifyKafkaTriggerEvent? triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                if (triggerEvent is not null)
                {
                    Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
                    if (newEvent is not null)
                    {
                        tasks.Add(currentStateContainer.ApplyAndPersistAsync<T>(newEvent));
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
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleAggregateEvent, Guid.Empty)
                    );
            });
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
    public async static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events) where P : NostifyObject, IProjection, new()
    {
        await HandleProjectionBulkUpdateEvent<P>(nostify, events, new List<string>());
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
    public async static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events, string eventTypeFilter) where P : NostifyObject, IProjection, new()
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
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionBulkUpdateEvent<P>(INostify nostify, string[] events, List<string> eventTypeFilter) where P : NostifyObject, IProjection, new()
    {
        
        try
        {
            Container projectionContainer = await nostify.GetProjectionContainerAsync<P>();
            List<Task> tasks = new List<Task>();
            
            foreach (var eventStr in events)
            {
                NostifyKafkaTriggerEvent? triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                if (triggerEvent is not null)
                {
                    Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
                    if (newEvent is not null)
                    {
                        tasks.Add(projectionContainer.ApplyAndPersistAsync<P>(newEvent));
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
                await nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionBulkUpdateEvent)}:{nameof(P)}", 
                    e.Message, 
                    @event ?? new EventFactory().NoValidate().CreateNullPayloadEvent(ErrorCommand.HandleProjection, Guid.Empty)
                    );
            });
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
        }
    }
}