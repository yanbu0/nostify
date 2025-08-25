using System;
using System.ComponentModel.DataAnnotations;

namespace nostify;

/// <summary>
/// Provides methods to build and validate Event instances for the event store.
/// </summary>
public class EventFactory
{
    /// <summary>
    /// Gets or sets whether to validate the payload against the aggregate type.
    /// </summary>
    public bool ValidatePayload { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventFactory"/> class with validation enabled by default.
    /// </summary>
    public EventFactory()
    {
        ValidatePayload = true;
    }

    /// <summary>
    /// Sets the factory to skip payload validation.
    /// </summary>
    /// <returns>The current <see cref="EventFactory"/> instance for method chaining.</returns>
    public EventFactory NoValidate()
    {
        ValidatePayload = false;
        return this;
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance and optionally validates its payload against the specified aggregate type.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate to validate against. Must inherit from <see cref="NostifyObject"/> and implement <see cref="IAggregate"/>.</typeparam>
    /// <param name="command">The command to persist.</param>
    /// <param name="aggregateRootId">The ID of the root aggregate to perform the command on.</param>
    /// <param name="payload">The properties to update or the ID of the aggregate to delete.</param>
    /// <param name="userId">The ID of the user responsible for the event.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in.</param>
    /// <returns>A <see cref="Event"/> instance, validated if ValidatePayload property is true.</returns>
    public Event Create<T>(NostifyCommand command, Guid aggregateRootId, object payload, Guid userId = default, Guid partitionKey = default) where T : NostifyObject, IAggregate
    {
        var evt = new Event(command, aggregateRootId, payload, userId, partitionKey);
        return ValidatePayload ? evt.ValidatePayload<T>() : evt;
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance and optionally validates its payload against the specified aggregate type, parsing the aggregateRootId from the payload.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate to validate against. Must inherit from <see cref="NostifyObject"/> and implement <see cref="IAggregate"/>.</typeparam>
    /// <param name="command">The command to persist.</param>
    /// <param name="payload">The properties to update or the ID of the aggregate to delete. Must contain an "id" property.</param>
    /// <param name="userId">The ID of the user responsible for the event.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in.</param>
    /// <returns>A <see cref="Event"/> instance, validated if ValidatePayload property is true.</returns>
    public Event Create<T>(NostifyCommand command, object payload, Guid userId = default, Guid partitionKey = default) where T : NostifyObject, IAggregate
    {
        var evt = new Event(command, payload, userId, partitionKey);
        return ValidatePayload ? evt.ValidatePayload<T>() : evt;
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
    /// <returns>A <see cref="Event"/> instance, validated if ValidatePayload property is true.</returns>
    public Event Create<T>(NostifyCommand command, string aggregateRootId, object payload, string userId, string partitionKey) where T : NostifyObject, IAggregate
    {
        var evt = new Event(command, aggregateRootId, payload, userId, partitionKey);
        return ValidatePayload ? evt.ValidatePayload<T>() : evt;
    }

    /// <summary>
    /// Creates a new <see cref="Event"/> instance with a null payload and no validation.
    /// This is typically used for delete operations where no payload data is needed.
    /// </summary>
    /// <param name="command">The command to persist.</param>
    /// <param name="aggregateRootId">The ID of the root aggregate to perform the command on.</param>
    /// <param name="userId">The ID of the user responsible for the event.</param>
    /// <param name="partitionKey">The ID of the partition that the aggregate to apply the event to is in.</param>
    /// <returns>An <see cref="Event"/> instance with null payload.</returns>
    public Event CreateNullPayloadEvent(NostifyCommand command, Guid aggregateRootId, Guid userId = default, Guid partitionKey = default)
    {
        return CreateEventWithNullPayload(command, aggregateRootId, userId, partitionKey);
    }

    /// <summary>
    /// Helper method to create an event with null payload by directly setting properties to bypass validation.
    /// </summary>
    private Event CreateEventWithNullPayload(NostifyCommand command, Guid aggregateRootId, Guid userId, Guid partitionKey)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command), "Command cannot be null");
        }

        // Create event using parameterless constructor to avoid null payload validation
        var eventInstance = new Event()
        {
            id = Guid.NewGuid(),
            command = command,
            aggregateRootId = aggregateRootId,
            userId = userId,
            partitionKey = partitionKey,
            timestamp = DateTime.UtcNow,
            payload = null
        };

        return eventInstance;
    }
}