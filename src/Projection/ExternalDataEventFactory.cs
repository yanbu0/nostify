

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace nostify;

/// <summary>
/// Configures and retrieves same-service and external events needed to initialize projections.
/// </summary>
/// <typeparam name="P">The projection type whose external data is requested.</typeparam>
public class ExternalDataEventFactory<P> where P : IProjection, IUniquelyIdentifiable, IApplyable
{
    private readonly HttpClient? _httpClient;
    private readonly INostify _nostify;
    private readonly IQueryExecutor _queryExecutor;
    private List<Guid> _sameServiceIds = new List<Guid>();
    private List<Func<P, Guid>> _foreignKeySelectors = new List<Func<P, Guid>>();
    private List<Func<P, Guid?>> _nullableForeignKeySelectors = new List<Func<P, Guid?>>();
    private List<Func<P, List<Guid>>> _foreignKeyListSelectors = new List<Func<P, List<Guid>>>();
    private List<Func<P, List<Guid?>>> _nullableForeignKeyListSelectors = new List<Func<P, List<Guid?>>>();
    private List<Func<P, Guid>> _dependantIdSelectors = new List<Func<P, Guid>>();
    private List<Func<P, Guid?>> _nullableDependantIdSelectors = new List<Func<P, Guid?>>();
    private List<Func<P, List<Guid>>> _dependantListIdSelectors = new List<Func<P, List<Guid>>>();
    private List<Func<P, List<Guid?>>> _nullableDependantListIdSelectors = new List<Func<P, List<Guid?>>>();
    private EventRequester<P>[] _eventRequestors = Array.Empty<EventRequester<P>>();
    private EventRequester<P>[] _dependantEventRequestors = Array.Empty<EventRequester<P>>();
    private AsyncEventRequester<P>[] _asyncEventRequestors = Array.Empty<AsyncEventRequester<P>>();
    private AsyncEventRequester<P>[] _dependantAsyncEventRequestors = Array.Empty<AsyncEventRequester<P>>();
    private GrpcEventRequester<P>[] _grpcEventRequestors = Array.Empty<GrpcEventRequester<P>>();
    private GrpcEventRequester<P>[] _dependantGrpcEventRequestors = Array.Empty<GrpcEventRequester<P>>();
    private List<P> _projectionsToInit = new List<P>();
    private DateTime? _pointInTime;
    private string? _grpcAuthToken;
    private string? _grpcAddress;

    private static readonly Action<ILogger, string, string, long, Exception?> LogCallTimingMessage =
        LoggerMessage.Define<string, string, long>(
            LogLevel.Information,
            new EventId(1, nameof(LogCallTimingMessage)),
            "ExternalDataEventFactory call={CallType} target={Target} elapsedMs={ElapsedMs}");

    private static readonly Action<ILogger, long, Exception?> LogTotalTimingMessage =
        LoggerMessage.Define<long>(
            LogLevel.Information,
            new EventId(2, nameof(LogTotalTimingMessage)),
            "ExternalDataEventFactory totalElapsedMs={TotalElapsedMs}");

    private static readonly Action<ILogger, Exception?> LogKafkaConsumerCloseFailure =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(LogKafkaConsumerCloseFailure)),
            "Kafka consumer close failed during external event request cleanup; disposal will continue");

    private static readonly Action<ILogger, string, string, Exception?> LogIndeterminateEventFilter =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4, nameof(LogIndeterminateEventFilter)),
            "ExternalDataEventFactory could not determine all handled event types for projection={ProjectionType}; returned events will not be filtered. Reason={Reason}");

    /// <summary>
    /// Gets whether events that the projection cannot apply are removed from event retrieval results.
    /// The default is <see langword="true"/>. When set to <see langword="false"/>, all returned
    /// events are retained and the developer may need to override the catch-all
    /// <see cref="NostifyObject.Apply(EventType, IEvent)"/> dispatch to avoid exceptions for
    /// unsupported event types.
    /// </summary>
    public bool RemoveNonAppliedEvents { get; }

    /// <summary>
    /// Creates a new ExternalDataEventFactory
    /// </summary>
    /// <param name="nostify">The INostify instance for accessing the event store</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="httpClient">Optional HTTP client for external service calls</param>
    /// <param name="pointInTime">Optional point in time to query events up to</param>
    /// <param name="queryExecutor">Optional query executor for unit testing. Defaults to CosmosQueryExecutor.</param>
    /// <param name="authToken">Optional default authentication token used by gRPC requestors when no per-call token is specified</param>
    /// <param name="grpcAddress">Optional default gRPC endpoint address used by <c>WithGrpcEventRequestor</c> and <c>WithDependantGrpcEventRequestor</c> overloads that omit the <c>address</c> parameter. When provided, the single-string overloads treat their first parameter as a service name rather than an endpoint address.</param>
    /// <param name="removeNonAppliedEvents">
    /// Whether to remove returned events that have no event-specific handler on <typeparamref name="P"/>.
    /// Defaults to <see langword="true"/>. If set to <see langword="false"/>, the developer may
    /// need to override the catch-all <see cref="NostifyObject.Apply(EventType, IEvent)"/> dispatch
    /// to avoid exceptions for unsupported event types.
    /// </param>
    public ExternalDataEventFactory(INostify nostify, List<P> projectionsToInit, HttpClient? httpClient = null, DateTime? pointInTime = null, IQueryExecutor? queryExecutor = null, string? authToken = null, string? grpcAddress = null, bool removeNonAppliedEvents = true)
    {
        this._nostify = nostify;
        this._httpClient = httpClient;
        this._projectionsToInit = projectionsToInit;
        this._pointInTime = pointInTime;
        this._queryExecutor = queryExecutor ?? CosmosQueryExecutor.Default;
        this._grpcAuthToken = authToken;
        this._grpcAddress = grpcAddress;
        RemoveNonAppliedEvents = removeNonAppliedEvents;
    }

    /// <summary>
    /// Removes events that are not handled by the projection when a complete finite handler set
    /// can be discovered. If discovery is indeterminate, all events are retained for compatibility.
    /// </summary>
    private List<ExternalDataEvent> FilterNonAppliedEvents(List<ExternalDataEvent> externalEvents)
    {
        if (!RemoveNonAppliedEvents || externalEvents.Count == 0)
        {
            return externalEvents;
        }

        HandledEventTypeResolver.Resolution resolution = HandledEventTypeResolver.GetOrBuild(typeof(P));
        if (!resolution.IsDeterminate)
        {
            ILogger? logger = _nostify.Logger;
            if (logger?.IsEnabled(LogLevel.Warning) == true)
            {
                LogIndeterminateEventFilter(
                    logger,
                    typeof(P).FullName ?? typeof(P).Name,
                    resolution.IndeterminateReason ?? "Unknown reason.",
                    null);
            }

            return externalEvents;
        }

        // Preserve each projection mapping and event order while dropping empty mappings.
        return externalEvents
            .Select(externalEvent => new ExternalDataEvent(
                externalEvent.aggregateRootId,
                externalEvent.events
                    .Where(evt => evt.eventType != null && resolution.EventTypeNames.Contains(evt.eventType.name))
                    .ToList()))
            .Where(externalEvent => externalEvent.events.Count != 0)
            .ToList();
    }

    /// <summary>
    /// Adds selectors for single foreign key IDs from the same service.
    /// </summary>
    /// <param name="selectors">Functions that extract non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithSameServiceIdSelectors(params Func<P, Guid>[] selectors)
    {
        _foreignKeySelectors.AddRange(selectors);
        return this;
    }

    /// <summary>
    /// Adds selectors for single nullable foreign key IDs from the same service.
    /// Null values are automatically filtered out during event retrieval.
    /// </summary>
    /// <param name="selectors">Functions that extract nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithSameServiceIdSelectors(params Func<P, Guid?>[] selectors)
    {
        _nullableForeignKeySelectors.AddRange(selectors);
        return this;
    }

    /// <summary>
    /// Adds selectors for lists of foreign key IDs from the same service.
    /// </summary>
    /// <param name="selectors">Functions that extract lists of non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithSameServiceListIdSelectors(params Func<P, List<Guid>>[] selectors)
    {
        _foreignKeyListSelectors.AddRange(selectors);
        return this;
    }

    /// <summary>
    /// Adds selectors for lists of nullable foreign key IDs from the same service.
    /// Null values within the lists are automatically filtered out during event retrieval.
    /// </summary>
    /// <param name="selectors">Functions that extract lists of nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithSameServiceListIdSelectors(params Func<P, List<Guid?>>[] selectors)
    {
        _nullableForeignKeyListSelectors.AddRange(selectors);
        return this;
    }

    /// <summary>
    /// Adds selectors for IDs that depend on values populated by events from the primary selectors.
    /// These selectors are evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// </summary>
    /// <param name="selectors">Functions that extract dependent foreign key IDs from a projection after initial events are applied</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithSameServiceDependantIdSelectors(params Func<P, Guid>[] selectors)
    {
        _dependantIdSelectors.AddRange(selectors);
        return this;
    }

    /// <summary>
    /// Adds selectors for nullable IDs that depend on values populated by events from the primary selectors.
    /// These selectors are evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// Null values are automatically filtered out during event retrieval.
    /// </summary>
    /// <param name="selectors">Functions that extract nullable dependent foreign key IDs from a projection after initial events are applied</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithSameServiceDependantIdSelectors(params Func<P, Guid?>[] selectors)
    {
        _nullableDependantIdSelectors.AddRange(selectors);
        return this;
    }

    /// <summary>
    /// Adds selectors for lists of IDs that depend on values populated by events from the primary selectors.
    /// These selectors are evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// </summary>
    /// <param name="selectors">Functions that extract lists of dependent foreign key IDs from a projection after initial events are applied</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithSameServiceDependantListIdSelectors(params Func<P, List<Guid>>[] selectors)
    {
        _dependantListIdSelectors.AddRange(selectors);
        return this;
    }

    /// <summary>
    /// Adds selectors for lists of nullable IDs that depend on values populated by events from the primary selectors.
    /// These selectors are evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// Null values within the lists are automatically filtered out during event retrieval.
    /// </summary>
    /// <param name="selectors">Functions that extract lists of nullable dependent foreign key IDs from a projection after initial events are applied</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithSameServiceDependantListIdSelectors(params Func<P, List<Guid?>>[] selectors)
    {
        _nullableDependantListIdSelectors.AddRange(selectors);
        return this;
    }

    /// <summary>
    /// Adds event requestors for fetching events from external services.
    /// </summary>
    /// <param name="eventRequestors">Event requestors containing service URLs and ID selectors</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> AddEventRequestors(params EventRequester<P>[] eventRequestors)
    {
        if (_httpClient == null)
        {
            throw new InvalidOperationException("HttpClient is not provided. Cannot add external event requestors.");
        }
        this._eventRequestors = this._eventRequestors.Concat(eventRequestors).ToArray();
        return this;
    }

    /// <summary>
    /// Adds an event requestor for fetching events from an external service.
    /// </summary>
    /// <param name="serviceUrl">The URL of the external service's event endpoint</param>
    /// <param name="foreignIdSelectors">Functions that extract foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithEventRequestor(string serviceUrl, params Func<P, Guid?>[] foreignIdSelectors)
    {
        if (_httpClient == null)
        {
            throw new InvalidOperationException("HttpClient is not provided. Cannot add external event requestors.");
        }
        this._eventRequestors = this._eventRequestors.Append(new EventRequester<P>(serviceUrl, foreignIdSelectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds event requestors for external services that depend on values populated by events from the primary selectors.
    /// These requestors are evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// </summary>
    /// <param name="eventRequestors">Event requestors for external services with dependent ID selectors</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> AddDependantEventRequestors(params EventRequester<P>[] eventRequestors)
    {
        if (_httpClient == null)
        {
            throw new InvalidOperationException("HttpClient is not provided. Cannot add external event requestors.");
        }
        this._dependantEventRequestors = this._dependantEventRequestors.Concat(eventRequestors).ToArray();
        return this;
    }

    /// <summary>
    /// Adds an event requestor for an external service that depends on values populated by events from the primary selectors.
    /// This requestor is evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// </summary>
    /// <param name="serviceUrl">The URL of the external service's event endpoint</param>
    /// <param name="foreignIdSelectors">Functions that extract dependent foreign key IDs from a projection after initial events are applied</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantEventRequestor(string serviceUrl, params Func<P, Guid?>[] foreignIdSelectors)
    {
        if (_httpClient == null)
        {
            throw new InvalidOperationException("HttpClient is not provided. Cannot add external event requestors.");
        }
        this._dependantEventRequestors = this._dependantEventRequestors.Append(new EventRequester<P>(serviceUrl, foreignIdSelectors)).ToArray();
        return this;
    }

    #region Async Event Requestors (Kafka)

    /// <summary>
    /// Adds async event requestors for fetching events from external services via Kafka.
    /// </summary>
    /// <param name="asyncEventRequestors">Async event requestors containing service names and ID selectors</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> AddAsyncEventRequestors(params AsyncEventRequester<P>[] asyncEventRequestors)
    {
        this._asyncEventRequestors = this._asyncEventRequestors.Concat(asyncEventRequestors).ToArray();
        return this;
    }

    /// <summary>
    /// Adds dependent async event requestors for fetching events from external services via Kafka.
    /// These requestors are evaluated after the first round of events are applied to projections.
    /// </summary>
    /// <param name="asyncEventRequestors">Async event requestors for external services with dependent ID selectors</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> AddDependantAsyncEventRequestors(params AsyncEventRequester<P>[] asyncEventRequestors)
    {
        this._dependantAsyncEventRequestors = this._dependantAsyncEventRequestors.Concat(asyncEventRequestors).ToArray();
        return this;
    }

    /// <summary>
    /// Adds an async event requestor for fetching events from an external service via Kafka, using nullable Guid selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service (used to derive Kafka topic)</param>
    /// <param name="foreignIdSelectors">Functions that extract nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithAsyncEventRequestor(string serviceName, params Func<P, Guid?>[] foreignIdSelectors)
    {
        this._asyncEventRequestors = this._asyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, foreignIdSelectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds an async event requestor for fetching events from an external service via Kafka, using non-nullable Guid selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service (used to derive Kafka topic)</param>
    /// <param name="selectors">Functions that extract non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithAsyncEventRequestor(string serviceName, params Func<P, Guid>[] selectors)
    {
        this._asyncEventRequestors = this._asyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, selectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds an async event requestor for fetching events from an external service via Kafka, using nullable Guid list selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service (used to derive Kafka topic)</param>
    /// <param name="selectors">Functions that extract lists of nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithAsyncEventRequestor(string serviceName, params Func<P, List<Guid?>>[] selectors)
    {
        this._asyncEventRequestors = this._asyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, selectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds an async event requestor for fetching events from an external service via Kafka, using non-nullable Guid list selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service (used to derive Kafka topic)</param>
    /// <param name="selectors">Functions that extract lists of non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithAsyncEventRequestor(string serviceName, params Func<P, List<Guid>>[] selectors)
    {
        this._asyncEventRequestors = this._asyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, selectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds an async event requestor for fetching events from an external service via Kafka,
    /// using a mix of single nullable and list nullable selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service (used to derive Kafka topic)</param>
    /// <param name="single">Functions that return a single nullable foreign id</param>
    /// <param name="list">Functions that return a list of nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithAsyncEventRequestor(string serviceName, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)
    {
        this._asyncEventRequestors = this._asyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, single, list)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds an async event requestor for fetching events from an external service via Kafka,
    /// using a mix of single non-nullable and list non-nullable selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service (used to derive Kafka topic)</param>
    /// <param name="single">Functions that return a single non-nullable foreign id</param>
    /// <param name="list">Functions that return a list of non-nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithAsyncEventRequestor(string serviceName, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)
    {
        this._asyncEventRequestors = this._asyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, single, list)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent async event requestor via Kafka, using nullable Guid selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    public ExternalDataEventFactory<P> WithDependantAsyncEventRequestor(string serviceName, params Func<P, Guid?>[] foreignIdSelectors)
    {
        this._dependantAsyncEventRequestors = this._dependantAsyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, foreignIdSelectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent async event requestor via Kafka, using non-nullable Guid selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    public ExternalDataEventFactory<P> WithDependantAsyncEventRequestor(string serviceName, params Func<P, Guid>[] selectors)
    {
        this._dependantAsyncEventRequestors = this._dependantAsyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, selectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent async event requestor via Kafka, using nullable Guid list selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    public ExternalDataEventFactory<P> WithDependantAsyncEventRequestor(string serviceName, params Func<P, List<Guid?>>[] selectors)
    {
        this._dependantAsyncEventRequestors = this._dependantAsyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, selectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent async event requestor via Kafka, using non-nullable Guid list selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    public ExternalDataEventFactory<P> WithDependantAsyncEventRequestor(string serviceName, params Func<P, List<Guid>>[] selectors)
    {
        this._dependantAsyncEventRequestors = this._dependantAsyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, selectors)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent async event requestor via Kafka,
    /// using a mix of single nullable and list nullable selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    public ExternalDataEventFactory<P> WithDependantAsyncEventRequestor(string serviceName, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)
    {
        this._dependantAsyncEventRequestors = this._dependantAsyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, single, list)).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent async event requestor via Kafka,
    /// using a mix of single non-nullable and list non-nullable selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    public ExternalDataEventFactory<P> WithDependantAsyncEventRequestor(string serviceName, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)
    {
        this._dependantAsyncEventRequestors = this._dependantAsyncEventRequestors.Append(new AsyncEventRequester<P>(serviceName, single, list)).ToArray();
        return this;
    }

    #endregion

    #region gRPC Event Requestors

    /// <summary>
    /// Adds gRPC event requestors for fetching events from external services via gRPC.
    /// </summary>
    /// <param name="grpcEventRequestors">gRPC event requestors containing addresses and ID selectors</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> AddGrpcEventRequestors(params GrpcEventRequester<P>[] grpcEventRequestors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Concat(grpcEventRequestors).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor using nullable Guid selectors.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="foreignIdSelectors">Functions that extract nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, params Func<P, Guid?>[] foreignIdSelectors)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, foreignIdSelectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, foreignIdSelectors)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor using non-nullable Guid selectors.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="selectors">Functions that extract non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, params Func<P, Guid>[] selectors)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, selectors)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor using nullable Guid list selectors.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="selectors">Functions that extract lists of nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, params Func<P, List<Guid?>>[] selectors)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, selectors)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor using non-nullable Guid list selectors.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="selectors">Functions that extract lists of non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, params Func<P, List<Guid>>[] selectors)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, selectors)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor using a mix of single nullable and list nullable selectors.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="single">Functions that return a single nullable foreign id</param>
    /// <param name="list">Functions that return a list of nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, single, list) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, single, list)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor using a mix of single non-nullable and list non-nullable selectors.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="single">Functions that return a single non-nullable foreign id</param>
    /// <param name="list">Functions that return a list of non-nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, single, list) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, single, list)).ToArray();
        }
        return this;
    }

    // ── WithGrpcEventRequestor overloads with serviceName + authToken ──

    /// <summary>
    /// Adds a gRPC event requestor with a service name and auth token, using nullable Guid selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address (e.g. "https://localhost:5001")</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="foreignIdSelectors">Functions that extract nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, string? authToken, params Func<P, Guid?>[] foreignIdSelectors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, foreignIdSelectors) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name and auth token, using non-nullable Guid selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="selectors">Functions that extract non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, string? authToken, params Func<P, Guid>[] selectors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name and auth token, using nullable Guid list selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="selectors">Functions that extract lists of nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, string? authToken, params Func<P, List<Guid?>>[] selectors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name and auth token, using non-nullable Guid list selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="selectors">Functions that extract lists of non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, string? authToken, params Func<P, List<Guid>>[] selectors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name and auth token, using a mix of single nullable and list nullable selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="single">Functions that return a single nullable foreign id</param>
    /// <param name="list">Functions that return a list of nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, string? authToken, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, single, list) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name and auth token, using a mix of single non-nullable and list non-nullable selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="single">Functions that return a single non-nullable foreign id</param>
    /// <param name="list">Functions that return a list of non-nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, string? authToken, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, single, list) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    // ── WithGrpcEventRequestor overloads with serviceName only (uses constructor authToken) ──

    /// <summary>
    /// Adds a gRPC event requestor with a service name, using nullable Guid selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// </summary>
    /// <param name="address">The gRPC endpoint address (e.g. "https://localhost:5001")</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="foreignIdSelectors">Functions that extract nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, params Func<P, Guid?>[] foreignIdSelectors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, foreignIdSelectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name, using non-nullable Guid selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="selectors">Functions that extract non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, params Func<P, Guid>[] selectors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name, using nullable Guid list selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="selectors">Functions that extract lists of nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, params Func<P, List<Guid?>>[] selectors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name, using non-nullable Guid list selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="selectors">Functions that extract lists of non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, params Func<P, List<Guid>>[] selectors)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name, using a mix of single nullable and list nullable selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="single">Functions that return a single nullable foreign id</param>
    /// <param name="list">Functions that return a list of nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, single, list) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a gRPC event requestor with a service name, using a mix of single non-nullable and list non-nullable selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="single">Functions that return a single non-nullable foreign id</param>
    /// <param name="list">Functions that return a list of non-nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithGrpcEventRequestor(string address, string serviceName, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)
    {
        this._grpcEventRequestors = this._grpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, single, list) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds dependent gRPC event requestors. Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="grpcEventRequestors">gRPC event requestors for external services with dependent ID selectors</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> AddDependantGrpcEventRequestors(params GrpcEventRequester<P>[] grpcEventRequestors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Concat(grpcEventRequestors).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor using nullable Guid selectors. Evaluated after the first round of events are applied.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="foreignIdSelectors">Functions that extract nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, params Func<P, Guid?>[] foreignIdSelectors)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, foreignIdSelectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, foreignIdSelectors)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor using non-nullable Guid selectors. Evaluated after the first round of events are applied.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="selectors">Functions that extract non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, params Func<P, Guid>[] selectors)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, selectors)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor using nullable Guid list selectors. Evaluated after the first round of events are applied.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="selectors">Functions that extract lists of nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, params Func<P, List<Guid?>>[] selectors)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, selectors)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor using non-nullable Guid list selectors. Evaluated after the first round of events are applied.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="selectors">Functions that extract lists of non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, params Func<P, List<Guid>>[] selectors)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, selectors)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor using a mix of single nullable and list nullable selectors. Evaluated after the first round of events are applied.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="single">Functions that return a single nullable foreign id</param>
    /// <param name="list">Functions that return a list of nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, single, list) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, single, list)).ToArray();
        }
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor using a mix of single non-nullable and list non-nullable selectors. Evaluated after the first round of events are applied.
    /// When <c>grpcAddress</c> was provided in the constructor, the <paramref name="address"/> parameter is treated as
    /// a service name and the constructor-provided address is used as the endpoint. When no constructor
    /// <c>grpcAddress</c> was provided, this parameter is treated as the gRPC endpoint address directly.
    /// </summary>
    /// <param name="address">The gRPC endpoint address, or the service name when a constructor <c>grpcAddress</c> was set</param>
    /// <param name="single">Functions that return a single non-nullable foreign id</param>
    /// <param name="list">Functions that return a list of non-nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)
    {
        if (!string.IsNullOrEmpty(_grpcAddress))
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(_grpcAddress, address, single, list) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        }
        else
        {
            this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, single, list)).ToArray();
        }
        return this;
    }

    // ── WithDependantGrpcEventRequestor overloads with serviceName + authToken ──

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name and auth token, using nullable Guid selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="foreignIdSelectors">Functions that extract nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, string? authToken, params Func<P, Guid?>[] foreignIdSelectors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, foreignIdSelectors) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name and auth token, using non-nullable Guid selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="selectors">Functions that extract non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, string? authToken, params Func<P, Guid>[] selectors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name and auth token, using nullable Guid list selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="selectors">Functions that extract lists of nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, string? authToken, params Func<P, List<Guid?>>[] selectors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name and auth token, using non-nullable Guid list selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="selectors">Functions that extract lists of non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, string? authToken, params Func<P, List<Guid>>[] selectors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name and auth token, using a mix of single nullable and list nullable selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="single">Functions that return a single nullable foreign id</param>
    /// <param name="list">Functions that return a list of nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, string? authToken, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, single, list) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name and auth token, using a mix of single non-nullable and list non-nullable selectors.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="authToken">Optional authentication token sent as Bearer authorization metadata</param>
    /// <param name="single">Functions that return a single non-nullable foreign id</param>
    /// <param name="list">Functions that return a list of non-nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, string? authToken, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, single, list) { AuthToken = authToken ?? "" }).ToArray();
        return this;
    }

    // ── WithDependantGrpcEventRequestor overloads with serviceName only (uses constructor authToken) ──

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name, using nullable Guid selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="foreignIdSelectors">Functions that extract nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, params Func<P, Guid?>[] foreignIdSelectors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, foreignIdSelectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name, using non-nullable Guid selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="selectors">Functions that extract non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, params Func<P, Guid>[] selectors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name, using nullable Guid list selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="selectors">Functions that extract lists of nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, params Func<P, List<Guid?>>[] selectors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name, using non-nullable Guid list selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="selectors">Functions that extract lists of non-nullable foreign key IDs from a projection</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, params Func<P, List<Guid>>[] selectors)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, selectors) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name, using a mix of single nullable and list nullable selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="single">Functions that return a single nullable foreign id</param>
    /// <param name="list">Functions that return a list of nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, Func<P, Guid?>[] single, Func<P, List<Guid?>>[] list)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, single, list) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a dependent gRPC event requestor with a service name, using a mix of single non-nullable and list non-nullable selectors.
    /// The authentication token is taken from the factory's constructor <c>authToken</c> parameter.
    /// Evaluated after the first round of events are applied.
    /// </summary>
    /// <param name="address">The gRPC endpoint address</param>
    /// <param name="serviceName">The target service name for routing in a unified gRPC server</param>
    /// <param name="single">Functions that return a single non-nullable foreign id</param>
    /// <param name="list">Functions that return a list of non-nullable foreign ids</param>
    /// <returns>This factory instance for fluent chaining</returns>
    public ExternalDataEventFactory<P> WithDependantGrpcEventRequestor(string address, string serviceName, Func<P, Guid>[] single, Func<P, List<Guid>>[] list)
    {
        this._dependantGrpcEventRequestors = this._dependantGrpcEventRequestors.Append(new GrpcEventRequester<P>(address, serviceName, single, list) { AuthToken = _grpcAuthToken ?? "" }).ToArray();
        return this;
    }

    #endregion

    /// <summary>
    /// Retrieves all configured same-service, HTTP, asynchronous, and gRPC events.
    /// </summary>
    /// <param name="enableLogging">Whether to emit timing diagnostics for each configured request.</param>
    /// <returns>The external events grouped for the projections being initialized.</returns>
    public async Task<List<ExternalDataEvent>> GetEventsAsync(bool enableLogging = false)
    {
        var logger = _nostify.Logger;
        var shouldLog = enableLogging && logger != null;
        var totalStopwatch = shouldLog ? Stopwatch.StartNew() : null;

        if (enableLogging && logger == null)
        {
            Console.WriteLine("ExternalDataEventFactory.GetEventsAsync logging is enabled, but INostify.Logger is null. Configure logging with NostifyFactory.WithLogger(yourLogger).");
        }

        void LogCallTiming(string callType, string target, long elapsedMs)
        {
            if (!shouldLog)
            {
                return;
            }

            LogCallTimingMessage(logger!, callType, target, elapsedMs, null);
        }

        Stopwatch? StartCallStopwatch() => shouldLog ? Stopwatch.StartNew() : null;

        void StopAndLogCall(Stopwatch? stopwatch, string callType, string target)
        {
            if (stopwatch == null)
            {
                return;
            }

            stopwatch.Stop();
            LogCallTiming(callType, target, stopwatch.ElapsedMilliseconds);
        }

        string BuildTargetsString(IEnumerable<string> values)
        {
            var targets = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
            return targets.Count == 0 ? "unknown" : string.Join(",", targets);
        }

        var result = new List<ExternalDataEvent>();

        try
        {
            // Resolve Cosmos only when a same-service selector needs it. Kafka, HTTP, and gRPC-only
            // requestors must remain independently usable when their own dependencies are available.
            bool requiresEventStore =
                _foreignKeySelectors.Count != 0 ||
                _nullableForeignKeySelectors.Count != 0 ||
                _foreignKeyListSelectors.Count != 0 ||
                _nullableForeignKeyListSelectors.Count != 0 ||
                _dependantIdSelectors.Count != 0 ||
                _dependantListIdSelectors.Count != 0 ||
                _nullableDependantIdSelectors.Count != 0 ||
                _nullableDependantListIdSelectors.Count != 0;
            Container? eventStoreContainer = requiresEventStore
                ? await _nostify.GetEventStoreContainerAsync()
                : null;

            // Get events for single-ID selectors (non-nullable)
            if (_foreignKeySelectors.Count != 0)
            {
                var singleIdStopwatch = StartCallStopwatch();
                var singleIdEvents = await ExternalDataEvent.GetEventsAsync(
                    eventStoreContainer!,
                    _projectionsToInit,
                    _queryExecutor,
                    _pointInTime,
                    _foreignKeySelectors.ToArray());
                StopAndLogCall(singleIdStopwatch, "WithSameServiceIdSelectors", "eventStore");
                result.AddRange(FilterNonAppliedEvents(singleIdEvents));
            }

            // Get events for single-ID selectors (nullable) - nulls filtered by HasValue in ExternalDataEvent
            if (_nullableForeignKeySelectors.Count != 0)
            {
                var nullableSingleIdStopwatch = StartCallStopwatch();
                var nullableSingleIdEvents = await ExternalDataEvent.GetEventsAsync(
                    eventStoreContainer!,
                    _projectionsToInit,
                    _queryExecutor,
                    _pointInTime,
                    _nullableForeignKeySelectors.ToArray());
                StopAndLogCall(nullableSingleIdStopwatch, "WithSameServiceIdSelectorsNullable", "eventStore");
                result.AddRange(FilterNonAppliedEvents(nullableSingleIdEvents));
            }

            // Get events for list-ID selectors (non-nullable)
            if (_foreignKeyListSelectors.Count != 0)
            {
                var listIdStopwatch = StartCallStopwatch();
                var listIdEvents = await ExternalDataEvent.GetEventsAsync(
                    eventStoreContainer!,
                    _projectionsToInit,
                    _queryExecutor,
                    _pointInTime,
                    _foreignKeyListSelectors.ToArray());
                StopAndLogCall(listIdStopwatch, "WithSameServiceListIdSelectors", "eventStore");
                result.AddRange(FilterNonAppliedEvents(listIdEvents));
            }

            // Get events for list-ID selectors (nullable) - nulls within lists filtered by HasValue in ExternalDataEvent
            if (_nullableForeignKeyListSelectors.Count != 0)
            {
                var nullableListIdStopwatch = StartCallStopwatch();
                var nullableListIdEvents = await ExternalDataEvent.GetEventsAsync(
                    eventStoreContainer!,
                    _projectionsToInit,
                    _queryExecutor,
                    _pointInTime,
                    _nullableForeignKeyListSelectors.ToArray());
                StopAndLogCall(nullableListIdStopwatch, "WithSameServiceListIdSelectorsNullable", "eventStore");
                result.AddRange(FilterNonAppliedEvents(nullableListIdEvents));
            }

            // Handle external service IDs before dependent selectors
            // so that external events can also populate dependent IDs
            if (_httpClient != null)
            {
                var externalEventsStopwatch = StartCallStopwatch();
                var externalEvents = await ExternalDataEvent.GetMultiServiceEventsAsync<P>(_httpClient!,
                    this._projectionsToInit,
                    this._pointInTime,
                    this._eventRequestors);
                StopAndLogCall(externalEventsStopwatch, "WithEventRequestor", BuildTargetsString(this._eventRequestors.Select(r => r.Url)));
                result.AddRange(FilterNonAppliedEvents(externalEvents));
            }

            // Handle async (Kafka) event requestors
            if (_asyncEventRequestors.Length != 0)
            {
                var asyncEventsStopwatch = StartCallStopwatch();
                var asyncEvents = await GetAsyncEventsAsync(_asyncEventRequestors, _projectionsToInit);
                StopAndLogCall(asyncEventsStopwatch, "WithAsyncEventRequestor", BuildTargetsString(_asyncEventRequestors.Select(r => r.ServiceName)));
                result.AddRange(FilterNonAppliedEvents(asyncEvents));
            }

            // Handle gRPC event requestors
            if (_grpcEventRequestors.Length != 0)
            {
                var grpcEventsStopwatch = StartCallStopwatch();
                var grpcEvents = await ExternalDataEvent.GetMultiServiceEventsViaGrpcAsync<P>(
                    this._projectionsToInit,
                    this._pointInTime,
                    this._grpcEventRequestors);
                StopAndLogCall(grpcEventsStopwatch, "WithGrpcEventRequestor", BuildTargetsString(this._grpcEventRequestors.Select(r => $"{r.ServiceName}@{r.Address}")));
                result.AddRange(FilterNonAppliedEvents(grpcEvents));
            }

            // Handle dependent selectors - these require applying ALL initial events first to get the IDs
            // This runs after both local and external events have been collected
            if (_dependantIdSelectors.Count != 0 || _dependantListIdSelectors.Count != 0 || _nullableDependantIdSelectors.Count != 0 || _nullableDependantListIdSelectors.Count != 0)
            {
                var dependantEventsStopwatch = StartCallStopwatch();
                var dependantEvents = await GetDependantEventsAsync(eventStoreContainer!, result);
                StopAndLogCall(dependantEventsStopwatch, "WithSameServiceDependantSelectors", "eventStore");
                result.AddRange(FilterNonAppliedEvents(dependantEvents));
            }

            // Handle dependent external event requestors - these also require applying initial events first
            if (_httpClient != null && _dependantEventRequestors.Length != 0)
            {
                var dependantExternalEventsStopwatch = StartCallStopwatch();
                var dependantExternalEvents = await GetDependantExternalEventsAsync(result);
                StopAndLogCall(dependantExternalEventsStopwatch, "WithDependantEventRequestor", BuildTargetsString(_dependantEventRequestors.Select(r => r.Url)));
                result.AddRange(FilterNonAppliedEvents(dependantExternalEvents));
            }

            // Handle dependent async (Kafka) event requestors - these also require applying initial events first
            if (_dependantAsyncEventRequestors.Length != 0)
            {
                var dependantAsyncEventsStopwatch = StartCallStopwatch();
                var dependantAsyncEvents = await GetDependantAsyncEventsAsync(result);
                StopAndLogCall(dependantAsyncEventsStopwatch, "WithDependantAsyncEventRequestor", BuildTargetsString(_dependantAsyncEventRequestors.Select(r => r.ServiceName)));
                result.AddRange(FilterNonAppliedEvents(dependantAsyncEvents));
            }

            // Handle dependent gRPC event requestors - these also require applying initial events first
            if (_dependantGrpcEventRequestors.Length != 0)
            {
                var dependantGrpcEventsStopwatch = StartCallStopwatch();
                var dependantGrpcEvents = await GetDependantGrpcEventsAsync(result);
                StopAndLogCall(dependantGrpcEventsStopwatch, "WithDependantGrpcEventRequestor", BuildTargetsString(_dependantGrpcEventRequestors.Select(r => $"{r.ServiceName}@{r.Address}")));
                result.AddRange(FilterNonAppliedEvents(dependantGrpcEvents));
            }

            return result;
        }
        finally
        {
            if (totalStopwatch != null)
            {
                totalStopwatch.Stop();
                LogTotalTimingMessage(logger!, totalStopwatch.ElapsedMilliseconds, null);
            }
        }
    }

    /// <summary>
    /// Gets events for dependent selectors by first applying the initial events to projections,
    /// then extracting the dependent IDs and querying for their events.
    /// </summary>
    private async Task<List<ExternalDataEvent>> GetDependantEventsAsync(Container eventStoreContainer, List<ExternalDataEvent> initialEvents)
    {
        var dependantEvents = new List<ExternalDataEvent>();

        // Create temporary copies of projections and apply initial events to extract dependent IDs
        var projectionsWithAppliedEvents = new List<P>();
        foreach (var projection in _projectionsToInit)
        {
            // Create a deep copy of the projection using JSON serialization
            var json = JsonConvert.SerializeObject(projection);
            var projectionCopy = JsonConvert.DeserializeObject<P>(json);

            if (projectionCopy == null)
            {
                continue;
            }

            // Find events for this projection and apply them
            var eventsForProjection = initialEvents
                .Where(e => e.aggregateRootId == projection.id)
                .SelectMany(e => e.events)
                .OrderBy(e => e.timestamp);

            foreach (var evt in eventsForProjection)
            {
                projectionCopy.Apply(evt);
            }

            projectionsWithAppliedEvents.Add(projectionCopy);
        }

        // Collect all dependent IDs from the updated projections
        var dependantIds = new HashSet<Guid>();

        foreach (var projection in projectionsWithAppliedEvents)
        {
            // Selector failures indicate invalid projection configuration and must remain observable.
            foreach (var selector in _dependantIdSelectors)
            {
                var id = selector(projection);
                if (id != Guid.Empty)
                {
                    dependantIds.Add(id);
                }
            }

            foreach (var selector in _nullableDependantIdSelectors)
            {
                var id = selector(projection);
                if (id.HasValue && id.Value != Guid.Empty)
                {
                    dependantIds.Add(id.Value);
                }
            }

            foreach (var selector in _dependantListIdSelectors)
            {
                var ids = selector(projection);
                if (ids != null)
                {
                    foreach (var id in ids.Where(id => id != Guid.Empty))
                    {
                        dependantIds.Add(id);
                    }
                }
            }

            foreach (var selector in _nullableDependantListIdSelectors)
            {
                var ids = selector(projection);
                if (ids != null)
                {
                    foreach (var id in ids.Where(id => id.HasValue && id.Value != Guid.Empty))
                    {
                        dependantIds.Add(id!.Value);
                    }
                }
            }
        }

        // Remove any IDs we've already fetched events for
        // Get the event aggregateRootIds from initial events (not the ExternalDataEvent.aggregateRootId which is the projection id)
        var existingEventIds = initialEvents.SelectMany(e => e.events.Select(evt => evt.aggregateRootId)).ToHashSet();
        var newIds = dependantIds.Except(existingEventIds).ToList();

        // Only query if there are new IDs to fetch
        if (newIds.Count != 0)
        {
            // Query for events matching the dependent IDs
            var query = eventStoreContainer.GetItemLinqQueryable<Event>()
                .Where(e => newIds.Contains(e.aggregateRootId));

            if (_pointInTime.HasValue)
            {
                query = query.Where(e => e.timestamp <= _pointInTime.Value);
            }

            var events = await _queryExecutor.ReadAllAsync(query);

            // Group events by aggregateRootId and create ExternalDataEvents
            // Map back to the original projection IDs
            foreach (var projection in projectionsWithAppliedEvents)
            {
                var projectionDependantIds = new HashSet<Guid>();

                // Re-evaluate selectors to map fetched events back to each projection.
                foreach (var selector in _dependantIdSelectors)
                {
                    var id = selector(projection);
                    if (id != Guid.Empty && newIds.Contains(id))
                    {
                        projectionDependantIds.Add(id);
                    }
                }

                foreach (var selector in _nullableDependantIdSelectors)
                {
                    var id = selector(projection);
                    if (id.HasValue && id.Value != Guid.Empty && newIds.Contains(id.Value))
                    {
                        projectionDependantIds.Add(id.Value);
                    }
                }

                foreach (var selector in _dependantListIdSelectors)
                {
                    var ids = selector(projection);
                    if (ids != null)
                    {
                        foreach (var id in ids.Where(id => id != Guid.Empty && newIds.Contains(id)))
                        {
                            projectionDependantIds.Add(id);
                        }
                    }
                }

                foreach (var selector in _nullableDependantListIdSelectors)
                {
                    var ids = selector(projection);
                    if (ids != null)
                    {
                        foreach (var id in ids.Where(id => id.HasValue && id.Value != Guid.Empty && newIds.Contains(id.Value)))
                        {
                            projectionDependantIds.Add(id!.Value);
                        }
                    }
                }

                // Get events for this projection's dependent IDs
                var projectionEvents = events
                    .Where(e => projectionDependantIds.Contains(e.aggregateRootId))
                    .ToList();

                if (projectionEvents.Count != 0)
                {
                    // Find the original projection (not the copy)
                    var originalProjection = _projectionsToInit.First(p => p.id == projection.id);
                    dependantEvents.Add(new ExternalDataEvent(originalProjection.id, projectionEvents));
                }
            }
        }

        return dependantEvents;
    }

    /// <summary>
    /// Gets events from external services for dependent requestors by first applying the initial events to projections,
    /// then using the dependent event requestors to fetch events from external services.
    /// </summary>
    private async Task<List<ExternalDataEvent>> GetDependantExternalEventsAsync(List<ExternalDataEvent> initialEvents)
    {
        // Create temporary copies of projections and apply initial events
        var projectionsWithAppliedEvents = new List<P>();
        foreach (var projection in _projectionsToInit)
        {
            // Create a deep copy of the projection using JSON serialization
            var json = JsonConvert.SerializeObject(projection);
            var projectionCopy = JsonConvert.DeserializeObject<P>(json);

            if (projectionCopy == null)
            {
                continue;
            }

            // Find events for this projection and apply them
            var eventsForProjection = initialEvents
                .Where(e => e.aggregateRootId == projection.id)
                .SelectMany(e => e.events)
                .OrderBy(e => e.timestamp);

            foreach (var evt in eventsForProjection)
            {
                projectionCopy.Apply(evt);
            }

            projectionsWithAppliedEvents.Add(projectionCopy);
        }

        // Use the updated projections with the dependent event requestors
        var dependantExternalEvents = await ExternalDataEvent.GetMultiServiceEventsAsync<P>(
            _httpClient!,
            projectionsWithAppliedEvents,
            _pointInTime,
            _dependantEventRequestors);

        // Map the results back to the original projection IDs
        var result = new List<ExternalDataEvent>();
        foreach (var externalEvent in dependantExternalEvents)
        {
            // Find the original projection ID that corresponds to this updated projection
            var updatedProjection = projectionsWithAppliedEvents.FirstOrDefault(p => p.id == externalEvent.aggregateRootId);
            if (updatedProjection != null)
            {
                // The aggregateRootId is already the projection id, so we can use it directly
                result.Add(externalEvent);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets events from external services for dependent async (Kafka) requestors by first applying the initial events
    /// to projections, then using the dependent async event requestors to fetch events via Kafka.
    /// </summary>
    private async Task<List<ExternalDataEvent>> GetDependantAsyncEventsAsync(List<ExternalDataEvent> initialEvents)
    {
        // Create temporary copies of projections and apply initial events
        var projectionsWithAppliedEvents = new List<P>();
        foreach (var projection in _projectionsToInit)
        {
            var json = JsonConvert.SerializeObject(projection);
            var projectionCopy = JsonConvert.DeserializeObject<P>(json);

            if (projectionCopy == null)
            {
                continue;
            }

            var eventsForProjection = initialEvents
                .Where(e => e.aggregateRootId == projection.id)
                .SelectMany(e => e.events)
                .OrderBy(e => e.timestamp);

            foreach (var evt in eventsForProjection)
            {
                projectionCopy.Apply(evt);
            }

            projectionsWithAppliedEvents.Add(projectionCopy);
        }

        // Use the updated projections with the dependent async event requestors
        var asyncEvents = await GetAsyncEventsAsync(_dependantAsyncEventRequestors, projectionsWithAppliedEvents);

        // Map the results back to the original projection IDs (they should already match since we use projection.id)
        return asyncEvents;
    }

    /// <summary>
    /// Gets events from external services for dependent gRPC requestors by first applying the initial events
    /// to projections, then using the dependent gRPC event requestors to fetch events via gRPC.
    /// </summary>
    private async Task<List<ExternalDataEvent>> GetDependantGrpcEventsAsync(List<ExternalDataEvent> initialEvents)
    {
        // Create temporary copies of projections and apply initial events
        var projectionsWithAppliedEvents = new List<P>();
        foreach (var projection in _projectionsToInit)
        {
            var json = JsonConvert.SerializeObject(projection);
            var projectionCopy = JsonConvert.DeserializeObject<P>(json);

            if (projectionCopy == null)
            {
                continue;
            }

            var eventsForProjection = initialEvents
                .Where(e => e.aggregateRootId == projection.id)
                .SelectMany(e => e.events)
                .OrderBy(e => e.timestamp);

            foreach (var evt in eventsForProjection)
            {
                projectionCopy.Apply(evt);
            }

            projectionsWithAppliedEvents.Add(projectionCopy);
        }

        // Use the updated projections with the dependent gRPC event requestors
        var grpcEvents = await ExternalDataEvent.GetMultiServiceEventsViaGrpcAsync<P>(
            projectionsWithAppliedEvents,
            _pointInTime,
            _dependantGrpcEventRequestors);

        // Map the results back to the original projection IDs (they should already match since we use projection.id)
        return grpcEvents;
    }

    /// <summary>
    /// Fetches events from external services via Kafka request-response pattern.
    /// For each requestor, produces an <see cref="AsyncEventRequest"/> message and consumes 
    /// <see cref="AsyncEventRequestResponse"/> messages until complete or timeout.
    /// </summary>
    /// <param name="requestors">The async event requestors to process</param>
    /// <param name="projections">The projections to extract foreign IDs from</param>
    /// <returns>List of ExternalDataEvent from all async requestors</returns>
    private async Task<List<ExternalDataEvent>> GetAsyncEventsAsync(AsyncEventRequester<P>[] requestors, List<P> projections)
    {
        var result = new List<ExternalDataEvent>();

        // Read timeout from environment variable
        var timeoutSecondsStr = Environment.GetEnvironmentVariable("AsyncEventRequestTimeoutSeconds");
        int timeoutSeconds = 30;
        if (int.TryParse(timeoutSecondsStr, out var parsedTimeout) && parsedTimeout > 0)
        {
            timeoutSeconds = parsedTimeout;
        }

        // Create a dedicated consumer and consumer group per factory invocation. A unique group prevents
        // concurrent projection initializations from partition-sharing response records and avoids stale
        // committed offsets influencing AutoOffsetReset.Latest assignment.
        var consumerGroup = $"{P.containerName}-{Guid.NewGuid():N}";
        var consumer = _nostify.CreateKafkaConsumer(consumerGroup);

        try
        {
            // Process each requestor
            foreach (var requestor in requestors)
            {
                // Collect all foreign IDs for this requestor.
                var allSelectors = requestor.ListSelectors.Length != 0
                    ? requestor.GetAllForeignIdSelectors(projections)
                    : requestor.ForeignIdSelectors;

                var foreignIds = (
                    from p in projections
                    from f in allSelectors
                    let foreignId = f(p)
                    where foreignId.HasValue && foreignId.Value != Guid.Empty
                    select foreignId!.Value
                ).Distinct().ToList();

                if (foreignIds.Count == 0)
                {
                    continue;
                }

                // Ensure consumer is subscribed to the response topic
                var responseTopic = requestor.ResponseTopicName;
                var currentSubscription = consumer.Subscription ?? new List<string>();
                if (!currentSubscription.Contains(responseTopic))
                {
                    var newSubscription = currentSubscription.Concat(new[] { responseTopic }).Distinct().ToList();
                    consumer.Subscribe(newSubscription);

                    // Wait for the rebalance, then explicitly establish a high-watermark
                    // boundary before publishing. Assignment alone can be visible before
                    // librdkafka has resolved AutoOffsetReset.Latest; an immediate response
                    // in that window could otherwise become the eventual "latest" offset
                    // and be skipped.
                    var assignmentDeadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                    while (!consumer.Assignment.Any(partition => partition.Topic == responseTopic) &&
                           DateTime.UtcNow < assignmentDeadline)
                    {
                        consumer.Consume(TimeSpan.FromMilliseconds(100));
                    }

                    List<TopicPartition> responseAssignments = consumer.Assignment
                        .Where(partition => partition.Topic == responseTopic)
                        .ToList();
                    if (responseAssignments.Count == 0)
                    {
                        throw new TimeoutException(
                            $"Kafka consumer group '{consumerGroup}' was not assigned response topic " +
                            $"'{responseTopic}' within {timeoutSeconds} seconds. Subscription: " +
                            $"'{string.Join(",", consumer.Subscription ?? new List<string>())}'; " +
                            $"broker: '{_nostify.KafkaUrl}'.");
                    }

                    var startingOffsets = new List<TopicPartitionOffset>();
                    foreach (TopicPartition partition in responseAssignments)
                    {
                        TimeSpan remaining = assignmentDeadline - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero)
                        {
                            throw new TimeoutException(
                                $"Kafka consumer group '{consumerGroup}' could not establish the " +
                                $"starting offset for response topic '{responseTopic}' within " +
                                $"{timeoutSeconds} seconds; broker: '{_nostify.KafkaUrl}'.");
                        }

                        WatermarkOffsets watermarks = consumer.QueryWatermarkOffsets(
                            partition,
                            remaining);
                        startingOffsets.Add(new TopicPartitionOffset(
                            partition,
                            watermarks.High));
                    }

                    // Replace the subscription-derived assignment with explicit
                    // offsets. A Seek can still be superseded by deferred group
                    // offset initialization; Assign makes the pre-publish boundary
                    // authoritative for every response partition.
                    consumer.Assign(startingOffsets);
                }

                // Generate correlation ID for this request
                var correlationId = Guid.NewGuid().ToString();

                // Produce the request
                var request = new AsyncEventRequest
                {
                    topic = requestor.TopicName,
                    responseTopic = responseTopic,
                    subtopic = "",
                    aggregateRootIds = foreignIds,
                    pointInTime = _pointInTime,
                    correlationId = correlationId
                };
                var requestJson = JsonConvert.SerializeObject(request);
                await _nostify.KafkaProducer.ProduceAsync(requestor.TopicName, new Message<string, string> { Value = requestJson });

                // Consume responses until complete or timeout
                var accumulatedEvents = new List<Event>();
                var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                bool complete = false;

                while (!complete && DateTime.UtcNow < deadline)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    var consumeResult = consumer.Consume(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1));
                    if (consumeResult == null) continue;

                    // Try to deserialize as a response
                    AsyncEventRequestResponse? response;
                    try
                    {
                        response = JsonConvert.DeserializeObject<AsyncEventRequestResponse>(consumeResult.Message.Value);
                    }
                    catch
                    {
                        // Not a response message (could be a request from another service), skip
                        continue;
                    }

                    // Check if this response matches our correlation ID
                    if (response?.correlationId != correlationId) continue;

                    // Accumulate events
                    if (response.events != null)
                    {
                        accumulatedEvents.AddRange(response.events);
                    }

                    // Reset deadline on each matching message
                    deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

                    if (response.complete)
                    {
                        complete = true;
                    }
                }

                // Map accumulated events back to projections
                if (accumulatedEvents.Count != 0)
                {
                    var eventsByAggRoot = accumulatedEvents.ToLookup(e => e.aggregateRootId);

                    var mappedEvents = (
                        from p in projections
                        from f in allSelectors
                        let foreignId = f(p)
                        where foreignId.HasValue
                        let eventList = eventsByAggRoot[foreignId!.Value].OrderBy(e => e.timestamp).ToList()
                        where eventList.Count != 0
                        select new ExternalDataEvent(p.id, eventList)
                    ).ToList();

                    result.AddRange(mappedEvents);
                }
            }
        }
        finally
        {
            try
            {
                consumer.Close();
            }
            catch (Exception ex)
            {
                if (_nostify.Logger?.IsEnabled(LogLevel.Warning) == true)
                {
                    LogKafkaConsumerCloseFailure(_nostify.Logger, ex);
                }
            }
            finally
            {
                consumer.Dispose();
            }
        }
        return result;
    }
}
