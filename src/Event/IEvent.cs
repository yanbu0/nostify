using System;

namespace nostify;

/// <summary>
/// Represents a persisted Nostify event and the metadata required to apply it to an aggregate or projection.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Timestamp of event.
    /// </summary>
    DateTime timestamp { get; set; }

    /// <summary>
    /// Partition key to apply event to.
    /// </summary>
    Guid partitionKey { get; set; }

    /// <summary>
    /// Id of user.
    /// </summary>
    Guid userId { get; set; }

    /// <summary>
    /// Id of event.
    /// </summary>
    Guid id { get; set; }

    /// <summary>
    /// Event type to perform, defined in Aggregate implementation.
    /// </summary>
    EventType eventType { get; set; }

    /// <summary>
    /// Legacy command compatibility metadata.
    /// For non-legacy typed event types, this returns a cached <see cref="NostifyCommand"/>
    /// shim that mirrors the current <see cref="eventType"/> metadata.
    /// </summary>
    [Obsolete("Use eventType instead.")]
    NostifyCommand command { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the aggregate root to which the event applies.
    /// </summary>
    /// <para>
    /// <strong>The series of events for an Aggregate should have the same key.</strong>
    /// </para>
    Guid aggregateRootId { get; set; }

    /// <summary>
    /// Gets or sets the properties to apply, or <see langword="null"/> for legacy or interoperable events without a payload.
    /// </summary>
    /// <remarks>
    /// Payload property names must match target property names. Delete events conventionally carry only the identifier of the aggregate to delete.
    /// </remarks>
    object? payload { get; set; }

    /// <summary>
    /// Determines whether the payload contains a property with the specified name.
    /// </summary>
    /// <param name="propertyName">The property name to locate.</param>
    /// <returns><see langword="true"/> when the payload contains the property; otherwise, <see langword="false"/>.</returns>
    bool PayloadHasProperty(string propertyName);

    /// <summary>
    /// Version of the event schema for compatibility and migration purposes.
    /// </summary>
    int schemaVersion { get; }

    /// <summary>
    /// Deserializes the payload as the requested type.
    /// </summary>
    /// <typeparam name="T">The payload type to return.</typeparam>
    /// <returns>The deserialized payload.</returns>
    /// <exception cref="InvalidOperationException">The event has no payload.</exception>
    T GetPayload<T>();

    /// <summary>
    /// Validates if the payload contains all required properties for performing a command on an aggregate of type T.
    /// </summary>
    /// <param name="throwErrorIfExtraProps">If true, will throw a ValidationException if any properties on payload not existing on T are found.</param>
    /// <returns>Returns the event for chaining.</returns>
    /// <typeparam name="T">The type of the aggregate to validate against.</typeparam>
    IEvent ValidatePayload<T>(bool throwErrorIfExtraProps = true) where T : class;
}
