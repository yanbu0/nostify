using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace nostify;

/// <summary>
/// Interface for Saga, representing a long-running process or transaction.
/// </summary>
public interface ISaga
{
    /// <summary>
    /// Unique identifier for the saga.
    /// </summary>
    Guid id { get; set; }

    /// <summary>
    /// Name of the saga.
    /// </summary>
    string name { get; set; }

    /// <summary>
    /// Current status of the saga.
    /// </summary>
    SagaStatus status { get; set; }

    /// <summary>
    /// Date and time when the saga was created.
    /// </summary>
    DateTime createdOn { get; set; }

    /// <summary>
    /// Date and time when the saga execution completed.
    /// </summary>
    DateTime? executionCompletedOn { get; set; }

    /// <summary>
    /// Date and time when the saga execution started.
    /// </summary>
    DateTime? executionStart { get; set; }

    /// <summary>
    /// Date and time when the rollback process started.
    /// </summary>
    DateTime? rollbackStartedOn { get; set; }

    /// <summary>
    /// Date and time when the rollback process completed.
    /// </summary>
    DateTime? rollbackCompletedOn { get; set; }

    /// <summary>
    /// Error message if the saga failed.
    /// </summary>
    string? errorMessage { get; set; }

    /// <summary>
    /// Error message if the rollback failed.
    /// </summary>
    string? rollbackErrorMessage { get; set; }

    /// <summary>
    /// List of steps in the saga.
    /// </summary>
    List<SagaStep> steps { get; set; }

    /// <summary>
    /// Gets the index of the currently executing step.
    /// </summary>
    /// <returns>The index of the currently executing step, or -1 if none.</returns>
    int GetCurrentlyExecutingStepIndex();

    /// <summary>
    /// The currently executing step.
    /// </summary>
    ISagaStep? GetCurrentlyExecutingStep();

    /// <summary>
    /// Index of the next step to execute.
    /// </summary>
    int GetNextStepIndex();

    /// <summary>
    /// The next step to execute.
    /// </summary>
    ISagaStep? GetNextStep();

    /// <summary>
    /// Index of the last completed step.
    /// </summary>
    int GetLastCompletedStepIndex();

    /// <summary>
    /// The last completed step.
    /// </summary>
    ISagaStep? GetLastCompletedStep();

    /// <summary>
    /// Adds a step to the saga.
    /// </summary>
    /// <param name="stepEvent">The event representing the step.</param>
    /// <param name="rollbackEvent">The event representing the rollback step (optional).</param>
    void AddStep(IEvent stepEvent, IEvent? rollbackEvent = null);

    /// <summary>
    /// Starts the saga execution.
    /// </summary>
    /// <param name="nostify">The Nostify instance used to publish events.</param>
    Task StartAsync(INostify nostify);

    /// <summary>
    /// Handles a successful step in the saga.
    /// </summary>
    /// <param name="nostify">The Nostify instance used to publish events.</param>
    /// <param name="successData">The data returned from the step if needed for a subsequent step.</param>
    Task HandleSuccessfulStepAsync(INostify nostify, object? successData = null);

    /// <summary>
    /// Handles a failed step in the Saga by updating the status and initiating the rollback process.
    /// </summary>
    /// <param name="nostify">The Nostify instance used to publish events.</param>
    Task StartRollbackAsync(INostify nostify);

    /// <summary>
    /// Handles a successful rollback step in the saga.
    /// </summary>
    /// <param name="nostify">The Nostify instance used to publish events.</param>
    /// <param name="rollbackData">The data returned from the rollback step if needed for a subsequent rollback step.</param>
    Task HandleSuccessfulStepRollbackAsync(INostify nostify, object? rollbackData = null);
}
