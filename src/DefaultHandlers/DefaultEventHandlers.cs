
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

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
            await nostify.HandleUndeliverableAsync(nameof(T), e.Message, newEvent);
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
            await nostify.HandleUndeliverableAsync(nameof(P), e.Message, newEvent);
        }
    }
}