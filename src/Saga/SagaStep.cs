namespace nostify;

/// <summary>
/// Represents a step in the saga process, including its order, events, and status.
/// </summary>
public class SagaStep : ISagaStep
{
    /// <inheritdoc/>
    public int order { get; set; } = 0;
    /// <inheritdoc/>
    public Event stepEvent { get; set; }
    /// <inheritdoc/>
    public Event? rollbackEvent { get; set; }
    /// <inheritdoc/>
    public SagaStepStatus status { get; set; }
    /// <inheritdoc/>
    public object? successData { get; set; }
    /// <inheritdoc/>
    public object? rollbackData { get; set; }
    /// <inheritdoc/>
    public DateTime? executionStart { get; set; }
    /// <inheritdoc/>
    public DateTime? executionComplete { get; set; }
    /// <inheritdoc/>
    public DateTime? rollbackStart { get; set; }
    /// <inheritdoc/>
    public DateTime? rollbackComplete { get; set; }
    /// <inheritdoc/>
    public Guid aggregateRootId => stepEvent.aggregateRootId;

    /// <summary>
    /// For JSON serialization purposes only.
    /// This constructor is not intended for use in application logic.
    /// </summary>
    public SagaStep()
    {
        order = 1;
        stepEvent = new Event(); 
        rollbackEvent = null;
        status = SagaStepStatus.WaitingForTrigger;
        successData = null;
        rollbackData = null;
        executionStart = null;
        executionComplete = null;
        rollbackStart = null;
        rollbackComplete = null;

    }

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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task RollbackAsync(INostify nostify)
    {
        // Validate this step is in the correct state to rollback
        if (status != SagaStepStatus.Triggered && status != SagaStepStatus.CompletedSuccessfully)
            throw new InvalidOperationException("Step is not in a valid state to rollback.");

        // Set the rollback completion time
        rollbackStart = DateTime.UtcNow;
        // Set the status to rolling back
        status = SagaStepStatus.RollingBack;

        // Publish the rollback event
        if (rollbackEvent != null)
        {
            await nostify.PersistEventAsync(rollbackEvent);
        }
        else
        {
            // Complete the rollback if no rollback event is provided
            CompleteRollback();
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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