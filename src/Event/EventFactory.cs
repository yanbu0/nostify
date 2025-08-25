using System;
using System.ComponentModel.DataAnnotations;

namespace nostify;

/// <summary>
/// Provides methods to create Event instances for the event store, including specialized methods for events with null payloads.
/// </summary>
public static class EventFactory
{
    /// <summary>
    /// Creates a new <see cref="Event"/> instance and validates its payload against the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate to validate against. Must inherit from <see cref="NostifyObject"/> and implement <see cref="IAggregate"/>.</typeparam>
    /// <param name="command">The command to persist.</param>
    /// <param name="aggregateRootId">The ID of the root aggregate to perform the command on.</param>
    /// <param name="payload">The properties to update or the ID of the aggregate to delete.</param>
    /// <param name="userId">The ID of the user responsible for the event.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in.</param>
    /// <returns>A validated <see cref="Event"/> instance.</returns>
    public static Event Create<T>(NostifyCommand command, Guid aggregateRootId, object payload, Guid userId = default, Guid partitionKey = default) where T : NostifyObject, IAggregate
    {
        return new Event(command, aggregateRootId, payload, userId, partitionKey).ValidatePayload<T>();
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance and validates its payload against the specified aggregate type, parsing the aggregateRootId from the payload.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate to validate against. Must inherit from <see cref="NostifyObject"/> and implement <see cref="IAggregate"/>.</typeparam>
    /// <param name="command">The command to persist.</param>
    /// <param name="payload">The properties to update or the ID of the aggregate to delete. Must contain an "id" property.</param>
    /// <param name="userId">The ID of the user responsible for the event.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in.</param>
    /// <returns>A validated <see cref="Event"/> instance.</returns>
    public static Event Create<T>(NostifyCommand command, object payload, Guid userId = default, Guid partitionKey = default) where T : NostifyObject, IAggregate
    {
        return new Event(command, payload, userId, partitionKey).ValidatePayload<T>();
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance and validates its payload against the specified aggregate type, parsing the aggregateRootId, userId, and partitionKey from string values.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate to validate against. Must inherit from <see cref="NostifyObject"/> and implement <see cref="IAggregate"/>.</typeparam>
    /// <param name="command">The command to persist.</param>
    /// <param name="aggregateRootId">The ID of the root aggregate to perform the command on, as a string.</param>
    /// <param name="payload">The properties to update or the ID of the aggregate to delete.</param>
    /// <param name="userId">The ID of the user responsible for the event, as a string.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in, as a string.</param>
    /// <returns>A validated <see cref="Event"/> instance.</returns>
    public static Event Create<T>(NostifyCommand command, string aggregateRootId, object payload, string userId, string partitionKey) where T : NostifyObject, IAggregate
    {
        return new Event(command, aggregateRootId, payload, userId, partitionKey).ValidatePayload<T>();
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance with a null payload without validation. 
    /// This method is typically used for delete operations where no payload data is needed.
    /// </summary>
    /// <param name="command">The command to persist.</param>
    /// <param name="aggregateRootId">The ID of the root aggregate to perform the command on.</param>
    /// <param name="userId">The ID of the user responsible for the event.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in.</param>
    /// <returns>An <see cref="Event"/> instance with a null payload and no validation.</returns>
    public static Event CreateNullPayloadEvent(NostifyCommand command, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default)
    {
        var eventInstance = new Event();
        eventInstance.command = command ?? throw new ArgumentNullException(nameof(command));
        eventInstance.aggregateRootId = aggregateRootId;
        eventInstance.userId = userId;
        eventInstance.partitionKey = partitionKey;
        eventInstance.payload = null;
        eventInstance.id = Guid.NewGuid();
        eventInstance.timestamp = DateTime.UtcNow;
        return eventInstance;
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance with a null payload without validation, parsing the aggregateRootId, userId, and partitionKey from string values.
    /// This method is typically used for delete operations where no payload data is needed.
    /// </summary>
    /// <param name="command">The command to persist.</param>
    /// <param name="aggregateRootId">The ID of the root aggregate to perform the command on, as a string.</param>
    /// <param name="userId">The ID of the user responsible for the event, as a string.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in, as a string.</param>
    /// <returns>An <see cref="Event"/> instance with a null payload and no validation.</returns>
    public static Event CreateNullPayloadEvent(NostifyCommand command, string aggregateRootId, string userId = "", string partitionKey = "")
    {
        Guid aggGuid;
        if (!Guid.TryParse(aggregateRootId, out aggGuid))
        {
            throw new ArgumentException("Aggregate Root ID is not parsable to a Guid", nameof(aggregateRootId));
        }

        Guid userGuid = default;
        if (!string.IsNullOrEmpty(userId) && !Guid.TryParse(userId, out userGuid))
        {
            throw new ArgumentException("User ID is not parsable to a Guid", nameof(userId));
        }

        Guid pKey = default;
        if (!string.IsNullOrEmpty(partitionKey) && !Guid.TryParse(partitionKey, out pKey))
        {
            throw new ArgumentException("Partition Key is not parsable to a Guid", nameof(partitionKey));
        }

        return CreateNullPayloadEvent(command, aggGuid, userGuid, pKey);
    }
}