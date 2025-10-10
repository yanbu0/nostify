using System;
using System.Threading.Tasks;

namespace nostify;

/// <summary>
/// Interface for a step in the saga process, including its order, events, and status.
/// </summary>
public interface ISagaStep
{
    /// <summary>
    /// Gets or sets the order of the step in the saga process.
    /// </summary>
    int order { get; set; }

    /// <summary>
    /// Gets or sets the event that will be published during this step in the saga process.
    /// </summary>
    IEvent stepEvent { get; set; }

    /// <summary>
    /// Gets or sets the event that will be published during the rollback of this step in the saga process.
    /// </summary>
    IEvent? rollbackEvent { get; set; }

    /// <summary>
    /// Gets or sets the status of the step in the saga process.
    /// </summary>
    SagaStepStatus status { get; set; }

    /// <summary>
    /// Gets or sets the data returned from the Saga Step if needed for a subsequent step.
    /// </summary>
    object? successData { get; set; }

    /// <summary>
    /// Gets or sets the data from the rollback if needed to rollback a previous step.
    /// </summary>
    object? rollbackData { get; set; }

    /// <summary>
    /// Gets or sets the start time of the step execution.
    /// </summary>
    DateTime? executionStart { get; set; }

    /// <summary>
    /// Gets or sets the completion time of the step execution.
    /// </summary>
    DateTime? executionComplete { get; set; }

    /// <summary>
    /// Gets or sets the start time of the rollback execution.
    /// </summary>
    DateTime? rollbackStart { get; set; }

    /// <summary>
    /// Gets or sets the completion time of the rollback execution.
    /// </summary>
    DateTime? rollbackComplete { get; set; }

    /// <summary>
    /// Gets the aggregate root ID associated with the step event.
    /// </summary>
    Guid aggregateRootId { get; }

    /// <summary>
    /// Starts the execution of this saga step by publishing its event and updating its status.
    /// </summary>
    /// <param name="nostify">The INostify instance used to persist the event.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartAsync(INostify nostify);

    /// <summary>
    /// Rolls back this saga step by publishing its rollback event, if one exists, and updating its status.
    /// If no rollback event is defined, the step will be marked as rolled back without any action.
    /// </summary>
    /// <param name="nostify">The INostify instance used to persist the rollback event.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RollbackAsync(INostify nostify);

    /// <summary>
    /// Completes this saga step, setting its status and completion time.
    /// </summary>
    /// <param name="successData">Optional data to store for use in subsequent steps.</param>
    void Complete(object? successData = null);

    /// <summary>
    /// Completes the rollback for this saga step, setting its status and completion time.
    /// </summary>
    /// <param name="rollbackData">Optional data to store for use in previous rollbacks.</param>
    void CompleteRollback(object? rollbackData = null);
}
