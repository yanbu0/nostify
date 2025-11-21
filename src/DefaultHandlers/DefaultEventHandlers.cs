
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using System.Linq;

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
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="filterExpression">Predicate used to select which projections should receive the event.</param>
    /// <param name="eventTypeFilter">Optional filter specifying which event type to process.</param>
    /// <param name="batchSize">Maximum number of projections to apply per batch.</param>
    /// <param name="allowRetry">Indicates whether retry logic should be used for transient failures (currently unused).</param>
    /// <param name="publishErrorEvents">Indicates whether error events should be published on failure (currently unused).</param>
    /// <returns>A task representing the asynchronous multi-apply operation.</returns>
    public async static Task HandleMultiApplyEvent<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, Func<P, bool> filterExpression, string? eventTypeFilter = null, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false) where P : NostifyObject, IProjection, new()
    {
        Event? newEvent = triggerEvent.GetEvent();
        try
        {
            if (newEvent != null)
            {
                //Update projection container
                Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
                //Get all projection ids that need to be updated
                List<Guid> projectionsToUpdate = await projectionContainer
                    .FilteredQuery<P>(newEvent.partitionKey, filterExpression)
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
    /// Default handler for the Create, Update, Delete events by applying the event to the projection of the specified type.
    /// </summary>
    /// <typeparam name="P">The aggregate type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task HandleProjectionEvent<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, string? eventTypeFilter = null) where P : NostifyObject, IProjection, new()
    {
        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter); 
        try
        {
            if (newEvent != null)
            {
                //Update aggregate current state projection
                Container currentStateContainer = await nostify.GetProjectionContainerAsync<P>();
                await currentStateContainer.ApplyAndPersistAsync<P>(newEvent);
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
}