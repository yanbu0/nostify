

using System;

namespace nostify;

/// <summary>
/// Base metadata for error events published by Nostify.
/// </summary>
public abstract class ErrorEventType : EventType
{
    /// <summary>
    /// Initializes error-event metadata.
    /// </summary>
    protected ErrorEventType(string name, bool isNew = false, bool allowNullPayload = false)
        : base(name, isNew, allowNullPayload)
    {
    }
}

/// <summary>Identifies a bulk-create failure.</summary>
public sealed class BulkCreateErrorEventType : ErrorEventType
{
    /// <summary>Initializes the canonical error-event definition.</summary>
    public BulkCreateErrorEventType() : base("Error_BulkCreate") { }
}

/// <summary>Identifies a bulk-upsert failure.</summary>
public sealed class BulkUpsertErrorEventType : ErrorEventType
{
    /// <summary>Initializes the canonical error-event definition.</summary>
    public BulkUpsertErrorEventType() : base("Error_BulkUpsert") { }
}

/// <summary>Identifies a bulk event-persistence failure.</summary>
public sealed class BulkPersistErrorEventType : ErrorEventType
{
    /// <summary>Initializes the canonical error-event definition.</summary>
    public BulkPersistErrorEventType() : base("Error_BulkPersistEvent") { }
}

/// <summary>Identifies a bulk apply-and-persist failure.</summary>
public sealed class BulkApplyAndPersistErrorEventType : ErrorEventType
{
    /// <summary>Initializes the canonical error-event definition.</summary>
    public BulkApplyAndPersistErrorEventType() : base("Error_BulkApplyAndPersist") { }
}

/// <summary>Identifies a projection-handler failure.</summary>
public sealed class HandleProjectionErrorEventType : ErrorEventType
{
    /// <summary>Initializes the canonical error-event definition.</summary>
    public HandleProjectionErrorEventType() : base("Error_HandleProjection") { }
}

/// <summary>Identifies an aggregate-event-handler failure.</summary>
public sealed class HandleAggregateEventErrorEventType : ErrorEventType
{
    /// <summary>Initializes the canonical error-event definition.</summary>
    public HandleAggregateEventErrorEventType() : base("Error_HandleAggregateEvent") { }
}

/// <summary>Identifies a multi-apply-event-handler failure.</summary>
public sealed class HandleMultiApplyEventErrorEventType : ErrorEventType
{
    /// <summary>Initializes the canonical error-event definition.</summary>
    public HandleMultiApplyEventErrorEventType() : base("Error_HandleMultiApplyEvent") { }
}

/// <summary>
/// Legacy error-command metadata retained for source compatibility.
/// </summary>
[Obsolete("ErrorCommand is deprecated; use a concrete ErrorEventType for new error events.")]
#pragma warning disable CS0618 // The shim intentionally preserves the published NostifyCommand hierarchy.
public class ErrorCommand : NostifyCommand
#pragma warning restore CS0618
{
    /// <summary>Bulk Create Error.</summary>
    public static ErrorCommand BulkCreate = new("Error_BulkCreate");

    /// <summary>Bulk Upsert Error.</summary>
    public static ErrorCommand BulkUpsert = new("Error_BulkUpsert");

    /// <summary>Bulk Persist Event Error.</summary>
    public static ErrorCommand BulkPersistEvent = new("Error_BulkPersistEvent");

    /// <summary>Bulk Apply and Persist Error.</summary>
    public static ErrorCommand BulkApplyAndPersist = new("Error_BulkApplyAndPersist");

    /// <summary>Handle Projection Error.</summary>
    public static ErrorCommand HandleProjection = new("Error_HandleProjection");

    /// <summary>Handle Aggregate Event Error.</summary>
    public static ErrorCommand HandleAggregateEvent = new("Error_HandleAggregateEvent");

    /// <summary>Handle Multi Apply Event Error.</summary>
    public static ErrorCommand HandleMultiApplyEvent = new("Error_HandleMultiApplyEvent");

    /// <summary>
    /// Initializes legacy error-command metadata.
    /// </summary>
    public ErrorCommand(string name, bool isNew = false) : base(name, isNew)
    {
    }
}

/// <summary>
/// Maps legacy error commands to modern error-event metadata.
/// </summary>
internal static class ErrorEventTypeMapper
{
#pragma warning disable CS0618 // This class is the compatibility boundary for ErrorCommand.
    internal static ErrorEventType FromCommand(ErrorCommand errorCommand)
#pragma warning restore CS0618
    {
        ArgumentNullException.ThrowIfNull(errorCommand);

        // Resolve built-in names to discoverable definitions so deserialization restores
        // their concrete CLR types. Custom names use a non-legacy EventType so newly
        // authored envelopes are schema version 2 while retaining all wire metadata.
        return errorCommand.name switch
        {
            "Error_BulkCreate" => new BulkCreateErrorEventType(),
            "Error_BulkUpsert" => new BulkUpsertErrorEventType(),
            "Error_BulkPersistEvent" => new BulkPersistErrorEventType(),
            "Error_BulkApplyAndPersist" => new BulkApplyAndPersistErrorEventType(),
            "Error_HandleProjection" => new HandleProjectionErrorEventType(),
            "Error_HandleAggregateEvent" => new HandleAggregateEventErrorEventType(),
            "Error_HandleMultiApplyEvent" => new HandleMultiApplyEventErrorEventType(),
            _ => new AdHocErrorEventType(errorCommand.name, errorCommand.isNew, errorCommand.allowNullPayload)
        };
    }

    /// <summary>
    /// Runtime metadata for a custom legacy error-command name.
    /// It is intentionally not a discoverable parameterless definition.
    /// </summary>
    private sealed class AdHocErrorEventType : ErrorEventType
    {
        internal AdHocErrorEventType(string name, bool isNew, bool allowNullPayload)
            : base(name, isNew, allowNullPayload)
        {
        }
    }
}

/// <summary>
/// Represents an error event in Nostify to publish to Kafka.
/// </summary>
public class NostifyErrorEvent : Event
{
    /// <summary>
    /// Initializes an error event with modern event-type metadata.
    /// </summary>
    public NostifyErrorEvent(ErrorEventType errorEventType, Guid aggregateRootId, ErrorPayload errorPayload, Guid userId, Guid partitionKey)
        : base(errorEventType, aggregateRootId, errorPayload, userId, partitionKey)
    {
    }

    /// <summary>
    /// Initializes an error event from legacy error-command metadata.
    /// </summary>
    [Obsolete("Use NostifyErrorEvent(ErrorEventType, ...) instead.")]
#pragma warning disable CS0618 // This overload is the source-compatibility boundary.
    public NostifyErrorEvent(ErrorCommand errorCommand, Guid aggregateRootId, ErrorPayload errorPayload, Guid userId, Guid partitionKey)
        : this(ErrorEventTypeMapper.FromCommand(errorCommand), aggregateRootId, errorPayload, userId, partitionKey)
#pragma warning restore CS0618
    {
    }
}

/// <summary>
/// Represents an error payload for NostifyErrorEvent
/// </summary>
public class ErrorPayload
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorPayload"/> class.
    /// </summary>
    /// <remarks>Default constructor for serialization.</remarks>
    public ErrorPayload()
    {
        ErrorMessage = string.Empty;
        StackTrace = null;
        Payload = new object();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorPayload"/> class.
    /// <param name="errorMessage">The error message.</param>
    /// <param name="payload">The payload of the event that failed.</param>
    /// <param name="stackTrace">Optional. The stack trace.</param>
    /// </summary>
    public ErrorPayload(string errorMessage, object payload, string? stackTrace = null)
    {
        ErrorMessage = errorMessage;
        StackTrace = stackTrace;
        Payload = payload;
    }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the stack trace.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Payload of the event that failed.
    /// </summary>
    public object Payload { get; set; }
}