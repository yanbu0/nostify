
namespace nostify;

/// <summary>
/// Represents the possible statuses of a saga.
/// </summary>
public enum SagaStatus
{
    /// <summary>
    /// The saga is pending and has not started yet.
    /// </summary>
    Pending,
    /// <summary>
    /// The saga is currently in progress.
    /// </summary>
    InProgress,
    /// <summary>
    /// The saga has completed successfully.
    /// </summary>
    CompletedSuccessfully,
    /// <summary>
    /// The saga is rolling back due to a failure.
    /// </summary>
    RollingBack,
    /// <summary>
    /// The saga has been rolled back.
    /// </summary>
    RolledBack,
    /// <summary>
    /// The saga has failed.
    /// </summary>
    Failed
}