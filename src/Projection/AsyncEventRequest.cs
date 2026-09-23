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
    /// Gets or sets the Kafka topic to which the request is published.
    /// The expected format is <c>{serviceName}_EventRequest</c>.
    /// </summary>
    public string topic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Kafka topic to which the response is published.
    /// The expected format is <c>{serviceName}_EventRequestResponse</c>.
    /// An empty value falls back to <see cref="topic"/> for backward compatibility.
    /// </summary>
    public string responseTopic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional subtopic reserved for more granular filtering.
    /// </summary>
    public string subtopic { get; set; } = string.Empty;

    /// <summary>
    /// The aggregate root IDs to request events for.
    /// </summary>
    public List<Guid> aggregateRootIds { get; set; } = new List<Guid>();

    /// <summary>
    /// Optional point in time to query events up to. If null, queries all events.
    /// </summary>
    public DateTime? pointInTime { get; set; }

    /// <summary>
    /// Gets or sets the unique correlation identifier used to match responses to this request.
    /// A new GUID string is generated for each batch request.
    /// </summary>
    public string correlationId { get; set; } = string.Empty;
}
