
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
    /// Gets or sets the unique identifier of the aggregate root 
    /// associated with the Saga.
    /// </summary>
    public Guid aggregateRootId { get; set; }

    /// <summary>
    /// Gets or sets the current status of the Saga.
    /// </summary>
    public SagaStatus status { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the Saga was created.
    /// </summary>
    public DateTime createdOn { get; set; }

    /// <summary>
    /// Gets or sets the list of steps that make up the Saga.
    /// </summary>
    public List<SagaStep> steps { get; set; }

    /// <summary>
    /// Gets or sets the index of the current step in the Saga.
    /// </summary>
    public int currentStep { get; set; }

    public Saga(string name, Guid aggregateRootId, List<SagaStep> steps)
    {
        this.id = Guid.NewGuid();
        this.name = name;
        this.aggregateRootId = aggregateRootId;
        status = SagaStatus.Pending;
        createdOn = DateTime.UtcNow;
        currentStep = 0;
        this.steps = steps;
    }

    public async Task StartAsync(INostify nostify)
    {
        await HandleSuccessDoNextStepAsync(nostify);
    }

    public async Task HandleSuccessDoNextStepAsync(INostify nostify)
    {
        if (currentStep < steps.Count)
        {
            if(currentStep == 0)
            {
                status = SagaStatus.InProgress;
            }
            else
            {
                var completedStep = steps[currentStep];
                completedStep.status = SagaStepStatus.CompletedSuccessfully;
            }
            currentStep = currentStep++;
            var step = steps[currentStep];
            step.status = SagaStepStatus.Triggered;
            // Update saga container
            var sagaContainer = await nostify.GetSagaContainerAsync();
            await sagaContainer.UpsertItemAsync<Saga>(this);
            // Publish the step event
            await nostify.PersistEventAsync(step.stepEvent);
        }
        else
        {
            throw new InvalidOperationException("No more steps to execute.");
        }
    }
}