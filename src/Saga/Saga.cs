
using System;
using System.Collections.Generic;

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
}