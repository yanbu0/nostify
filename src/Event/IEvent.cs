using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace nostify;

/// <summary>
/// Represents events in event store
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Timestamp of event
    /// </summary>
    DateTime timestamp { get; set; }

    /// <summary>
    /// Partition key to apply event to
    /// </summary>
    Guid partitionKey { get; set; }

    /// <summary>
    /// Id of user
    /// </summary>
    Guid userId { get; set; }

    /// <summary>
    /// Id of event
    /// </summary>
    Guid id { get; set; }

    /// <summary>
    /// Command to perform, defined in Aggregate implementation
    /// </summary>
    NostifyCommand command { get; set; }

    /// <summary>
    /// Key of the Aggregate to perform the event on
    /// </summary>
    /// <para>
    /// <strong>The series of events for an Aggregate should have the same key.</strong>
    /// </para>
    Guid aggregateRootId { get; set; }

    /// <summary>
    /// Object containing properties of Aggregate to perform the command on
    /// </summary>
    /// <para>
    /// Properties must be the exact same name to have updates applied.
    /// </para>
    /// <para>
    /// Delete command should contain solelly the id value of the Aggregate to delete.
    /// </para>
    object payload { get; set; }

    /// <summary>
    /// Checks if the payload of this event has a property
    /// </summary>
    /// <param name="propertyName">Property to check for</param>
    bool PayloadHasProperty(string propertyName);

    /// <summary>
    /// Version of the event schema for compatibility and migration purposes.
    /// </summary>
    int schemaVersion { get; }

    /// <summary>
    /// Returns typed value of payload
    /// </summary>
    T GetPayload<T>();

    /// <summary>
    /// Validates if the payload contains all required properties for performing a command on an aggregate of type T.
    /// </summary>
    /// <param name="throwErrorIfExtraProps">If true, will throw a ValidationException if any properties on payload not existing on T are found.</param>
    /// <returns>Returns the event for chaining.</returns>
    /// <typeparam name="T">The type of the aggregate to validate against.</typeparam>
    IEvent ValidatePayload<T>(bool throwErrorIfExtraProps = true) where T : class;
}