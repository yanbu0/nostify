

using System;

namespace nostify;

/// <summary>
/// Represents an error command in Nostify.
/// </summary>
public class ErrorCommand : NostifyCommand
{
    /// <summary>
    /// Bulk Create Error
    /// </summary>
    public static ErrorCommand BulkCreate = new ErrorCommand("Error_BulkCreate", false);

    /// <summary>
    /// Bulk Upsert Error
    /// </summary>
    public static ErrorCommand BulkUpsert = new ErrorCommand("Error_BulkUpsert", false);

    /// <summary>
    /// Bulk Persist Event Error
    /// </summary>
    public static ErrorCommand BulkPersistEvent = new ErrorCommand("Error_BulkPersistEvent", false);

    /// <summary>
    /// Bulk Apply and Persist Error
    /// </summary>
    public static ErrorCommand BulkApplyAndPersist = new ErrorCommand("Error_BulkApplyAndPersist", false);

    /// <summary>
    /// Handle Projection Error
    /// </summary>
    public static ErrorCommand HandleProjection = new ErrorCommand("Error_HandleProjection", false);

    /// <summary>
    /// Handle Aggregate Event Error
    /// </summary>
    public static ErrorCommand HandleAggregateEvent = new ErrorCommand("Error_HandleAggregateEvent", false);

    /// <summary>
    /// Handle Multi Apply Event Error
    /// </summary>
    public static ErrorCommand HandleMultiApplyEvent = new ErrorCommand("Error_HandleMultiApplyEvent", false);

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorCommand"/> class.
    /// </summary>
    /// <param name="name">The name of the error command.</param>
    /// <param name="isNew">Indicates whether the command is new.</param>
    public ErrorCommand(string name, bool isNew = false) : base(name, isNew)
    {
    }
}

/// <summary>
/// Represents an error event in Nostify to publish to kafka.
/// </summary>
public class NostifyErrorEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NostifyErrorEvent"/> class.
    /// </summary>
    /// <param name="errorCommand">The error command.</param>
    /// <param name="aggregateRootId">The aggregate root identifier.</param>
    /// <param name="errorPayload">Error payload for the event that failed. Will contain error message and payload.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="partitionKey">The partition key.</param>
    public NostifyErrorEvent(ErrorCommand errorCommand, Guid aggregateRootId, ErrorPayload errorPayload, Guid userId, Guid partitionKey)
        : base(errorCommand, aggregateRootId, errorPayload, userId, partitionKey)
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