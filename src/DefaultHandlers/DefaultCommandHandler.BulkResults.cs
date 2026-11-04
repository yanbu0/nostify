using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using nostify;

/// <summary>
/// Provides result-aware and count-based bulk command-handler APIs.
/// </summary>
public static partial class DefaultCommandHandler
{
    /// <summary>Creates events from an HTTP request and returns every persistence result.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param>
    /// <param name="eventType">The event type to create.</param>
    /// <param name="req">The request containing aggregate objects.</param>
    /// <param name="userId">The initiating user identifier.</param>
    /// <param name="partitionKey">The event partition key.</param>
    /// <param name="batchSize">The persistence batch size.</param>
    /// <param name="allowRetry">Whether to use the default retry options.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <param name="partitionKeyName">The aggregate partition-key property name.</param>
    /// <returns>One ordered persistence result for every request item.</returns>
    public static Task<IReadOnlyList<BulkPersistEventResult>> GetBulkCreateResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        GetBulkCreateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry ? nostify.DefaultRetryOptions : null, publishErrorEvents, partitionKeyName);

    /// <summary>Creates events from an HTTP request and returns every persistence result using explicit retry options.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param>
    /// <param name="eventType">The event type to create.</param>
    /// <param name="req">The request containing aggregate objects.</param>
    /// <param name="userId">The initiating user identifier.</param>
    /// <param name="partitionKey">The event partition key.</param>
    /// <param name="batchSize">The persistence batch size.</param>
    /// <param name="retryOptions">Retry options, or <see langword="null"/> to disable retry.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <param name="partitionKeyName">The aggregate partition-key property name.</param>
    /// <returns>One ordered persistence result for every request item.</returns>
    public static async Task<IReadOnlyList<BulkPersistEventResult>> GetBulkCreateResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        List<T> newObjects = JsonConvert.DeserializeObject<List<T>>(await new StreamReader(req.Body).ReadToEndAsync())
            ?? throw new NostifyException($"Failed to deserialize request body to list of objects of type {typeof(T).Name}.");
        return await GetBulkCreateResultsAsync<T>(nostify, eventType, newObjects, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName);
    }

    /// <summary>Creates events from aggregate objects and returns every persistence result.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param>
    /// <param name="eventType">The event type to create.</param>
    /// <param name="newObjects">The aggregate objects to create.</param>
    /// <param name="userId">The initiating user identifier.</param>
    /// <param name="partitionKey">The event partition key.</param>
    /// <param name="batchSize">The persistence batch size.</param>
    /// <param name="allowRetry">Whether to use the default retry options.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <param name="partitionKeyName">The aggregate partition-key property name.</param>
    /// <returns>One ordered persistence result for every input object.</returns>
    public static Task<IReadOnlyList<BulkPersistEventResult>> GetBulkCreateResultsAsync<T>(INostify nostify, EventType eventType, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, bool allowRetry = true, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        GetBulkCreateResultsAsync<T>(nostify, eventType, newObjects, userId, partitionKey, batchSize, allowRetry ? nostify.DefaultRetryOptions : null, publishErrorEvents, partitionKeyName);

    /// <summary>Creates events from aggregate objects and returns every persistence result using explicit retry options.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param>
    /// <param name="eventType">The event type to create.</param>
    /// <param name="newObjects">The aggregate objects to create.</param>
    /// <param name="userId">The initiating user identifier.</param>
    /// <param name="partitionKey">The event partition key.</param>
    /// <param name="batchSize">The persistence batch size.</param>
    /// <param name="retryOptions">Retry options, or <see langword="null"/> to disable retry.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <param name="partitionKeyName">The aggregate partition-key property name.</param>
    /// <returns>One ordered persistence result for every input object.</returns>
    public static async Task<IReadOnlyList<BulkPersistEventResult>> GetBulkCreateResultsAsync<T>(INostify nostify, EventType eventType, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        List<IEvent> events = new(newObjects.Count);
        foreach (T item in newObjects)
        {
            Guid newId = Guid.NewGuid();
            item.id = newId;
            var property = item.GetType().GetProperty(partitionKeyName)
                ?? throw new NostifyException($"Property '{partitionKeyName}' was not found on type '{item.GetType().Name}'. Ensure the aggregate defines a public partition key property with that name.");
            if (!property.CanWrite)
            {
                throw new NostifyException($"Property '{partitionKeyName}' on type '{item.GetType().Name}' is read-only. Ensure the partition key property has a public setter.");
            }

            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType != typeof(Guid))
            {
                throw new NostifyException($"Property '{partitionKeyName}' on type '{item.GetType().Name}' must be of type '{typeof(Guid).Name}' to receive the partition key value.");
            }

            property.SetValue(item, partitionKey);
            events.Add(new EventFactory().Create<T>(eventType, newId, item, userId, partitionKey));
        }

        return await nostify.BulkPersistEventAsync(events, batchSize, retryOptions, publishErrorEvents);
    }

    /// <summary>Returns the number of persistence results produced for create request items.</summary>
    /// <inheritdoc cref="GetBulkCreateResultsAsync{T}(INostify, EventType, HttpRequestData, Guid, Guid, int, bool, bool, string)"/>
    public static async Task<int> CountBulkCreateResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        (await GetBulkCreateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents, partitionKeyName)).Count;

    /// <summary>Returns the number of persistence results produced for create request items.</summary>
    /// <inheritdoc cref="GetBulkCreateResultsAsync{T}(INostify, EventType, HttpRequestData, Guid, Guid, int, RetryOptions, bool, string)"/>
    public static async Task<int> CountBulkCreateResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        (await GetBulkCreateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName)).Count;

    /// <summary>Returns the number of persistence results produced for create input objects.</summary>
    /// <inheritdoc cref="GetBulkCreateResultsAsync{T}(INostify, EventType, List{T}, Guid, Guid, int, bool, bool, string)"/>
    public static async Task<int> CountBulkCreateResultsAsync<T>(INostify nostify, EventType eventType, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, bool allowRetry = true, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        (await GetBulkCreateResultsAsync<T>(nostify, eventType, newObjects, userId, partitionKey, batchSize, allowRetry, publishErrorEvents, partitionKeyName)).Count;

    /// <summary>Returns the number of persistence results produced for create input objects.</summary>
    /// <inheritdoc cref="GetBulkCreateResultsAsync{T}(INostify, EventType, List{T}, Guid, Guid, int, RetryOptions, bool, string)"/>
    public static async Task<int> CountBulkCreateResultsAsync<T>(INostify nostify, EventType eventType, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        (await GetBulkCreateResultsAsync<T>(nostify, eventType, newObjects, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName)).Count;

    /// <summary>Creates update events from an HTTP request and returns every persistence result.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param><param name="eventType">The update event type.</param><param name="req">The request containing update objects.</param>
    /// <param name="userId">The initiating user identifier.</param><param name="partitionKey">The event partition key.</param><param name="batchSize">The persistence batch size.</param>
    /// <param name="allowRetry">Whether to use the default retry options.</param><param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <returns>One ordered persistence result for every request item.</returns>
    public static Task<IReadOnlyList<BulkPersistEventResult>> GetBulkUpdateResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        GetBulkUpdateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry ? nostify.DefaultRetryOptions : null, publishErrorEvents);

    /// <summary>Creates update events from an HTTP request and returns every persistence result using explicit retry options.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param><param name="eventType">The update event type.</param><param name="req">The request containing update objects.</param>
    /// <param name="userId">The initiating user identifier.</param><param name="partitionKey">The event partition key.</param><param name="batchSize">The persistence batch size.</param>
    /// <param name="retryOptions">Retry options, or <see langword="null"/> to disable retry.</param><param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <returns>One ordered persistence result for every request item.</returns>
    public static async Task<IReadOnlyList<BulkPersistEventResult>> GetBulkUpdateResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
    {
        List<dynamic> updateObjects = JsonConvert.DeserializeObject<List<dynamic>>(await new StreamReader(req.Body).ReadToEndAsync())
            ?? throw new NostifyException("Failed to deserialize request body to list of objects.");
        List<IEvent> events = new(updateObjects.Count);
        foreach (dynamic item in updateObjects)
        {
            string? id = item.id?.ToString();
            if (!Guid.TryParse(id, out Guid aggregateRootId))
            {
                throw new ArgumentException("Each object must have a valid 'id' property");
            }

            events.Add(new EventFactory().Create<T>(eventType, aggregateRootId, item, userId, partitionKey));
        }

        return await nostify.BulkPersistEventAsync(events, batchSize, retryOptions, publishErrorEvents);
    }

    /// <summary>Returns the number of persistence results produced for update request items.</summary>
    /// <inheritdoc cref="GetBulkUpdateResultsAsync{T}(INostify, EventType, HttpRequestData, Guid, Guid, int, bool, bool)"/>
    public static async Task<int> CountBulkUpdateResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        (await GetBulkUpdateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents)).Count;

    /// <summary>Returns the number of persistence results produced for update request items.</summary>
    /// <inheritdoc cref="GetBulkUpdateResultsAsync{T}(INostify, EventType, HttpRequestData, Guid, Guid, int, RetryOptions, bool)"/>
    public static async Task<int> CountBulkUpdateResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate =>
        (await GetBulkUpdateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents)).Count;

    /// <summary>Creates delete events from an HTTP request and returns every persistence result.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param><param name="eventType">The delete event type.</param><param name="req">The request containing aggregate identifiers.</param>
    /// <param name="userId">The initiating user identifier.</param><param name="partitionKey">The event partition key.</param><param name="batchSize">The persistence batch size.</param>
    /// <param name="allowRetry">Whether to use the default retry options.</param><param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <returns>One ordered persistence result for every request item.</returns>
    public static Task<IReadOnlyList<BulkPersistEventResult>> GetBulkDeleteResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        GetBulkDeleteResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry ? nostify.DefaultRetryOptions : null, publishErrorEvents);

    /// <summary>Creates delete events from an HTTP request and returns every persistence result using explicit retry options.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param><param name="eventType">The delete event type.</param><param name="req">The request containing aggregate identifiers.</param>
    /// <param name="userId">The initiating user identifier.</param><param name="partitionKey">The event partition key.</param><param name="batchSize">The persistence batch size.</param>
    /// <param name="retryOptions">Retry options, or <see langword="null"/> to disable retry.</param><param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <returns>One ordered persistence result for every request item.</returns>
    public static async Task<IReadOnlyList<BulkPersistEventResult>> GetBulkDeleteResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
    {
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            throw new NostifyException("Request body cannot be null or empty.");
        }

        List<string>? idStrings;
        try
        {
            idStrings = JsonConvert.DeserializeObject<List<string>>(requestBody);
        }
        catch (JsonException exception)
        {
            throw new NostifyException($"Failed to deserialize request body to list of IDs. {exception.Message}");
        }

        if (idStrings == null)
        {
            throw new NostifyException("Failed to deserialize request body to list of IDs.");
        }

        List<Guid> ids = new(idStrings.Count);
        foreach (string idString in idStrings)
        {
            if (!Guid.TryParse(idString, out Guid id))
            {
                throw new ArgumentException($"Invalid id: {idString}");
            }

            ids.Add(id);
        }

        return await GetBulkDeleteResultsAsync<T>(nostify, eventType, ids, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);
    }

    /// <summary>Creates delete events from aggregate identifiers and returns every persistence result.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param><param name="eventType">The delete event type.</param><param name="aggregateRootIds">The aggregate identifiers.</param>
    /// <param name="userId">The initiating user identifier.</param><param name="partitionKey">The event partition key.</param><param name="batchSize">The persistence batch size.</param>
    /// <param name="allowRetry">Whether to use the default retry options.</param><param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <returns>One ordered persistence result for every input identifier.</returns>
    public static Task<IReadOnlyList<BulkPersistEventResult>> GetBulkDeleteResultsAsync<T>(INostify nostify, EventType eventType, List<Guid> aggregateRootIds, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        GetBulkDeleteResultsAsync<T>(nostify, eventType, aggregateRootIds, userId, partitionKey, batchSize, allowRetry ? nostify.DefaultRetryOptions : null, publishErrorEvents);

    /// <summary>Creates delete events from aggregate identifiers and returns every persistence result using explicit retry options.</summary>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="nostify">The Nostify instance.</param><param name="eventType">The delete event type.</param><param name="aggregateRootIds">The aggregate identifiers.</param>
    /// <param name="userId">The initiating user identifier.</param><param name="partitionKey">The event partition key.</param><param name="batchSize">The persistence batch size.</param>
    /// <param name="retryOptions">Retry options, or <see langword="null"/> to disable retry.</param><param name="publishErrorEvents">Whether to publish error events for failed writes.</param>
    /// <returns>One ordered persistence result for every input identifier.</returns>
    public static async Task<IReadOnlyList<BulkPersistEventResult>> GetBulkDeleteResultsAsync<T>(INostify nostify, EventType eventType, List<Guid> aggregateRootIds, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
    {
        List<IEvent> events = new(aggregateRootIds.Count);
        foreach (Guid aggregateRootId in aggregateRootIds)
        {
            events.Add(new EventFactory().CreateNullPayloadEvent(eventType, aggregateRootId, userId, partitionKey));
        }

        return await nostify.BulkPersistEventAsync(events, batchSize, retryOptions, publishErrorEvents);
    }

    /// <summary>Returns the number of persistence results produced for delete request items.</summary>
    /// <inheritdoc cref="GetBulkDeleteResultsAsync{T}(INostify, EventType, HttpRequestData, Guid, Guid, int, bool, bool)"/>
    public static async Task<int> CountBulkDeleteResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        (await GetBulkDeleteResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents)).Count;

    /// <summary>Returns the number of persistence results produced for delete request items.</summary>
    /// <inheritdoc cref="GetBulkDeleteResultsAsync{T}(INostify, EventType, HttpRequestData, Guid, Guid, int, RetryOptions, bool)"/>
    public static async Task<int> CountBulkDeleteResultsAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate =>
        (await GetBulkDeleteResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents)).Count;

    /// <summary>Returns the number of persistence results produced for delete input identifiers.</summary>
    /// <inheritdoc cref="GetBulkDeleteResultsAsync{T}(INostify, EventType, List{Guid}, Guid, Guid, int, bool, bool)"/>
    public static async Task<int> CountBulkDeleteResultsAsync<T>(INostify nostify, EventType eventType, List<Guid> aggregateRootIds, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        (await GetBulkDeleteResultsAsync<T>(nostify, eventType, aggregateRootIds, userId, partitionKey, batchSize, allowRetry, publishErrorEvents)).Count;

    /// <summary>Returns the number of persistence results produced for delete input identifiers.</summary>
    /// <inheritdoc cref="GetBulkDeleteResultsAsync{T}(INostify, EventType, List{Guid}, Guid, Guid, int, RetryOptions, bool)"/>
    public static async Task<int> CountBulkDeleteResultsAsync<T>(INostify nostify, EventType eventType, List<Guid> aggregateRootIds, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate =>
        (await GetBulkDeleteResultsAsync<T>(nostify, eventType, aggregateRootIds, userId, partitionKey, batchSize, retryOptions, publishErrorEvents)).Count;
}
