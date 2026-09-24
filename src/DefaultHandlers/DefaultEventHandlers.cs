
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using System.Linq;
using System.Linq.Expressions;
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
    // Compiled templates keep bulk-handler logging structured and avoid formatting when logging is disabled.
    private static readonly Action<ILogger, string, string, string, Exception?> LogBulkCreateFailure =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            new EventId(1, nameof(LogBulkCreateFailure)),
            "Error in {HandlerName}:{ModelType}, creating undeliverables: {ErrorMessage}");

    private static readonly Action<ILogger, Event, Exception?> LogProjectionUpdateFailure =
        LoggerMessage.Define<Event>(
            LogLevel.Warning,
            new EventId(2, nameof(LogProjectionUpdateFailure)),
            "Failed to update projection for event: {Event}");

    private static readonly Action<ILogger, NostifyKafkaTriggerEvent, Exception?> LogMissingTriggerEvent =
        LoggerMessage.Define<NostifyKafkaTriggerEvent>(
            LogLevel.Warning,
            new EventId(3, nameof(LogMissingTriggerEvent)),
            "Unable to get event from trigger event: {TriggerEvent}");

    private static readonly Action<ILogger, string, Exception?> LogTriggerDeserializationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, nameof(LogTriggerDeserializationFailure)),
            "Failed to deserialize event: {SerializedEvent}");

    #region Async Methods

    private static RetryOptions? ResolveRetryOptions(INostify nostify, RetryOptions? retryOptions, bool allowRetry = true)
    {
        if (!allowRetry)
        {
            return null;
        }

        RetryOptions? source = retryOptions ?? nostify.DefaultRetryOptions;
        if (source == null)
        {
            return null;
        }

        var clonedOptions = new RetryOptions(source)
        {
            LogRetries = true,
            Logger = source.Logger ?? nostify.Logger
        };

        return clonedOptions;
    }

    private static void LogFailedProjectionUpdate(INostify nostify, Event @event)
    {
        if (nostify.Logger != null)
        {
            LogProjectionUpdateFailure(nostify.Logger, @event, null);
        }
    }

    private static void LogUnableToGetEvent(INostify nostify, NostifyKafkaTriggerEvent triggerEvent)
    {
        if (nostify.Logger != null)
        {
            LogMissingTriggerEvent(nostify.Logger, triggerEvent, null);
        }
    }

    private static void LogFailedDeserialization(INostify nostify, string serializedEvent)
    {
        if (nostify.Logger != null)
        {
            LogTriggerDeserializationFailure(nostify.Logger, serializedEvent, null);
        }
    }

    /// <summary>
    /// Default handler for the Create, Update, Delete events by applying the event to the current state projection of the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the event data.</param>
    /// <param name="idToApplyToPropertyName">Optional property name in the event payload to extract the projection base aggregate ID from.
    /// Will apply to this aggregate rather than the aggregateRootId of the Event. Use when Events can have effects on other aggregates.</param>
    /// <param name="eventTypeFilter">Optional filter to specify which event type to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior. When <c>null</c> and <paramref name="allowRetry"/> is <c>true</c>, uses <see cref="INostify.DefaultRetryOptions"/>.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <paramref name="retryOptions"/> when provided, otherwise <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry entirely, even when <paramref name="retryOptions"/> is provided.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task<T?> HandleAggregateEventAsync<T>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, string? idToApplyToPropertyName = null, string? eventTypeFilter = null, RetryOptions? retryOptions = null, bool allowRetry = true) where T : NostifyObject, IAggregate, new()
    {
        Event newEvent = triggerEvent.GetEvent(eventTypeFilter) ?? throw new NostifyException("No event found in trigger event for the specified event type filter");
        try
        {
            RetryOptions? effectiveRetryOptions = ResolveRetryOptions(nostify, retryOptions, allowRetry);
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

            if (effectiveRetryOptions != null)
            {
                var retryable = currentStateContainer.WithRetry(effectiveRetryOptions);
                return projectionBaseAggregateId.HasValue
                    ? await retryable.ApplyAndPersistAsync<T>(newEvent, projectionBaseAggregateId.Value,
                        onExhausted: () => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}:Retry",
                            $"Not found after {effectiveRetryOptions.MaxRetries} retries", newEvent),
                        onNotFound: () => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}:NotFound",
                            "Not found and RetryWhenNotFound is false", newEvent),
                        onException: (ex) => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}",
                            ex.Message, newEvent))
                    : await retryable.ApplyAndPersistAsync<T>(newEvent,
                        onExhausted: () => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}:Retry",
                            $"Not found after {effectiveRetryOptions.MaxRetries} retries", newEvent),
                        onNotFound: () => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}:NotFound",
                            "Not found and RetryWhenNotFound is false", newEvent),
                        onException: (ex) => nostify.HandleUndeliverableAsync($"{nameof(HandleAggregateEventAsync)}:{nameof(T)}",
                            ex.Message, newEvent));
            }

            return projectionBaseAggregateId.HasValue
                ? await currentStateContainer.ApplyAndPersistAsync<T>(newEvent, projectionBaseAggregateId.Value)
                : await currentStateContainer.ApplyAndPersistAsync<T>(newEvent);
        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync(
                $"{nameof(HandleAggregateEventAsync)}:{nameof(T)}",
                e.Message,
                newEvent);
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
    /// <param name="foreignIdSelector">Expression that extracts the foreign key from a projection to match against the event's aggregateRootId.</param>
    /// <param name="eventTypeFilter">Optional filter specifying which event type to process.</param>
    /// <param name="batchSize">Maximum number of projections to apply per batch.</param>
    /// <param name="retryOptions">Optional retry options for configuring per-item retry behavior. When <c>null</c> and <paramref name="allowRetry"/> is <c>true</c>, uses <see cref="INostify.DefaultRetryOptions"/>.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses per-item retry with <paramref name="retryOptions"/> when provided, otherwise <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry entirely, even when <paramref name="retryOptions"/> is provided.</param>
    /// <returns>A task containing the number of successfully updated projections.</returns>
    public async static Task<int> HandleMultiApplyEventAsync<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, Expression<Func<P, Guid?>> foreignIdSelector, string? eventTypeFilter = null, int batchSize = 100, RetryOptions? retryOptions = null, bool allowRetry = true) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
        try
        {
            if (newEvent != null)
            {
                RetryOptions? effectiveRetryOptions = ResolveRetryOptions(nostify, retryOptions, allowRetry);

                //Update projection container
                Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
                //Get all projection ids that need to be updated
                // Compose filter expression tree: p => foreignIdSelector(p) == aggregateRootId
                // This avoids embedding a delegate Invoke node that Cosmos DB LINQ cannot translate
                var selectorParam = foreignIdSelector.Parameters[0];
                var equalsExpr = Expression.Equal(
                    foreignIdSelector.Body,
                    Expression.Constant((Guid?)newEvent.aggregateRootId, typeof(Guid?)));
                var filterExpr = Expression.Lambda<Func<P, bool>>(equalsExpr, selectorParam);

                List<Guid> projectionsToUpdate = await projectionContainer
                    .FilteredQuery<P>(newEvent.partitionKey, filterExpr)
                    .Select(p => p.id)
                    .ReadAllAsync();
                //Use MultiApplyAndPersist to update all projections in one go
                List<P> updatedProjections = await nostify.MultiApplyAndPersistAsync<P>(projectionContainer,
                    newEvent,
                    projectionsToUpdate,
                    batchSize,
                    effectiveRetryOptions);
                // Need to init to fetch any external data for the projections that were updated
                await nostify.InitAllUninitializedAsync<P>();

                return updatedProjections.Count;
            }

            return 0;
        }
        catch (Exception e)
        {
            IEvent undeliverableEvent = newEvent
                ?? new EventFactory().NoValidate().CreateNullPayloadEvent(
                    (EventType)ErrorCommand.HandleMultiApplyEvent,
                    Guid.Empty);
            await nostify.HandleUndeliverableAsync(
                $"{nameof(HandleMultiApplyEventAsync)}:{nameof(P)}",
                e.Message,
                undeliverableEvent);
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
    /// <param name="retryOptions">Optional retry options for configuring retry behavior. When <c>null</c> and <paramref name="allowRetry"/> is <c>true</c>, uses <see cref="INostify.DefaultRetryOptions"/>.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <paramref name="retryOptions"/> when provided, otherwise <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry entirely, even when <paramref name="retryOptions"/> is provided.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async static Task<P?> HandleProjectionEventAsync<P>(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, HttpClient? httpClient, string? idToApplyToPropertyName = null, string? eventTypeFilter = null, RetryOptions? retryOptions = null, bool allowRetry = true) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        Event newEvent = triggerEvent.GetEvent(eventTypeFilter) ?? throw new NostifyException("No event found in trigger event for the specified event type filter");
        try
        {
            RetryOptions? effectiveRetryOptions = ResolveRetryOptions(nostify, retryOptions, allowRetry);
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

            if (effectiveRetryOptions != null)
            {
                var retryable = currentStateContainer.WithRetry(effectiveRetryOptions);
                projection = projectionBaseAggregateId.HasValue
                    ? await retryable.ApplyAndPersistAsync<P>(newEvent, projectionBaseAggregateId.Value,
                        onExhausted: () => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{typeof(P).Name}:Retry",
                            $"Not found after {effectiveRetryOptions.MaxRetries} retries", newEvent),
                        onNotFound: () => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{typeof(P).Name}:NotFound",
                            "Not found and RetryWhenNotFound is false", newEvent),
                        onException: (ex) => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{typeof(P).Name}",
                            ex.Message, newEvent))
                    : await retryable.ApplyAndPersistAsync<P>(newEvent,
                        onExhausted: () => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{typeof(P).Name}:Retry",
                            $"Not found after {effectiveRetryOptions.MaxRetries} retries", newEvent),
                        onNotFound: () => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{typeof(P).Name}:NotFound",
                            "Not found and RetryWhenNotFound is false", newEvent),
                        onException: (ex) => nostify.HandleUndeliverableAsync($"{nameof(HandleProjectionEventAsync)}:{typeof(P).Name}",
                            ex.Message, newEvent));
            }
            else
            {
                projection = projectionBaseAggregateId.HasValue
                    ? await currentStateContainer.ApplyAndPersistAsync<P>(newEvent, projectionBaseAggregateId.Value)
                    : await currentStateContainer.ApplyAndPersistAsync<P>(newEvent);
            }
            //Initialize projection with external data (null-check - projection may have been deleted)
            if (projection != null)
            {
                await projection.InitAsync(nostify, httpClient);
            }
            return projection;

        }
        catch (Exception e)
        {
            await nostify.HandleUndeliverableAsync(
                $"{nameof(HandleProjectionEventAsync)}:{typeof(P).Name}",
                e.Message,
                newEvent);
            throw;
        }
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleAggregateBulkCreateEventAsync<T>(INostify nostify, string[] events, bool allowRetry = true) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkCreateEventAsync<T>(nostify, events, new List<string>(), allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events without event type filtering, with retry options.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleAggregateBulkCreateEventAsync<T>(INostify nostify, string[] events, RetryOptions retryOptions) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkCreateEventAsync<T>(nostify, events, new List<string>(), retryOptions);
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleAggregateBulkCreateEventAsync<T>(INostify nostify, string[] events, string eventTypeFilter, bool allowRetry = true) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkCreateEventAsync<T>(nostify, events, new List<string>() { eventTypeFilter }, allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk creation of aggregate events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to create or update current state projections for the specified aggregate type.
    /// Supports configurable retry behavior for handling transient errors (429 TooManyRequests).
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior on transient errors. When <c>null</c>, no retry is applied.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleAggregateBulkCreateEventAsync<T>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where T : NostifyObject, IAggregate, new()
    {
        retryOptions = ResolveRetryOptions(nostify, retryOptions, retryOptions != null);
        try
        {
            // Count events that match the filter (BulkCreate is all-or-nothing on success)
            int matchingEventCount = events.Count(eventStr =>
            {
                var triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                return triggerEvent?.GetEvent(eventTypeFilter) != null;
            });

            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<T>(events, eventTypeFilter, retryOptions);

            return matchingEventCount;
        }
        catch (Exception e)
        {
            if (nostify.Logger != null)
            {
                LogBulkCreateFailure(
                    nostify.Logger,
                    nameof(HandleAggregateBulkCreateEventAsync),
                    typeof(T).Name,
                    e.Message,
                    e);
            }

            // Bulk create undeliverables
            List<Task> tasks = new List<Task>();
            events.ToList().ForEach(eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                tasks.Add(nostify.HandleUndeliverableAsync(
                    $"{nameof(HandleAggregateBulkCreateEventAsync)}:{typeof(T).Name}",
                    e.Message,
                    @event));
            });
            await Task.WhenAll(tasks);
            throw;
        }
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleProjectionBulkCreateEventAsync<P>(INostify nostify, string[] events, bool allowRetry = true) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkCreateEventAsync<P>(nostify, events, new List<string>(), allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events without event type filtering, with retry options.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleProjectionBulkCreateEventAsync<P>(INostify nostify, string[] events, RetryOptions retryOptions) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkCreateEventAsync<P>(nostify, events, new List<string>(), retryOptions);
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleProjectionBulkCreateEventAsync<P>(INostify nostify, string[] events, string eventTypeFilter, bool allowRetry = true) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkCreateEventAsync<P>(nostify, events, new List<string>() { eventTypeFilter }, allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk creation of projection events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to create or update projections for the specified projection type.
    /// Supports configurable retry behavior for handling transient errors (429 TooManyRequests).
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior on transient errors. When <c>null</c>, no retry is applied.</param>
    /// <returns>A task containing the number of successfully created records.</returns>
    public async static Task<int> HandleProjectionBulkCreateEventAsync<P>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        retryOptions = ResolveRetryOptions(nostify, retryOptions, retryOptions != null);
        try
        {
            // Count events that match the filter (BulkCreate is all-or-nothing on success)
            int matchingEventCount = events.Count(eventStr =>
            {
                var triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                return triggerEvent?.GetEvent(eventTypeFilter) != null;
            });

            Container currentStateContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<P>(events, eventTypeFilter, retryOptions);
            await nostify.InitAllUninitializedAsync<P>();

            return matchingEventCount;
        }
        catch (Exception e)
        {
            if (nostify.Logger != null)
            {
                LogBulkCreateFailure(
                    nostify.Logger,
                    nameof(HandleProjectionBulkCreateEventAsync),
                    typeof(P).Name,
                    e.Message,
                    e);
            }

            // Bulk create undeliverables
            List<Task> tasks = new List<Task>();
            events.ToList().ForEach(eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter) ?? throw new NostifyException("Event is null");
                tasks.Add(nostify.HandleUndeliverableAsync(
                    $"{nameof(HandleProjectionBulkCreateEventAsync)}:{typeof(P).Name}",
                    e.Message,
                    @event));
            });
            await Task.WhenAll(tasks);
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
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleAggregateBulkUpdateEventAsync<T>(INostify nostify, string[] events, bool allowRetry = true) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkUpdateEventAsync<T>(nostify, events, new List<string>(), allowRetry ? nostify.DefaultRetryOptions : null);
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
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleAggregateBulkUpdateEventAsync<T>(INostify nostify, string[] events, string eventTypeFilter, bool allowRetry = true) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkUpdateEventAsync<T>(nostify, events, new List<string>() { eventTypeFilter }, allowRetry ? nostify.DefaultRetryOptions : null);
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
    /// <param name="retryOptions">Options to configure retry behavior for the operation. When <c>null</c>, no retry is applied.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleAggregateBulkUpdateEventAsync<T>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where T : NostifyObject, IAggregate, new()
    {
        retryOptions = ResolveRetryOptions(nostify, retryOptions, retryOptions != null);
        try
        {
            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            ConcurrentBag<T> updatedAggregates = new ConcurrentBag<T>();
            List<Task> tasks = new List<Task>();

            if (retryOptions != null)
            {
                var retryable = currentStateContainer.WithRetry(retryOptions);
                foreach (var eventStr in events)
                {
                    NostifyKafkaTriggerEvent? triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                    if (triggerEvent is not null)
                    {
                        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
                        if (newEvent is not null)
                        {
                            tasks.Add(ApplyAggregateWithRetryAsync());

                            async Task ApplyAggregateWithRetryAsync()
                            {
                                T? result = await retryable.ApplyAndPersistAsync<T>(newEvent,
                                    onExhausted: () => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleAggregateBulkUpdateEventAsync)}:{typeof(T).Name}:Retry",
                                        $"Not found after {retryOptions.MaxRetries} retries",
                                        newEvent),
                                    onNotFound: () => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleAggregateBulkUpdateEventAsync)}:{typeof(T).Name}:NotFound",
                                        "Not found and RetryWhenNotFound is false",
                                        newEvent),
                                    onException: (ex) => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleAggregateBulkUpdateEventAsync)}:{typeof(T).Name}",
                                        ex.Message ?? "Unknown error",
                                        newEvent));

                                if (result != null)
                                {
                                    updatedAggregates.Add(result);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (var eventStr in events)
                {
                    NostifyKafkaTriggerEvent? triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                    if (triggerEvent is not null)
                    {
                        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
                        if (newEvent is not null)
                        {
                            tasks.Add(ApplyAggregateAsync());

                            async Task ApplyAggregateAsync()
                            {
                                T? result = await currentStateContainer.ApplyAndPersistAsync<T>(newEvent);
                                if (result != null)
                                {
                                    updatedAggregates.Add(result);
                                }
                            }
                        }
                    }
                }
            }

            await Task.WhenAll(tasks);
            return updatedAggregates.Count;
        }
        catch (Exception e)
        {
            // Observe every undeliverable write before propagating the original bulk failure.
            foreach (string eventStr in events)
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter)
                    ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync(
                    $"{nameof(HandleAggregateBulkUpdateEventAsync)}:{nameof(T)}",
                    e.Message,
                    @event);
            }

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
    /// <param name="allowRetry">When <c>true</c>, applies the configured default retry options; otherwise, disables retries.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleProjectionBulkUpdateEventAsync<P>(INostify nostify, string[] events, bool allowRetry = true) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkUpdateEventAsync<P>(nostify, events, new List<string>(), allowRetry ? nostify.DefaultRetryOptions : null);
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
    /// <param name="allowRetry">When <c>true</c> (default), uses the retryable container with <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleProjectionBulkUpdateEventAsync<P>(INostify nostify, string[] events, string eventTypeFilter, bool allowRetry = true) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        return await HandleProjectionBulkUpdateEventAsync<P>(nostify, events, new List<string>() { eventTypeFilter }, allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk update of projection events from Kafka trigger events with multiple event type filters.
    /// Uses ApplyAndPersistAsync to update each projection's state.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for the operation. When <c>null</c>, no retry is applied.</param>
    /// <returns>A task containing the number of successfully updated records.</returns>
    public async static Task<int> HandleProjectionBulkUpdateEventAsync<P>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        retryOptions = ResolveRetryOptions(nostify, retryOptions, retryOptions != null);
        try
        {
            Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            List<Task> tasks = new List<Task>();

            // Need to use thread safe collection here
            ConcurrentBag<P> updatedProjections = new ConcurrentBag<P>();

            if (retryOptions != null)
            {
                var retryable = projectionContainer.WithRetry(retryOptions);
                foreach (var eventStr in events)
                {
                    NostifyKafkaTriggerEvent? triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                    if (triggerEvent is not null)
                    {
                        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
                        if (newEvent is not null)
                        {
                            tasks.Add(ApplyProjectionWithRetryAsync());

                            async Task ApplyProjectionWithRetryAsync()
                            {
                                P? result = await retryable.ApplyAndPersistAsync<P>(newEvent,
                                    onExhausted: () => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleProjectionBulkUpdateEventAsync)}:{typeof(P).Name}:Retry",
                                        $"Not found after {retryOptions.MaxRetries} retries",
                                        newEvent),
                                    onNotFound: () => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleProjectionBulkUpdateEventAsync)}:{typeof(P).Name}:NotFound",
                                        "Not found and RetryWhenNotFound is false",
                                        newEvent),
                                    onException: (ex) => nostify.HandleUndeliverableAsync(
                                        $"{nameof(HandleProjectionBulkUpdateEventAsync)}:{typeof(P).Name}",
                                        ex.Message ?? "Unknown error",
                                        newEvent));

                                if (result != null)
                                {
                                    updatedProjections.Add(result);
                                }
                                else
                                {
                                    LogFailedProjectionUpdate(nostify, newEvent);
                                }
                            }
                        }
                        else
                        {
                            LogUnableToGetEvent(nostify, triggerEvent);
                        }
                    }
                    else
                    {
                        LogFailedDeserialization(nostify, eventStr);
                    }
                }
            }
            else
            {
                foreach (var eventStr in events)
                {
                    NostifyKafkaTriggerEvent? triggerEvent = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr);
                    if (triggerEvent is not null)
                    {
                        Event? newEvent = triggerEvent.GetEvent(eventTypeFilter);
                        if (newEvent is not null)
                        {
                            tasks.Add(ApplyProjectionAsync());

                            async Task ApplyProjectionAsync()
                            {
                                P? result = await projectionContainer.ApplyAndPersistAsync<P>(newEvent);
                                if (result != null)
                                {
                                    updatedProjections.Add(result);
                                }
                                else
                                {
                                    LogFailedProjectionUpdate(nostify, newEvent);
                                }
                            }
                        }
                        else
                        {
                            LogUnableToGetEvent(nostify, triggerEvent);
                        }
                    }
                    else
                    {
                        LogFailedDeserialization(nostify, eventStr);
                    }
                }
            }

            await Task.WhenAll(tasks);

            await nostify.InitAsync<P>(updatedProjections.ToList());

            return updatedProjections.Count;
        }
        catch (Exception e)
        {
            // Observe every undeliverable write before propagating the original bulk failure.
            foreach (string eventStr in events)
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter)
                    ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync(
                    $"{nameof(HandleProjectionBulkUpdateEventAsync)}:{nameof(P)}",
                    e.Message,
                    @event);
            }

            throw;
        }
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses retry with <see cref="INostify.DefaultRetryOptions"/> for bulk TTL patch operations. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleAggregateBulkDeleteEventAsync<T>(INostify nostify, string[] events, bool allowRetry = true) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkDeleteEventAsync<T>(nostify, events, new List<string>(), allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events without event type filtering, with retry options.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for bulk TTL patch operations.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleAggregateBulkDeleteEventAsync<T>(INostify nostify, string[] events, RetryOptions retryOptions) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkDeleteEventAsync<T>(nostify, events, new List<string>(), retryOptions);
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <param name="allowRetry">When <c>true</c>, applies the configured default retry options; otherwise, disables retries.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleAggregateBulkDeleteEventAsync<T>(INostify nostify, string[] events, string eventTypeFilter, bool allowRetry = true) where T : NostifyObject, IAggregate, new()
    {
        return await HandleAggregateBulkDeleteEventAsync<T>(nostify, events, new List<string>() { eventTypeFilter }, allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk deletion of aggregate events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to delete current state projections for the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements NostifyObject and IAggregate.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior on transient errors. When <c>null</c>, no retry is applied.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleAggregateBulkDeleteEventAsync<T>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where T : NostifyObject, IAggregate, new()
    {
        retryOptions = ResolveRetryOptions(nostify, retryOptions, retryOptions != null);

        try
        {
            Container currentStateContainer = await nostify.GetBulkCurrentStateContainerAsync<T>();
            int deletedCount = await currentStateContainer.BulkDeleteFromEventsAsync<T>(events, eventTypeFilter, retryOptions);
            return deletedCount;
        }
        catch (Exception e)
        {
            // Observe every undeliverable write before propagating the original bulk failure.
            foreach (string eventStr in events)
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter)
                    ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync(
                    $"{nameof(HandleAggregateBulkDeleteEventAsync)}:{nameof(T)}",
                    e.Message,
                    @event);
            }

            throw;
        }
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events without event type filtering.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="allowRetry">When <c>true</c> (default), uses retry with <see cref="INostify.DefaultRetryOptions"/> for bulk TTL patch operations. Set to <c>false</c> to disable retry.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleProjectionBulkDeleteEventAsync<P>(INostify nostify, string[] events, bool allowRetry = true) where P : NostifyObject, IProjection, new()
    {
        return await HandleProjectionBulkDeleteEventAsync<P>(nostify, events, new List<string>(), allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events without event type filtering, with retry options.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="retryOptions">Options to configure retry behavior for bulk TTL patch operations.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleProjectionBulkDeleteEventAsync<P>(INostify nostify, string[] events, RetryOptions retryOptions) where P : NostifyObject, IProjection, new()
    {
        return await HandleProjectionBulkDeleteEventAsync<P>(nostify, events, new List<string>(), retryOptions);
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events with a single event type filter.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">Single event type filter to specify which event type to process.</param>
    /// <param name="allowRetry">When <c>true</c>, applies the configured default retry options; otherwise, disables retries.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleProjectionBulkDeleteEventAsync<P>(INostify nostify, string[] events, string eventTypeFilter, bool allowRetry = true) where P : NostifyObject, IProjection, new()
    {
        return await HandleProjectionBulkDeleteEventAsync<P>(nostify, events, new List<string>() { eventTypeFilter }, allowRetry ? nostify.DefaultRetryOptions : null);
    }

    /// <summary>
    /// Handles bulk deletion of projection events from Kafka trigger events with multiple event type filters.
    /// Processes events in bulk to delete projections for the specified projection type.
    /// </summary>
    /// <typeparam name="P">The projection type that implements NostifyObject and IProjection.</typeparam>
    /// <param name="nostify">The nostify instance for accessing containers and handling undeliverable events.</param>
    /// <param name="events">Array of Kafka trigger event strings to process.</param>
    /// <param name="eventTypeFilter">List of event type filters to specify which event types to process.</param>
    /// <param name="retryOptions">Optional retry options for configuring retry behavior on transient errors. When <c>null</c>, no retry is applied.</param>
    /// <returns>A task containing the number of successfully deleted records.</returns>
    public async static Task<int> HandleProjectionBulkDeleteEventAsync<P>(INostify nostify, string[] events, List<string> eventTypeFilter, RetryOptions? retryOptions = null) where P : NostifyObject, IProjection, new()
    {
        retryOptions = ResolveRetryOptions(nostify, retryOptions, retryOptions != null);

        try
        {
            Container projectionContainer = await nostify.GetBulkProjectionContainerAsync<P>();
            int deletedCount = await projectionContainer.BulkDeleteFromEventsAsync<P>(events, eventTypeFilter, retryOptions);
            return deletedCount;
        }
        catch (Exception e)
        {
            // Observe every undeliverable write before propagating the original bulk failure.
            foreach (string eventStr in events)
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent(eventTypeFilter)
                    ?? throw new NostifyException("Event is null");
                await nostify.HandleUndeliverableAsync(
                    $"{nameof(HandleProjectionBulkDeleteEventAsync)}:{nameof(P)}",
                    e.Message,
                    @event);
            }

            throw;
        }
    }

    #endregion
}
