

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
    /// <param name="eventThatFailed">The event that failed.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="partitionKey">The partition key.</param>
    public NostifyErrorEvent(ErrorCommand errorCommand, Guid aggregateRootId, object eventThatFailed, Guid userId = default, Guid partitionKey = default) 
        : base(errorCommand, aggregateRootId, eventThatFailed, userId, partitionKey)
    {
    }

}