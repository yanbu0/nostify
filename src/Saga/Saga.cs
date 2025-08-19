using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace nostify;

/// <summary>
/// Represents a Saga, which is a long-running process or transaction 
/// that can be broken into smaller steps and coordinated.
/// </summary>
public class Saga : ISaga
{
    /// <inheritdoc/>
    public Guid id { get; set; }

    /// <inheritdoc/>
    public string name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public SagaStatus status { get; set; }

    /// <inheritdoc/>
    public DateTime createdOn { get; set; }

    /// <inheritdoc/>
    public DateTime? executionCompletedOn { get; set; }

    /// <inheritdoc/>
    public DateTime? executionStart { get; set; }

    /// <inheritdoc/>
    public DateTime? rollbackStartedOn { get; set; }

    /// <inheritdoc/>
    public DateTime? rollbackCompletedOn { get; set; }

    /// <inheritdoc/>
    public string? errorMessage { get; set; }

    /// <inheritdoc/>
    public string? rollbackErrorMessage { get; set; }

    /// <inheritdoc/>
    public List<SagaStep> steps { get; set; } = new List<SagaStep>();

    /// <inheritdoc/>
    public int GetCurrentlyExecutingStepIndex()
    {
        if (status == SagaStatus.RolledBack || status == SagaStatus.CompletedSuccessfully || status == SagaStatus.Pending)
        {
            return -1;
        }
        else
        {
            var step = steps.OrderBy(x => x.order).FirstOrDefault(x => x.status == SagaStepStatus.Triggered || x.status == SagaStepStatus.RollingBack);
            return step != null ? steps.IndexOf(step) : -1;
        }
    }

    /// <inheritdoc/>
    public ISagaStep? GetCurrentlyExecutingStep() => GetCurrentlyExecutingStepIndex() != -1 ? steps[GetCurrentlyExecutingStepIndex()] : null;

    /// <inheritdoc/>
    public int GetNextStepIndex()
    {
        if (status == SagaStatus.RollingBack)
        {
            var step = steps.OrderBy(x => x.order).LastOrDefault(x => x.status == SagaStepStatus.CompletedSuccessfully);
            return step != null ? steps.IndexOf(step) : -1;
        }
        else
        {
            var step = steps.OrderBy(x => x.order).FirstOrDefault(x => x.status == SagaStepStatus.WaitingForTrigger);
            return step != null ? steps.IndexOf(step) : -1;
        }
        
    }

    /// <inheritdoc/>
    public ISagaStep? GetNextStep() => GetNextStepIndex() != -1 ? steps[GetNextStepIndex()] : null;

    /// <inheritdoc/>
    public int GetLastCompletedStepIndex()
    {
        if (status == SagaStatus.RollingBack)
        {
            var step = steps.OrderBy(x => x.order).FirstOrDefault(x => x.status == SagaStepStatus.RolledBack);
            return step != null ? steps.IndexOf(step) - 1 : -1;
        }
        else if (status == SagaStatus.InProgress)
        {
            var step = steps.OrderBy(x => x.order).FirstOrDefault(x => x.status == SagaStepStatus.CompletedSuccessfully);
            return step != null ? steps.IndexOf(step) : -1;
        }
        else
        {
            return -1;
        }
        
    }

    /// <inheritdoc/>
    public ISagaStep? GetLastCompletedStep() => GetLastCompletedStepIndex() != -1 ? steps[GetLastCompletedStepIndex()] : null;

    /// <summary>
    /// Default constructor for JSON serialization/deserialization.
    /// </summary>
    public Saga()
    {

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
    }
    
    /// <inheritdoc/>
    public void AddStep(Event stepEvent, Event? rollbackEvent = null)
    {
        // Find next highest order
        int nextOrder = steps.Count == 0 ? 1 : steps.Max(x => x.order) + 1;
        // Add step
        var step = new SagaStep(nextOrder, stepEvent, rollbackEvent);
        steps.Add(step);
    }

    /// <inheritdoc/>
    public async Task StartAsync(INostify nostify)
    {
        // Check if the saga is already started
        if (status != SagaStatus.Pending) throw new InvalidOperationException("Saga has already been started.");
        // Check if the first step is valid
        var firstStep = steps.Where(s => s.status == SagaStepStatus.WaitingForTrigger).Single(x => x.order == 1) ?? throw new InvalidOperationException("Saga has no unexecuted step 1.");

        // Update saga status and execution start time
        status = SagaStatus.InProgress;
        executionStart = DateTime.UtcNow;

        // Have to save here as well to ensure the saga is in a valid state before starting the first step
        await SaveAsync(nostify);

        // Start the first step
        await firstStep.StartAsync(nostify);
        await SaveAsync(nostify);
    }

    /// <inheritdoc/>
    public async Task HandleSuccessfulStepAsync(INostify nostify, object? successData = null)
    {
        // Check if the saga is in progress
        if (status != SagaStatus.InProgress) throw new InvalidOperationException("Saga is not in progress.");
        // Check if the currently executing step is valid
        var currentStep = GetCurrentlyExecutingStep() ?? throw new InvalidOperationException("Saga has no currently executing step.");

        // Check if the saga is completed
        if (GetCurrentlyExecutingStepIndex() == steps.IndexOf(steps.Last()))
        {
            CompleteSaga();
        }
        else
        {
            // Trigger the next step
            await TriggerNextStepAfterSuccess(nostify);
        }
        
        // Complete the current step, this needs to be done after the next step is triggered
        currentStep.Complete(successData);

        await SaveAsync(nostify);
    }

    private async Task TriggerNextStepAfterSuccess(INostify nostify)
    {
        // Check that saga is in valid state to trigger next step
        if (status == SagaStatus.CompletedSuccessfully || status == SagaStatus.RolledBack) throw new InvalidOperationException("Saga is completed, cannot trigger next step");
        if (GetCurrentlyExecutingStepIndex() == -1) throw new InvalidOperationException("Saga has no currently executing step.");

        // Get the next step
        ISagaStep nextSagaStep = GetNextStep() ?? throw new InvalidOperationException("Saga has no next step.");
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

    /// <inheritdoc/>
    public async Task StartRollbackAsync(INostify nostify)
    {
        // Check if the saga is in progress
        if (status != SagaStatus.InProgress) throw new InvalidOperationException("Saga is not in progress.");
        
        // Get the currently executing step
        ISagaStep currentStep = GetCurrentlyExecutingStep() ?? throw new InvalidOperationException("Saga has no currently executing step.");

        // Update saga rollback data
        rollbackStartedOn = DateTime.UtcNow;
        status = SagaStatus.RollingBack;

        // Rollback step
        await currentStep.RollbackAsync(nostify);

        await SaveAsync(nostify);
    }

    /// <inheritdoc/>
    public async Task HandleSuccessfulStepRollbackAsync(INostify nostify, object? rollbackData = null)
    {
        // Check if the saga is rolling back
        if (status != SagaStatus.RollingBack) throw new InvalidOperationException("Saga is not rolling back.");
        // Check if the currently executing step is valid
        ISagaStep currentStep = GetCurrentlyExecutingStep() ?? throw new InvalidOperationException("Saga has no currently executing step.");
        //Get the next step index before changes
        ISagaStep? nextStepBeforeChanges = GetNextStep();

        currentStep.CompleteRollback(rollbackData);

        // Check if the saga is completed
        if (nextStepBeforeChanges == null)
        {
            CompleteSagaRollback();
        }
        else
        {
            // Trigger the next step
            var nextRollbackStep = nextStepBeforeChanges ?? throw new InvalidOperationException("Saga has no next step.");
            await nextRollbackStep.RollbackAsync(nostify);
            // If all steps rolled back then complete the saga rollback
            if (!steps.Any(s => s.status != SagaStepStatus.RolledBack))
            {
                CompleteSagaRollback();
            }
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