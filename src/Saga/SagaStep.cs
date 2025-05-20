

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
    public required Event stepEvent { get; set; }
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
}