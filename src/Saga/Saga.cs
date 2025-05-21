
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace nostify;

/// <summary>
/// Represents a Saga, which is a long-running process or transaction 
/// that can be broken into smaller steps and coordinated.
/// </summary>
public class Saga
{
    /// <summary>
    /// Gets or sets the unique identifier of the Saga.
    /// </summary>
    public Guid id { get; set; }

    /// <summary>
    /// Gets or sets the name of the Saga.
    /// </summary>
    public string name { get; set; }

    /// <summary>
    /// Gets or sets the current status of the Saga.
    /// </summary>
    public SagaStatus status { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the Saga was created.
    /// </summary>
    public DateTime createdOn { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the Saga was completed successfully.
    /// </summary>
    public DateTime? executionCompletedOn { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the Saga execution started.
    /// </summary>
    public DateTime? executionStart { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the rollback process started.
    /// </summary>
    public DateTime? rollbackStartedOn { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the rollback process completed.
    /// </summary>
    public DateTime? rollbackCompletedOn { get; set; }

    /// <summary>
    /// Gets or sets the error message if the Saga fails.
    /// </summary>
    public string? errorMessage { get; set; }

    /// <summary>
    /// Gets or sets the error message if the Saga rollback fails.
    /// </summary>
    public string? rollbackErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the list of steps that make up the Saga.
    /// </summary>
    public List<SagaStep> steps { get; set; }

    /// <summary>
    /// If the saga is executing, this is the step that is currently executing. If it is rolling back, this is the step that is currently rolling back. 
    /// Will return -1 if the saga is completed or rolled back, will return 0 if saga has not yet been started.
    /// </summary>
    public int currentlyExecutingStep
    {
        get
        {
            if (status == SagaStatus.RolledBack || status == SagaStatus.CompletedSuccessfully)
            {
                return -1;
            }
            else if (status == SagaStatus.Pending)
            {
                return 0;
            }
            else
            {
                return steps.OrderBy(x => x.order).ToList().First(x => x.status == SagaStepStatus.Triggered || x.status == SagaStepStatus.RollingBack)?.order ?? -1;
            }
        }
    }

    /// <summary>
    /// If the saga is rolling back, this will return the next step to rollback. If the saga is executing, this will return the next step to execute.  
    /// Will return -1 if the currently executing step is the final step, or the saga is completed or rolled back.
    /// </summary>
    public int nextStep
    {
        get
        {
            if (currentlyExecutingStep == -1)
            {
                return -1;
            }
            else if (status == SagaStatus.RollingBack)
            {
                return steps.OrderBy(x => x.order).ToList().First(x => x.status == SagaStepStatus.RolledBack)?.order - 1 ?? -1;
            }
            else
            {
                return steps.OrderBy(x => x.order).ToList().First(x => x.status == SagaStepStatus.WaitingForTrigger)?.order ?? -1;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Saga"/> class.
    /// </summary>
    /// <param name="name">The name of the Saga.</param>
    /// <param name="steps">The list of steps that make up the Saga.</param>
    public Saga(string name, List<SagaStep> steps)
    {
        this.id = Guid.NewGuid();
        this.name = name;
        status = SagaStatus.Pending;
        createdOn = DateTime.UtcNow;
        this.steps = steps;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Saga"/> class with the specified name and an empty list of steps.
    /// </summary>
    /// <param name="name">The name of the Saga.</param>
    public Saga(string name)
    {
        this.id = Guid.NewGuid();
        this.name = name;
        status = SagaStatus.Pending;
        createdOn = DateTime.UtcNow;
        steps = new List<SagaStep>();
    }
    
    /// <summary>
    /// Adds a step to the Saga with the specified event and optional rollback event.
    /// </summary>
    /// <param name="stepEvent">The event representing the step to add.</param>
    /// <param name="rollbackEvent">The optional event to use for rolling back this step.</param>
    public void AddStep(Event stepEvent, Event? rollbackEvent = null)
    {
        // Find next highest order
        var nextOrder = steps.Count == 0 ? 1 : steps.Max(x => x.order) + 1;
        // Add step
        var step = new SagaStep(nextOrder, stepEvent, rollbackEvent);
        steps.Add(step);
    }

    /// <summary>
    /// Starts the Saga by triggering the first step.
    /// </summary>
    /// <param name="nostify">The Nostify instance used to publish events.</param>
    /// <exception cref="InvalidOperationException">Thrown if the Saga has already been started or if the first step is invalid.</exception>
    public async Task StartAsync(INostify nostify)
    {
        // Check if the saga is already started
        if (status != SagaStatus.Pending) throw new InvalidOperationException("Saga has already been started.");
        // Check if the first step is valid
        var firstStep = steps.Where(s => s.status == SagaStepStatus.WaitingForTrigger).Single(x => x.order == 1) ?? throw new InvalidOperationException("Saga has no unexecuted step 1.");

        // Start the first step
        await firstStep.StartAsync(nostify);

        // Update saga status and execution start time
        status = SagaStatus.InProgress;
        executionStart = DateTime.UtcNow;

        await SaveAsync(nostify);
    }

    /// <summary>
    /// When the currently executing step is completed successfully, this method will be called to update the status of the saga and the step and trigger the next step.
    /// </summary>
    /// <param name="nostify">The Nostify instance used to publish events.</param>
    /// <param name="successData">The data returned from the step if needed for a subsequent step.</param>
    public async Task HandleSuccessfulStepAsync(INostify nostify, object? successData = null)
    {
        // Check if the saga is in progress
        if (status != SagaStatus.InProgress) throw new InvalidOperationException("Saga is not in progress.");
        // Check if the currently executing step is valid
        var currentStep = steps.Where(s => s.status == SagaStepStatus.Triggered).Single(x => x.order == currentlyExecutingStep);
        // Update the status of the current step and the saga
        currentStep.Complete(successData);

        // Check if the saga is completed
        if (currentlyExecutingStep == steps.Max(x => x.order))
        {
            CompleteSaga();
        }
        else
        {
            // Trigger the next step
            await TriggerNextStepAfterSuccess(nostify);
        }

        await SaveAsync(nostify);
    }

    private async Task TriggerNextStepAfterSuccess(INostify nostify)
    {
        // Check that saga is in valid state to trigger next step
        if (status == SagaStatus.CompletedSuccessfully || status == SagaStatus.RolledBack) throw new InvalidOperationException("Saga is completed, cannot trigger next step");
        if (currentlyExecutingStep == -1) throw new InvalidOperationException("Saga has no currently executing step.");

        // Get the next step
        SagaStep nextSagaStep = steps[nextStep];
        await nextSagaStep.StartAsync(nostify);
    }

    private void CompleteSaga()
    {
        // Check if the saga is in progress
        if (status != SagaStatus.InProgress) throw new InvalidOperationException("Saga is not in progress.");
        // Update the status of the saga
        status = SagaStatus.CompletedSuccessfully;
        executionCompletedOn = DateTime.UtcNow;
    }

    /// <summary>
    /// Handles a failed step in the Saga by updating the status and initiating the rollback process.
    /// </summary>
    /// <param name="nostify">The Nostify instance used to publish events.</param>
    public async Task StartRollbackAsync(INostify nostify)
    {
        // Check if the saga is in progress
        if (status != SagaStatus.InProgress) throw new InvalidOperationException("Saga is not in progress.");
        
        // Get the currently executing step
        var currentStep = steps[currentlyExecutingStep] ?? throw new InvalidOperationException("Saga has no currently executing step.");

        // Update saga rollback data
        rollbackStartedOn = DateTime.UtcNow;
        status = SagaStatus.RollingBack;

        // Rollback step
        await currentStep.RollbackAsync(nostify);

        await SaveAsync(nostify);
    }

    public async Task HandleSuccessfulStepRollbackAsync(INostify nostify, object? rollbackData = null)
    {
        // Check if the saga is rolling back
        if (status != SagaStatus.RollingBack) throw new InvalidOperationException("Saga is not rolling back.");
        // Check if the currently executing step is valid
        var currentStep = steps[currentlyExecutingStep] ?? throw new InvalidOperationException("Saga has no currently executing step.");
        currentStep.CompleteRollback(rollbackData);

        // Check if the saga is completed
        if (nextStep == -1)
        {
            CompleteSagaRollback();
        }
        else
        {
            // Trigger the next step
            var nextRollbackStep = steps[nextStep];
            await nextRollbackStep.RollbackAsync(nostify);
        }

        await SaveAsync(nostify);
    }

    private void CompleteSagaRollback()
    {
        // Check if the saga is rolling back
        if (status != SagaStatus.RollingBack) throw new InvalidOperationException("Saga is not rolling back.");
        // Update the status of the saga
        status = SagaStatus.RolledBack;
        rollbackCompletedOn = DateTime.UtcNow;
    }

    private async Task SaveAsync(INostify nostify)
    {
        var sagaContainer = await nostify.GetSagaContainerAsync();
        await sagaContainer.UpsertItemAsync<Saga>(this);
    }
}