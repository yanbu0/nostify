

namespace nostify;

/// <summary>
/// Represents a step in the saga process, including its order, events, and status.
/// </summary>
public class SagaStep
{
    /// <summary>
    /// Gets or sets the order of the step in the saga process.
    /// </summary>
    public int order { get; set; }
    /// <summary>
    /// Gets or sets the event that will be published during this step in the saga process.
    /// </summary>
    public Event stepEvent { get; set; }
    /// <summary>
    /// Gets or sets the event that will be published during the rollback of this step in the saga process.
    /// </summary>
    public Event? rollbackEvent { get; set; }
    /// <summary>
    /// Gets or sets the status of the step in the saga process.
    /// </summary>
    public SagaStepStatus status { get; set; }
    /// <summary>
    /// Gets or sets the data returned from the Saga Step if needed for a subsequent step.
    /// </summary>
    public object? successData { get; set; }
    /// <summary>
    /// Gets or sets the data from the rollback if needed to rollback a previous step.
    /// </summary>
    public object? rollbackData { get; set; }
    /// <summary>
    /// Gets or sets the start time of the step execution.
    /// </summary>
    public DateTime? executionStart { get; set; }
    /// <summary>
    /// Gets or sets the completion time of the step execution.
    /// </summary>
    public DateTime? executionComplete { get; set; }
    /// <summary>
    /// Gets or sets the start time of the rollback execution.
    /// </summary>
    public DateTime? rollbackStart { get; set; }
    /// <summary>
    /// Gets or sets the completion time of the rollback execution.
    /// </summary>
    public DateTime? rollbackComplete { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaStep"/> class.
    /// </summary>
    public SagaStep(int order, Event stepEvent, Event? rollbackEvent = null)
    {
        this.order = order;
        this.stepEvent = stepEvent;
        this.rollbackEvent = rollbackEvent;
        status = SagaStepStatus.WaitingForTrigger;
    }

    /// <summary>
    /// Starts the execution of this saga step by publishing its event and updating its status.
    /// </summary>
    /// <param name="nostify">The INostify instance used to persist the event.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(INostify nostify)
    {
        // Validate this step is in the correct state to start
        if (status != SagaStepStatus.WaitingForTrigger) throw new InvalidOperationException("Step is not in a valid state to start.");

        // Publish the event
        await nostify.PersistEventAsync(stepEvent);

        // Set the start time
        executionStart = DateTime.UtcNow;
        // Set the status to triggered
        status = SagaStepStatus.Triggered;
    }

    /// <summary>
    /// Rolls back this saga step by publishing its rollback event, if one exists, and updating its status.
    /// If no rollback event is defined, the step will be marked as rolled back without any action.
    /// </summary>
    /// <param name="nostify">The INostify instance used to persist the rollback event.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RollbackAsync(INostify nostify)
    {
        // Validate this step is in the correct state to rollback
        if (status != SagaStepStatus.Triggered && status != SagaStepStatus.CompletedSuccessfully)
            throw new InvalidOperationException("Step is not in a valid state to rollback.");

        // Publish the rollback event
        if (rollbackEvent != null)
        {
            await nostify.PersistEventAsync(rollbackEvent);
        }

        // Set the rollback completion time
        rollbackComplete = DateTime.UtcNow;
        // Set the status to rolled back
        status = SagaStepStatus.RolledBack;
    }

    public void Complete(object? successData = null)
    {
        // Validate this step is in the correct state to complete
        if (status != SagaStepStatus.Triggered) throw new InvalidOperationException("Step is not in a valid state to complete.");

        // Set the completion time
        executionComplete = DateTime.UtcNow;
        // Set the status to completed successfully
        status = SagaStepStatus.CompletedSuccessfully;
        // Set the success data
        this.successData = successData;
    }

    public void CompleteRollback(object? rollbackData = null)
    {
        // Validate this step is in the correct state to complete rollback
        if (status != SagaStepStatus.RollingBack) throw new InvalidOperationException("Step is not in a valid state to complete rollback.");

        // Set the rollback completion time
        rollbackComplete = DateTime.UtcNow;
        // Set the status to rolled back
        status = SagaStepStatus.RolledBack;
        // Set the rollback data
        this.rollbackData = rollbackData;
    }
}