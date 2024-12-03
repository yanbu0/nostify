

using System;
using System.Collections.Generic;
using nostify;

///<summary>
///List of Events queried from external data source to update a Projection
///</summary>
public class ExternalDataEvent
{
    /// <summary>
    /// Constructor for ExternalDataEvent
    /// </summary>
    public ExternalDataEvent(Guid aggregateRootId, List<Event> events = null)
    {
        this.aggregateRootId = aggregateRootId;
        this.events = events == null ? new List<Event>() : events;
    }

    /// <summary>
    /// Aggregate Root Id to update
    /// </summary>
    public Guid aggregateRootId { get; set; }

    /// <summary>
    /// List of Events to apply to Projection
    /// </summary>
    public List<Event> events { get; set; }
}