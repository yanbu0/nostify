using System;
using System.Net;
using Newtonsoft.Json;

namespace nostify;

/// <summary>
/// Identifies the terminal outcome of persisting one input event in a bulk operation.
/// </summary>
public enum BulkPersistEventOutcome
{
    /// <summary>The event was created in Cosmos DB.</summary>
    Created,

    /// <summary>The event already existed and the 409 conflict was treated as idempotent success.</summary>
    AlreadyExists,

    /// <summary>The write failed without exhausting a configured throttling retry policy.</summary>
    Failed,

    /// <summary>The write remained throttled after all configured retries were attempted.</summary>
    RetryExhausted
}

/// <summary>
/// Describes retry activity for one bulk event persistence input.
/// </summary>
public sealed class BulkPersistRetryDetails
{
    /// <summary>Gets the total number of Cosmos write attempts, including the initial attempt.</summary>
    public int AttemptCount { get; init; }

    /// <summary>Gets the number of retries performed after the initial attempt.</summary>
    public int RetryCount => Math.Max(0, AttemptCount - 1);

    /// <summary>Gets whether the configured retry allowance was exhausted.</summary>
    public bool Exhausted { get; init; }

    /// <summary>Gets the most recent server-provided retry-after duration, when available.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Gets the most recent delay applied before a retry.</summary>
    public TimeSpan? LastDelay { get; init; }
}

/// <summary>
/// Describes a 409 Conflict returned while creating an event.
/// </summary>
public sealed class BulkPersistConflictDetails
{
    /// <summary>Gets the Cosmos conflict message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Gets whether the conflict was interpreted as an idempotent duplicate delivery.</summary>
    public bool TreatedAsIdempotentSuccess { get; init; }

    /// <summary>Gets the Cosmos substatus code, when provided.</summary>
    public int SubStatusCode { get; init; }

    /// <summary>Gets the Cosmos activity identifier, when provided.</summary>
    public string? ActivityId { get; init; }
}

/// <summary>
/// Describes best-effort undeliverable and optional error-event reporting for a failed write.
/// </summary>
public sealed class BulkPersistFailureReportingDetails
{
    /// <summary>Gets whether failure reporting was attempted.</summary>
    public bool Attempted { get; init; }

    /// <summary>Gets whether all requested failure reporting completed successfully.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets the reporting exception type when reporting failed.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Gets the reporting failure message when reporting failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets the original reporting exception for in-process inspection.</summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public Exception? Exception { get; init; }
}

/// <summary>
/// Represents the complete terminal result for one input supplied to a bulk event persistence operation.
/// </summary>
/// <remarks>
/// Results are returned in input order. <see cref="InputIndex"/> keeps duplicate event identifiers distinguishable.
/// A 409 conflict is reported as <see cref="BulkPersistEventOutcome.AlreadyExists"/> and is considered an
/// idempotent success, while retaining its Cosmos status and conflict details.
/// </remarks>
public sealed class BulkPersistEventResult
{
    /// <summary>Gets the zero-based position of the corresponding event in the input list.</summary>
    public int InputIndex { get; init; }

    /// <summary>Gets the identifier of the corresponding input event.</summary>
    public Guid EventId { get; init; }

    /// <summary>Gets the terminal persistence outcome.</summary>
    public BulkPersistEventOutcome Outcome { get; init; }

    /// <summary>Gets whether the event is present in Cosmos after this operation.</summary>
    public bool IsSuccess => Outcome is BulkPersistEventOutcome.Created or BulkPersistEventOutcome.AlreadyExists;

    /// <summary>Gets the Cosmos HTTP status code, when the operation produced one.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>Gets the Cosmos substatus code, when provided.</summary>
    public int? SubStatusCode { get; init; }

    /// <summary>Gets the Cosmos activity identifier, when provided.</summary>
    public string? ActivityId { get; init; }

    /// <summary>Gets the request charge reported by Cosmos, when available.</summary>
    public double? RequestCharge { get; init; }

    /// <summary>Gets Cosmos diagnostics for a successful write, when available.</summary>
    public string? Diagnostics { get; init; }

    /// <summary>Gets conflict details when <see cref="Outcome"/> is <see cref="BulkPersistEventOutcome.AlreadyExists"/>.</summary>
    public BulkPersistConflictDetails? Conflict { get; init; }

    /// <summary>Gets retry activity for this input.</summary>
    public BulkPersistRetryDetails Retry { get; init; } = new();

    /// <summary>Gets the primary persistence exception type when the write failed.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Gets the primary persistence failure message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets the original persistence exception for in-process inspection.</summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public Exception? Exception { get; init; }

    /// <summary>Gets details of secondary undeliverable and optional error-event reporting.</summary>
    public BulkPersistFailureReportingDetails Reporting { get; init; } = new();
}
