
namespace nostify;

/// <summary>
/// Represents the status of a step in a saga workflow.
/// </summary>
public enum SagaStepStatus
{
    /// <summary>
    /// The step is waiting for a trigger to start.
    /// </summary>
    WaitingForTrigger,

    /// <summary>
    /// The step has been triggered and is in progress.
    /// </summary>
    Triggered,

    /// <summary>
    /// The step completed successfully.
    /// </summary>
    CompletedSuccessfully,

    /// <summary>
    /// The step is in the process of rolling back.
    /// </summary>
    RollingBack,

    /// <summary>
    /// The step has been rolled back.
    /// </summary>
    RolledBack,

    /// <summary>
    /// The step has failed.
    /// </summary>
    Failed
}