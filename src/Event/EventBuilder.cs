using System;
using System.ComponentModel.DataAnnotations;

namespace nostify;

/// <summary>
/// Provides methods to build and validate Event instances for the event store.
/// </summary>
public static class EventBuilder
{
    /// <summary>
    /// Creates a new <see cref="Event"/> instance and optionally validates its payload against the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate to validate against. Must inherit from <see cref="NostifyObject"/> and implement <see cref="IAggregate"/>.</typeparam>
    /// <param name="command">The command to persist.</param>
    /// <param name="aggregateRootId">The ID of the root aggregate to perform the command on.</param>
    /// <param name="payload">The properties to update or the ID of the aggregate to delete.</param>
    /// <param name="userId">The ID of the user responsible for the event.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in.</param>
    /// <param name="validate">Whether to validate the payload against the aggregate type. Defaults to true.</param>
    /// <returns>A <see cref="Event"/> instance, validated if validate parameter is true.</returns>
    public static IEvent Create<T>(NostifyCommand command, Guid aggregateRootId, object payload, Guid userId = default, Guid partitionKey = default, bool validate = true) where T : NostifyObject, IAggregate
    {
        var evt = new Event(command, aggregateRootId, payload, userId, partitionKey);
        return validate ? evt.ValidatePayload<T>() : evt;
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance and optionally validates its payload against the specified aggregate type, parsing the aggregateRootId from the payload.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate to validate against. Must inherit from <see cref="NostifyObject"/> and implement <see cref="IAggregate"/>.</typeparam>
    /// <param name="command">The command to persist.</param>
    /// <param name="payload">The properties to update or the ID of the aggregate to delete. Must contain an "id" property.</param>
    /// <param name="userId">The ID of the user responsible for the event.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in.</param>
    /// <param name="validate">Whether to validate the payload against the aggregate type. Defaults to true.</param>
    /// <returns>A <see cref="Event"/> instance, validated if validate parameter is true.</returns>
    public static IEvent Create<T>(NostifyCommand command, object payload, Guid userId = default, Guid partitionKey = default, bool validate = true) where T : NostifyObject, IAggregate
    {
        var evt = new Event(command, payload, userId, partitionKey);
        return validate ? evt.ValidatePayload<T>() : evt;
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance and optionally validates its payload against the specified aggregate type, parsing the aggregateRootId, userId, and partitionKey from string values.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate to validate against. Must inherit from <see cref="NostifyObject"/> and implement <see cref="IAggregate"/>.</typeparam>
    /// <param name="command">The command to persist.</param>
    /// <param name="aggregateRootId">The ID of the root aggregate to perform the command on, as a string.</param>
    /// <param name="payload">The properties to update or the ID of the aggregate to delete.</param>
    /// <param name="userId">The ID of the user responsible for the event, as a string.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in, as a string.</param>
    /// <param name="validate">Whether to validate the payload against the aggregate type. Defaults to true.</param>
    /// <returns>A <see cref="Event"/> instance, validated if validate parameter is true.</returns>
    public static IEvent Create<T>(NostifyCommand command, string aggregateRootId, object payload, string userId, string partitionKey, bool validate = true) where T : NostifyObject, IAggregate
    {
        var evt = new Event(command, aggregateRootId, payload, userId, partitionKey);
        return validate ? evt.ValidatePayload<T>() : evt;
    }
}
