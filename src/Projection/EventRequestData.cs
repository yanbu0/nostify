using System;
using System.Collections.Generic;

namespace nostify;

/// <summary>
/// Request data model for external event queries that includes foreign IDs and optional point-in-time filtering
/// </summary>
public class EventRequestData
{
    /// <summary>
    /// List of aggregate root IDs to query events for
    /// </summary>
    public List<Guid> ForeignIds { get; set; } = new List<Guid>();

    /// <summary>
    /// Point in time to query events up to. If null, queries all events.
    /// </summary>
    public DateTime? PointInTime { get; set; }
}