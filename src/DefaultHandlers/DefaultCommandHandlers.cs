
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
public static class DefaultCommandHandler
{
    /// <summary>
    /// Handles PATCH operations for aggregate roots by creating and persisting events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute</param>
    /// <param name="req">The HTTP request containing the patch data</param>
    /// <param name="context">The Azure Functions execution context</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <returns>The GUID of the aggregate root that was patched</returns>
    /// <exception cref="ArgumentException">Thrown when the provided ID is invalid</exception>
    public async static Task<Guid> HandlePatchAsync<T>(INostify nostify, NostifyCommand command, HttpRequestData req, FunctionContext context, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
    {
        // Read the patch object from the request body
        dynamic patchObj = await req.Body.ReadFromRequestBodyAsync();
        // Try to get the aggregate root ID from the binding data or the patch object
        context.BindingContext.BindingData.TryGetValue("id", out string idStr);
        string unparsedGuid = idStr ?? patchObj.id.ToString();
        if (!Guid.TryParse(unparsedGuid, out Guid aggRootId))
        {
            throw new ArgumentException($"Invalid id: {unparsedGuid}");
        }
        
        return await HandlePatchAsync<T>(nostify, command, (object)patchObj, aggRootId, userId, partitionKey);
    }

    /// <summary>
    /// Handles PATCH operations for aggregate roots by creating and persisting events from provided data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute</param>
    /// <param name="patchObj">The patch data object</param>
    /// <param name="aggregateRootId">The GUID of the aggregate root to patch</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <returns>The GUID of the aggregate root that was patched</returns>
    public async static Task<Guid> HandlePatchAsync<T>(INostify nostify, NostifyCommand command, object patchObj, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
    {
        // Create and persist the event using the EventFactory, with validation enabled
        IEvent pe = new EventFactory().Create<T>(command, aggregateRootId, patchObj, userId, partitionKey);
        await nostify.PersistEventAsync(pe);

        return aggregateRootId;
    }

    /// <summary>
    /// Handles POST operations for aggregate roots by creating and persisting events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute</param>
    /// <param name="req">The HTTP request containing the post data</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <param name="partitionKeyName">The name of the partition key property</param>
    /// <returns>The GUID of the aggregate root that was created</returns>
    public async static Task<Guid> HandlePostAsync<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId = default, Guid partitionKey = default, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        // Read the post object from the request body
        object postObj = await req.Body.ReadFromRequestBodyAsync(true);
        
        return await HandlePostAsync<T>(nostify, command, postObj, userId, partitionKey, partitionKeyName);
    }

    /// <summary>
    /// Handles POST operations for aggregate roots by creating and persisting events from provided data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute</param>
    /// <param name="postObj">The post data object</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <param name="partitionKeyName"></param>
    /// <returns>The GUID of the aggregate root that was created</returns>
    public async static Task<Guid> HandlePostAsync<T>(INostify nostify, NostifyCommand command, object postObj, Guid userId = default, Guid partitionKey = default, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        dynamic dynamicPostObj = postObj as dynamic;
        Guid aggRootId = Guid.NewGuid();
        dynamicPostObj.id = aggRootId; // Ensure the post object has an ID property set to the new GUID

        // Also ensure the partitionKey is set if it's not provided
        dynamicPostObj[partitionKeyName] = partitionKey;
        
        // Create and persist the event using the EventFactory, with validation enabled
        IEvent pe = new EventFactory().Create<T>(command, aggRootId, dynamicPostObj, userId, partitionKey);
        await nostify.PersistEventAsync(pe);

        return aggRootId;
    }

    /// <summary>
    /// Handles DELETE operations for aggregate roots by creating and persisting delete events.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute</param>
    /// <param name="context">The Azure Functions execution context</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <returns>The GUID of the aggregate root that was deleted</returns>
    /// <exception cref="ArgumentException">Thrown when the provided ID is invalid or missing</exception>
    public async static Task<Guid> HandleDeleteAsync<T>(INostify nostify, NostifyCommand command, FunctionContext context, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
    {
        // Try to get the aggregate root ID from the binding data
        if (!context.BindingContext.BindingData.TryGetValue("id", out string idStr))
        {
            throw new ArgumentException("No id provided in route");
        }
        
        if (!Guid.TryParse(idStr, out Guid aggRootId))
        {
            throw new ArgumentException($"Invalid id: {idStr}");
        }

        return await HandleDeleteAsync<T>(nostify, command, aggRootId, userId, partitionKey);
    }

    /// <summary>
    /// Handles DELETE operations for aggregate roots by creating and persisting delete events.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute</param>
    /// <param name="aggregateRootId">The GUID of the aggregate root to delete</param>
    /// <param name="userId">Optional user identifier for the operation</param>
    /// <param name="partitionKey">Optional tenant identifier for the operation</param>
    /// <returns>The GUID of the aggregate root that was deleted</returns>
    public async static Task<Guid> HandleDeleteAsync<T>(INostify nostify, NostifyCommand command, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
    {

        // Create and persist the event using the EventFactory
        IEvent pe = new EventFactory().CreateNullPayloadEvent(command, aggregateRootId, userId, partitionKey);
        await nostify.PersistEventAsync(pe);

        return aggregateRootId;
    }

    /// <summary>
    /// Handles bulk POST operations for aggregate roots by creating and persisting multiple events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of objects to create</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">Whether to allow retries on failed operations (default: false)</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <param name="partitionKeyName">The property name to use for the partition key in the dynamic object (default: "tenantId")</param>
    /// <returns>The count of aggregate roots that were created</returns>
    public async static Task<int> HandleBulkCreateAsync<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        RetryOptions? retryOptions = allowRetry
            ? new RetryOptions()
            : null;
        return await HandleBulkCreateAsync<T>(nostify, command, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName);
    }

    /// <summary>
    /// Handles bulk POST operations for aggregate roots by creating and persisting multiple events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of objects to create</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional. Retry options for configuring per-item retry behavior. When provided, each event is persisted using RetryableContainer with retry logic.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <param name="partitionKeyName">The property name to use for the partition key in the dynamic object (default: "tenantId")</param>
    /// <returns>The count of aggregate roots that were created</returns>
    public async static Task<int> HandleBulkCreateAsync<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        List<T> newObjects = JsonConvert.DeserializeObject<List<T>>(await new StreamReader(req.Body).ReadToEndAsync()) 
            ?? throw new NostifyException($"Failed to deserialize request body to list of objects of type {typeof(T).Name}.");
        return await HandleBulkCreateAsync<T>(nostify, command, newObjects, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName);
    }

    /// <summary>
    /// Handles bulk POST operations for aggregate roots by creating and persisting multiple events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="newObjects">The list of objects to create</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional. Retry options for configuring per-item retry behavior. When provided, each event is persisted using RetryableContainer with retry logic.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <param name="partitionKeyName">The property name to use for the partition key in the dynamic object (default: "tenantId")</param>
    /// <returns>The count of aggregate roots that were created</returns>
    public async static Task<int> HandleBulkCreateAsync<T>(INostify nostify, NostifyCommand command, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, bool allowRetry = false, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        return await HandleBulkCreateAsync<T>(nostify, 
                        command, 
                        newObjects, 
                        userId, 
                        partitionKey, 
                        batchSize, 
                        allowRetry ? new RetryOptions() : null, 
                        publishErrorEvents, 
                        partitionKeyName);
    }

    /// <summary>
    /// Handles bulk POST operations for aggregate roots by creating and persisting multiple events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="newObjects">The list of objects to create</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional. Retry options for configuring per-item retry behavior. When provided, each event is persisted using RetryableContainer with retry logic.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <param name="partitionKeyName">The property name to use for the partition key in the dynamic object (default: "tenantId")</param>
    /// <returns>The count of aggregate roots that were created</returns>
    public async static Task<int> HandleBulkCreateAsync<T>(INostify nostify, NostifyCommand command, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
    {
        List<IEvent> peList = new List<IEvent>();

        newObjects.ForEach(e =>
        {
            Guid newId = Guid.NewGuid();
            e.id = newId;

            var propertyInfo = e.GetType().GetProperty(partitionKeyName);
            if (propertyInfo == null)
            {
                throw new NostifyException($"Property '{partitionKeyName}' was not found on type '{e.GetType().Name}'. Ensure the aggregate defines a public partition key property with that name.");
            }

            if (!propertyInfo.CanWrite)
            {
                throw new NostifyException($"Property '{partitionKeyName}' on type '{e.GetType().Name}' is read-only. Ensure the partition key property has a public setter.");
            }

            var propertyType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;
            if (propertyType != typeof(Guid))
            {
                throw new NostifyException($"Property '{partitionKeyName}' on type '{e.GetType().Name}' must be of type '{typeof(Guid).Name}' or '{typeof(Guid?).Name}' to receive the partition key value.");
            }

            propertyInfo.SetValue(e, partitionKey);
            IEvent pe = new EventFactory().Create<T>(command, newId, e, userId, partitionKey);
            peList.Add(pe);
        });

        await nostify.BulkPersistEventAsync(peList, batchSize, retryOptions, publishErrorEvents);

        return newObjects.Count;
    }

    /// <summary>
    /// Handles bulk PATCH operations for aggregate roots by creating and persisting multiple update events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of objects to update</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">Whether to allow retries on failed operations (default: false)</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were updated</returns>
    public async static Task<int> HandleBulkUpdateAsync<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false) where T : class, IAggregate
    {
        RetryOptions? retryOptions = allowRetry
            ? new RetryOptions()
            : null;
        return await HandleBulkUpdateAsync<T>(nostify, command, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);
    }

    /// <summary>
    /// Handles bulk PATCH operations for aggregate roots by creating and persisting multiple update events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of objects to update</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional. Retry options for configuring per-item retry behavior. When provided, each event is persisted using RetryableContainer with retry logic.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were updated</returns>
    public async static Task<int> HandleBulkUpdateAsync<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
    {
        List<dynamic> updateObjects = JsonConvert.DeserializeObject<List<dynamic>>(await new StreamReader(req.Body).ReadToEndAsync()) ?? new List<dynamic>();
        List<IEvent> peList = new List<IEvent>();

        updateObjects.ForEach(e =>
        {
            if (e.id == null || !Guid.TryParse(e.id.ToString(), out Guid aggRootId))
            {
                throw new ArgumentException($"Each object must have a valid 'id' property");
            }
            
            IEvent pe = new EventFactory().Create<T>(command, Guid.Parse(e.id.ToString()), e, userId, partitionKey);
            peList.Add(pe);
        });

        await nostify.BulkPersistEventAsync(peList, batchSize, retryOptions, publishErrorEvents);

        return updateObjects.Count;
    }

    /// <summary>
    /// Handles bulk DELETE operations for aggregate roots by creating and persisting multiple delete events from request data.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of IDs to delete</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">Whether to allow retries on failed operations (default: false)</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were deleted</returns>
    public async static Task<int> HandleBulkDeleteAsync<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false) where T : class, IAggregate
    {
        RetryOptions? retryOptions = allowRetry
            ? new RetryOptions()
            : null;
        return await HandleBulkDeleteAsync<T>(nostify, command, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);
    }

    /// <summary>
    /// Handles bulk DELETE operations for aggregate roots by creating and persisting multiple delete events from request data.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="req">The HTTP request containing an array of IDs to delete</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional. Retry options for configuring per-item retry behavior. When provided, each event is persisted using RetryableContainer with retry logic.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were deleted</returns>
    public async static Task<int> HandleBulkDeleteAsync<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
    {
        List<string> idStrings = JsonConvert.DeserializeObject<List<string>>(await new StreamReader(req.Body).ReadToEndAsync()) ?? new List<string>();
        List<IEvent> peList = new List<IEvent>();

        idStrings.ForEach(idStr =>
        {
            if (!Guid.TryParse(idStr, out Guid aggRootId))
            {
                throw new ArgumentException($"Invalid id: {idStr}");
            }
            
            IEvent pe = new EventFactory().CreateNullPayloadEvent(command, aggRootId, userId, partitionKey);
            peList.Add(pe);
        });

        await nostify.BulkPersistEventAsync(peList, batchSize, retryOptions, publishErrorEvents);

        return idStrings.Count;
    }

    /// <summary>
    /// Handles bulk DELETE operations for aggregate roots by creating and persisting multiple delete events from a list of GUIDs.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="aggregateRootIds">List of aggregate root IDs to delete</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="allowRetry">Whether to allow retries on failed operations (default: false)</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were deleted</returns>
    public async static Task<int> HandleBulkDeleteAsync<T>(INostify nostify, NostifyCommand command, List<Guid> aggregateRootIds, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false) where T : class, IAggregate
    {
        RetryOptions? retryOptions = allowRetry
            ? new RetryOptions()
            : null;
        return await HandleBulkDeleteAsync<T>(nostify, command, aggregateRootIds, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);
    }

    /// <summary>
    /// Handles bulk DELETE operations for aggregate roots by creating and persisting multiple delete events from a list of GUIDs.
    /// Uses configurable RetryOptions for per-item retry behavior.
    /// </summary>
    /// <typeparam name="T">The aggregate type that implements IAggregate</typeparam>
    /// <param name="nostify">The Nostify instance for event persistence</param>
    /// <param name="command">The command to execute for each item</param>
    /// <param name="aggregateRootIds">List of aggregate root IDs to delete</param>
    /// <param name="userId">Optional user identifier for the operations</param>
    /// <param name="partitionKey">Optional tenant identifier for the operations</param>
    /// <param name="batchSize">The size of batches for bulk operations (default: 100)</param>
    /// <param name="retryOptions">Optional. Retry options for configuring per-item retry behavior. When provided, each event is persisted using RetryableContainer with retry logic.</param>
    /// <param name="publishErrorEvents">Whether to publish error events for failed operations (default: false)</param>
    /// <returns>The count of aggregate roots that were deleted</returns>
    public async static Task<int> HandleBulkDeleteAsync<T>(INostify nostify, NostifyCommand command, List<Guid> aggregateRootIds, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
    {
        List<IEvent> peList = new List<IEvent>();

        aggregateRootIds.ForEach(aggRootId =>
        {
            IEvent pe = new EventFactory().CreateNullPayloadEvent(command, aggRootId, userId, partitionKey);
            peList.Add(pe);
        });

        await nostify.BulkPersistEventAsync(peList, batchSize, retryOptions, publishErrorEvents);

        return aggregateRootIds.Count;
    }

    // ---------------------------------------------------------------------------
    // Backward-compatible wrappers (obsolete — use the *Async equivalents)
    // ---------------------------------------------------------------------------

    /// <inheritdoc cref="HandlePatchAsync{T}(INostify, NostifyCommand, HttpRequestData, FunctionContext, Guid, Guid)"/>
    [Obsolete("Use HandlePatchAsync instead.")]
    public static Task<Guid> HandlePatch<T>(INostify nostify, NostifyCommand command, HttpRequestData req, FunctionContext context, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
        => HandlePatchAsync<T>(nostify, command, req, context, userId, partitionKey);

    /// <inheritdoc cref="HandlePatchAsync{T}(INostify, NostifyCommand, object, Guid, Guid, Guid)"/>
    [Obsolete("Use HandlePatchAsync instead.")]
    public static Task<Guid> HandlePatch<T>(INostify nostify, NostifyCommand command, object patchObj, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
        => HandlePatchAsync<T>(nostify, command, patchObj, aggregateRootId, userId, partitionKey);

    /// <inheritdoc cref="HandlePostAsync{T}(INostify, NostifyCommand, HttpRequestData, Guid, Guid, string)"/>
    [Obsolete("Use HandlePostAsync instead.")]
    public static Task<Guid> HandlePost<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId = default, Guid partitionKey = default, string partitionKeyName = "tenantId") where T : class, IAggregate
        => HandlePostAsync<T>(nostify, command, req, userId, partitionKey, partitionKeyName);

    /// <inheritdoc cref="HandlePostAsync{T}(INostify, NostifyCommand, object, Guid, Guid, string)"/>
    [Obsolete("Use HandlePostAsync instead.")]
    public static Task<Guid> HandlePost<T>(INostify nostify, NostifyCommand command, object postObj, Guid userId = default, Guid partitionKey = default, string partitionKeyName = "tenantId") where T : class, IAggregate
        => HandlePostAsync<T>(nostify, command, postObj, userId, partitionKey, partitionKeyName);

    /// <inheritdoc cref="HandleDeleteAsync{T}(INostify, NostifyCommand, FunctionContext, Guid, Guid)"/>
    [Obsolete("Use HandleDeleteAsync instead.")]
    public static Task<Guid> HandleDelete<T>(INostify nostify, NostifyCommand command, FunctionContext context, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
        => HandleDeleteAsync<T>(nostify, command, context, userId, partitionKey);

    /// <inheritdoc cref="HandleDeleteAsync{T}(INostify, NostifyCommand, Guid, Guid, Guid)"/>
    [Obsolete("Use HandleDeleteAsync instead.")]
    public static Task<Guid> HandleDelete<T>(INostify nostify, NostifyCommand command, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default) where T : class, IAggregate
        => HandleDeleteAsync<T>(nostify, command, aggregateRootId, userId, partitionKey);

    /// <inheritdoc cref="HandleBulkCreateAsync{T}(INostify, NostifyCommand, HttpRequestData, Guid, Guid, int, bool, bool, string)"/>
    [Obsolete("Use HandleBulkCreateAsync instead.")]
    public static Task<int> HandleBulkCreate<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
        => HandleBulkCreateAsync<T>(nostify, command, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents, partitionKeyName);

    /// <inheritdoc cref="HandleBulkCreateAsync{T}(INostify, NostifyCommand, HttpRequestData, Guid, Guid, int, RetryOptions?, bool, string)"/>
    [Obsolete("Use HandleBulkCreateAsync instead.")]
    public static Task<int> HandleBulkCreate<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
        => HandleBulkCreateAsync<T>(nostify, command, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName);

    /// <inheritdoc cref="HandleBulkCreateAsync{T}(INostify, NostifyCommand, List{T}, Guid, Guid, int, bool, bool, string)"/>
    [Obsolete("Use HandleBulkCreateAsync instead.")]
    public static Task<int> HandleBulkCreate<T>(INostify nostify, NostifyCommand command, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, bool allowRetry = false, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
        => HandleBulkCreateAsync<T>(nostify, command, newObjects, userId, partitionKey, batchSize, allowRetry, publishErrorEvents, partitionKeyName);

    /// <inheritdoc cref="HandleBulkCreateAsync{T}(INostify, NostifyCommand, List{T}, Guid, Guid, int, RetryOptions?, bool, string)"/>
    [Obsolete("Use HandleBulkCreateAsync instead.")]
    public static Task<int> HandleBulkCreate<T>(INostify nostify, NostifyCommand command, List<T> newObjects, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false, string partitionKeyName = "tenantId") where T : class, IAggregate
        => HandleBulkCreateAsync<T>(nostify, command, newObjects, userId, partitionKey, batchSize, retryOptions, publishErrorEvents, partitionKeyName);

    /// <inheritdoc cref="HandleBulkUpdateAsync{T}(INostify, NostifyCommand, HttpRequestData, Guid, Guid, int, bool, bool)"/>
    [Obsolete("Use HandleBulkUpdateAsync instead.")]
    public static Task<int> HandleBulkUpdate<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false) where T : class, IAggregate
        => HandleBulkUpdateAsync<T>(nostify, command, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents);

    /// <inheritdoc cref="HandleBulkUpdateAsync{T}(INostify, NostifyCommand, HttpRequestData, Guid, Guid, int, RetryOptions?, bool)"/>
    [Obsolete("Use HandleBulkUpdateAsync instead.")]
    public static Task<int> HandleBulkUpdate<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
        => HandleBulkUpdateAsync<T>(nostify, command, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);

    /// <inheritdoc cref="HandleBulkDeleteAsync{T}(INostify, NostifyCommand, HttpRequestData, Guid, Guid, int, bool, bool)"/>
    [Obsolete("Use HandleBulkDeleteAsync instead.")]
    public static Task<int> HandleBulkDelete<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false) where T : class, IAggregate
        => HandleBulkDeleteAsync<T>(nostify, command, req, userId, partitionKey, batchSize, allowRetry, publishErrorEvents);

    /// <inheritdoc cref="HandleBulkDeleteAsync{T}(INostify, NostifyCommand, HttpRequestData, Guid, Guid, int, RetryOptions?, bool)"/>
    [Obsolete("Use HandleBulkDeleteAsync instead.")]
    public static Task<int> HandleBulkDelete<T>(INostify nostify, NostifyCommand command, HttpRequestData req, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
        => HandleBulkDeleteAsync<T>(nostify, command, req, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);

    /// <inheritdoc cref="HandleBulkDeleteAsync{T}(INostify, NostifyCommand, List{Guid}, Guid, Guid, int, bool, bool)"/>
    [Obsolete("Use HandleBulkDeleteAsync instead.")]
    public static Task<int> HandleBulkDelete<T>(INostify nostify, NostifyCommand command, List<Guid> aggregateRootIds, Guid userId = default, Guid partitionKey = default, int batchSize = 100, bool allowRetry = false, bool publishErrorEvents = false) where T : class, IAggregate
        => HandleBulkDeleteAsync<T>(nostify, command, aggregateRootIds, userId, partitionKey, batchSize, allowRetry, publishErrorEvents);

    /// <inheritdoc cref="HandleBulkDeleteAsync{T}(INostify, NostifyCommand, List{Guid}, Guid, Guid, int, RetryOptions?, bool)"/>
    [Obsolete("Use HandleBulkDeleteAsync instead.")]
    public static Task<int> HandleBulkDelete<T>(INostify nostify, NostifyCommand command, List<Guid> aggregateRootIds, Guid userId, Guid partitionKey, int batchSize, RetryOptions? retryOptions, bool publishErrorEvents = false) where T : class, IAggregate
        => HandleBulkDeleteAsync<T>(nostify, command, aggregateRootIds, userId, partitionKey, batchSize, retryOptions, publishErrorEvents);
}