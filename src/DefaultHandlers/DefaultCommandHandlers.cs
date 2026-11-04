
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using nostify;

/// <summary>
/// Provides extension methods for handling common CRUD operations in Azure Functions with event sourcing.
/// </summary>
public static partial class DefaultCommandHandler
{
    /// <summary>
    /// Handles PATCH operations for aggregate roots by creating and persisting events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The event type to execute</param>
    /// <param name="req">The HTTP request containing the patch data</param>
    /// <param name="context">The Azure Functions execution context</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <returns>The GUID of the aggregate root that was patched</returns>
    /// <exception cref="ArgumentException">Thrown when the provided ID is invalid</exception>
    public async static Task<Guid> HandlePatchAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, FunctionContext context, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
    {
        // Read the patch object from the request body
        dynamic patchObj = await req.Body.ReadFromRequestBodyAsync();
        // Try to get the aggregate root ID from the binding data or the patch object
        context.BindingContext.BindingData.TryGetValue("id", out string? idStr);
        string unparsedGuid = idStr ?? patchObj.id.ToString();
        if (!Guid.TryParse(unparsedGuid, out Guid aggRootId))
        {
            throw new ArgumentException($"Invalid id: {unparsedGuid}");
        }

        return await HandlePatchAsync<T>(nostify, eventType, (object)patchObj, aggRootId, userId, partitionKey);
    }

    /// <summary>
    /// Handles PATCH operations for aggregate roots by creating and persisting events from provided data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The event type to execute</param>
    /// <param name="patchObj">The patch data object</param>
    /// <param name="aggregateRootId">The GUID of the aggregate root to patch</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <returns>The GUID of the aggregate root that was patched</returns>
    public async static Task<Guid> HandlePatchAsync<T>(INostify nostify, EventType eventType, object patchObj, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
    {
        // Create and persist the event using the EventFactory, with validation enabled
        IEvent pe = new EventFactory().Create<T>(eventType, aggregateRootId, patchObj, userId, partitionKey);
        await nostify.PersistEventAsync(pe);

        return aggregateRootId;
    }

    /// <summary>
    /// Handles POST operations for aggregate roots by creating and persisting events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The event type to execute</param>
    /// <param name="req">The HTTP request containing the post data</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <param name="partitionKeyName">The name of the partition key property</param>
    /// <returns>The GUID of the aggregate root that was created</returns>
    public async static Task<Guid> HandlePostAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        // Read the post object from the request body
        object postObj = await req.Body.ReadFromRequestBodyAsync(true);

        return await HandlePostAsync<T>(nostify, eventType, postObj, userId, partitionKey, partitionKeyName);
    }

    /// <summary>
    /// Handles POST operations for aggregate roots by creating and persisting events from provided data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The event type to execute</param>
    /// <param name="postObj">The post data object</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <param name="partitionKeyName">The name of the partition key property in the dynamic object. Defaults to <c>"tenantId"</c>.</param>
    /// <returns>The GUID of the aggregate root that was created</returns>
    public async static Task<Guid> HandlePostAsync<T>(INostify nostify, EventType eventType, object postObj, Guid userId = default, Guid partitionKey = default, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        dynamic dynamicPostObj = postObj as dynamic;
        Guid aggRootId = Guid.NewGuid();
        dynamicPostObj.id = aggRootId; // Ensure the post object has an ID property set to the new GUID

        // Also ensure the partitionKey is set if it's not provided
        dynamicPostObj[partitionKeyName] = partitionKey;

        // Create and persist the event using the EventFactory, with validation enabled
        IEvent pe = new EventFactory().Create<T>(eventType, aggRootId, dynamicPostObj, userId, partitionKey);
        await nostify.PersistEventAsync(pe);

        return aggRootId;
    }

    /// <summary>
    /// Handles DELETE operations for aggregate roots by creating and persisting delete events.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The event type to execute</param>
    /// <param name="context">The Azure Functions execution context</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <returns>The GUID of the aggregate root that was deleted</returns>
    /// <exception cref="ArgumentException">Thrown when the provided ID is invalid or missing</exception>
    public async static Task<Guid> HandleDeleteAsync<T>(INostify nostify, EventType eventType, FunctionContext context, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
    {
        // Try to get the aggregate root ID from the binding data
        if (!context.BindingContext.BindingData.TryGetValue("id", out string? idStr) || idStr is null)
        {
            throw new ArgumentException("No id provided in route");
        }

        if (!Guid.TryParse(idStr, out Guid aggRootId))
        {
            throw new ArgumentException($"Invalid id: {idStr}");
        }

        return await HandleDeleteAsync<T>(nostify, eventType, aggRootId, userId, partitionKey);
    }

    /// <summary>
    /// Handles DELETE operations for aggregate roots by creating and persisting delete events.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The event type to execute</param>
    /// <param name="aggregateRootId">The GUID of the aggregate root to delete</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <returns>The GUID of the aggregate root that was deleted</returns>
    public async static Task<Guid> HandleDeleteAsync<T>(INostify nostify, EventType eventType, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
    {
        // Create and persist the event using the EventFactory
        IEvent pe = new EventFactory().CreateNullPayloadEvent(eventType, aggregateRootId, userId, partitionKey);
        await nostify.PersistEventAsync(pe);

        return aggregateRootId;
    }

    /// <summary>
    /// Handles bulk POST operations for aggregate roots by creating and persisting multiple events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of objects to create</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">When <c>true</c> (default), retries throttled writes using <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <param name="partitionKeyName">The property name to use for the partition key in the dynamic object (default: "tenantId")</param>
    /// <returns>The count of aggregate roots that were created</returns>
    [Obsolete("Use CountBulkCreateResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkCreateAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        CountBulkCreateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents, partitionKeyName);

    /// <summary>
    /// Handles bulk POST operations for aggregate roots by creating and persisting multiple events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of objects to create</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional retry options for throttled event writes.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <param name="partitionKeyName">The property name to use for the partition key in the dynamic object (default: "tenantId")</param>
    /// <returns>The count of aggregate roots that were created</returns>
    [Obsolete("Use CountBulkCreateResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkCreateAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        CountBulkCreateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName);

    /// <summary>
    /// Handles bulk POST operations for aggregate roots by creating and persisting multiple events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="newObjects">The list of objects to create</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">When <c>true</c> (default), retries throttled writes using <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <param name="partitionKeyName">The property name to use for the partition key in the dynamic object (default: "tenantId")</param>
    /// <returns>The count of aggregate roots that were created</returns>
    [Obsolete("Use CountBulkCreateResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkCreateAsync<T>(INostify nostify, EventType eventType, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, bool allowRetry = true, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        CountBulkCreateResultsAsync<T>(nostify, eventType, newObjects, userId, partitionKey, batchSize, allowRetry, publishErrorEvents, partitionKeyName);

    /// <summary>
    /// Handles bulk POST operations for aggregate roots by creating and persisting multiple events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="newObjects">The list of objects to create</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional retry options for throttled event writes.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <param name="partitionKeyName">The property name to use for the partition key in the dynamic object (default: "tenantId")</param>
    /// <returns>The count of aggregate roots that were created</returns>
    [Obsolete("Use CountBulkCreateResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkCreateAsync<T>(INostify nostify, EventType eventType, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate =>
        CountBulkCreateResultsAsync<T>(nostify, eventType, newObjects, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName);

    /// <summary>
    /// Handles bulk PATCH operations for aggregate roots by creating and persisting multiple update events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of objects to update</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">When <c>true</c> (default), retries throttled writes using <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were updated</returns>
    [Obsolete("Use CountBulkUpdateResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkUpdateAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        CountBulkUpdateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents);

    /// <summary>
    /// Handles bulk PATCH operations for aggregate roots by creating and persisting multiple update events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of objects to update</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional retry options for throttled event writes.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were updated</returns>
    [Obsolete("Use CountBulkUpdateResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkUpdateAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate =>
        CountBulkUpdateResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);

    /// <summary>
    /// Handles bulk DELETE operations for aggregate roots by creating and persisting multiple delete events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of IDs to delete</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">When <c>true</c> (default), retries throttled writes using <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were deleted</returns>
    [Obsolete("Use CountBulkDeleteResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkDeleteAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        CountBulkDeleteResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents);

    /// <summary>
    /// Handles bulk DELETE operations for aggregate roots by creating and persisting multiple delete events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of IDs to delete</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional retry options for throttled event writes.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were deleted</returns>
    [Obsolete("Use CountBulkDeleteResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkDeleteAsync<T>(INostify nostify, EventType eventType, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate =>
        CountBulkDeleteResultsAsync<T>(nostify, eventType, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);

    /// <summary>
    /// Handles bulk DELETE operations for aggregate roots by creating and persisting multiple delete events from a list of GUIDs.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="aggregateRootIds">List of aggregate root IDs to delete</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">When <c>true</c> (default), retries throttled writes using <see cref="INostify.DefaultRetryOptions"/>. Set to <c>false</c> to disable retry.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were deleted</returns>
    [Obsolete("Use CountBulkDeleteResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkDeleteAsync<T>(INostify nostify, EventType eventType, List<Guid> aggregateRootIds, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = true, bool publishErrorEvents = false) where T : class, IAggregate =>
        CountBulkDeleteResultsAsync<T>(nostify, eventType, aggregateRootIds, userId, partitionKey, batchSize, allowRetry, publishErrorEvents);

    /// <summary>
    /// Handles bulk DELETE operations for aggregate roots by creating and persisting multiple delete events from a list of GUIDs.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="eventType">The eventType to execute for each item</param>
    /// <param name="aggregateRootIds">List of aggregate root IDs to delete</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional retry options for throttled event writes.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were deleted</returns>
    [Obsolete("Use CountBulkDeleteResultsAsync<T>(...) instead. This method will be removed in v6.", false)]
    public static Task<int> HandleBulkDeleteAsync<T>(INostify nostify, EventType eventType, List<Guid> aggregateRootIds, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate =>
        CountBulkDeleteResultsAsync<T>(nostify, eventType, aggregateRootIds, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);

}
