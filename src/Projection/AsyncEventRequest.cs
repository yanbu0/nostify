using System;
using System.Collections.Generic;

namespace nostify;

/// <summary>
/// Represents a request for events from an external service via Kafka.
/// Produced to the {serviceName}_EventRequest topic when a projection needs events from another microservice.
/// </summary>
public class AsyncEventRequest
{
    /// <summary>
    /// The Kafka topic the request is published to.
    /// Format: {serviceName}_EventRequest
    /// </summary>
    public string topic { get; set; }

    /// <summary>
    /// The Kafka topic to publish the response to.
    /// Format: {serviceName}_EventRequestResponse
    /// If null, falls back to <see cref="topic"/> for backward compatibility.
    /// </summary>
    public string responseTopic { get; set; }

    /// <summary>
    /// Reserved for future use. Subtopic for more granular filtering.
    /// </summary>
    public string subtopic { get; set; }

    /// <summary>
    /// The aggregate root IDs to request events for.
    /// </summary>
    public List<Guid> aggregateRootIds { get; set; } = new List<Guid>();

    /// <summary>
    /// Optional point in time to query events up to. If null, queries all events.
    /// </summary>
    public DateTime? pointInTime { get; set; }

    /// <summary>
    /// Unique correlation ID for matching responses to this request.
    /// Generated as a new Guid string per batch request.
    /// </summary>
    public string correlationId { get; set; }
}
