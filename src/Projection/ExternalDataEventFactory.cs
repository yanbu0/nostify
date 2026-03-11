

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace nostify;

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
    private EventRequester<P>[] _eventRequestors = new EventRequester<P>[0];
    private EventRequester<P>[] _dependantEventRequestors = new EventRequester<P>[0];
    private AsyncEventRequester<P>[] _asyncEventRequestors = new AsyncEventRequester<P>[0];
    private AsyncEventRequester<P>[] _dependantAsyncEventRequestors = new AsyncEventRequester<P>[0];
    private List<P> _projectionsToInit = new List<P>();
    private DateTime? _pointInTime;

    /// <summary>
    /// Creates a new ExternalDataEventFactory
    /// </summary>
    /// <param name="nostify">The INostify instance for accessing the event store</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="httpClient">Optional HTTP client for external service calls</param>
    /// <param name="pointInTime">Optional point in time to query events up to</param>
    /// <param name="queryExecutor">Optional query executor for unit testing. Defaults to CosmosQueryExecutor.</param>
    public ExternalDataEventFactory(INostify nostify, List<P> projectionsToInit, HttpClient? httpClient = null, DateTime? pointInTime = null, IQueryExecutor? queryExecutor = null)
    {
        this._nostify = nostify;
        this._httpClient = httpClient;
        this._projectionsToInit = projectionsToInit;
        this._pointInTime = pointInTime;
        this._queryExecutor = queryExecutor ?? CosmosQueryExecutor.Default;
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

    public async Task<List<ExternalDataEvent>> GetEventsAsync()
    {
        var result = new List<ExternalDataEvent>();

        // Handle same service IDs using ExternalDataEvent.GetEventsAsync
        Container eventStoreContainer = await _nostify.GetEventStoreContainerAsync();
        
        // Get events for single-ID selectors (non-nullable)
        if (_foreignKeySelectors.Any())
        {
            var singleIdEvents = await ExternalDataEvent.GetEventsAsync(
                eventStoreContainer, 
                _projectionsToInit, 
                _queryExecutor,
                _pointInTime, 
                _foreignKeySelectors.ToArray());
            result.AddRange(singleIdEvents);
        }

        // Get events for single-ID selectors (nullable) - nulls filtered by HasValue in ExternalDataEvent
        if (_nullableForeignKeySelectors.Any())
        {
            var nullableSingleIdEvents = await ExternalDataEvent.GetEventsAsync(
                eventStoreContainer, 
                _projectionsToInit, 
                _queryExecutor,
                _pointInTime, 
                _nullableForeignKeySelectors.ToArray());
            result.AddRange(nullableSingleIdEvents);
        }

        // Get events for list-ID selectors (non-nullable)
        if (_foreignKeyListSelectors.Any())
        {
            var listIdEvents = await ExternalDataEvent.GetEventsAsync(
                eventStoreContainer, 
                _projectionsToInit, 
                _queryExecutor,
                _pointInTime, 
                _foreignKeyListSelectors.ToArray());
            result.AddRange(listIdEvents);
        }

        // Get events for list-ID selectors (nullable) - nulls within lists filtered by HasValue in ExternalDataEvent
        if (_nullableForeignKeyListSelectors.Any())
        {
            var nullableListIdEvents = await ExternalDataEvent.GetEventsAsync(
                eventStoreContainer, 
                _projectionsToInit, 
                _queryExecutor,
                _pointInTime, 
                _nullableForeignKeyListSelectors.ToArray());
            result.AddRange(nullableListIdEvents);
        }

        // Handle external service IDs before dependent selectors
        // so that external events can also populate dependent IDs
        if (_httpClient != null)
        {
            var externalEvents = await ExternalDataEvent.GetMultiServiceEventsAsync<P>(_httpClient!,
                this._projectionsToInit,
                this._pointInTime,
                this._eventRequestors);

            result.AddRange(externalEvents);
        }

        // Handle async (Kafka) event requestors
        if (_asyncEventRequestors.Any())
        {
            var asyncEvents = await GetAsyncEventsAsync(_asyncEventRequestors, _projectionsToInit);
            result.AddRange(asyncEvents);
        }

        // Handle dependent selectors - these require applying ALL initial events first to get the IDs
        // This runs after both local and external events have been collected
        if (_dependantIdSelectors.Any() || _dependantListIdSelectors.Any() || _nullableDependantIdSelectors.Any() || _nullableDependantListIdSelectors.Any())
        {
            var dependantEvents = await GetDependantEventsAsync(eventStoreContainer, result);
            result.AddRange(dependantEvents);
        }

        // Handle dependent external event requestors - these also require applying initial events first
        if (_httpClient != null && _dependantEventRequestors.Any())
        {
            var dependantExternalEvents = await GetDependantExternalEventsAsync(result);
            result.AddRange(dependantExternalEvents);
        }

        // Handle dependent async (Kafka) event requestors - these also require applying initial events first
        if (_dependantAsyncEventRequestors.Any())
        {
            var dependantAsyncEvents = await GetDependantAsyncEventsAsync(result);
            result.AddRange(dependantAsyncEvents);
        }

        return result; 
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
            // Extract single IDs
            foreach (var selector in _dependantIdSelectors)
            {
                try
                {
                    var id = selector(projection);
                    if (id != Guid.Empty)
                    {
                        dependantIds.Add(id);
                    }
                }
                catch
                {
                    // Selector threw an exception (e.g., null reference), skip this ID
                }
            }

            // Extract nullable single IDs
            foreach (var selector in _nullableDependantIdSelectors)
            {
                try
                {
                    var id = selector(projection);
                    if (id.HasValue && id.Value != Guid.Empty)
                    {
                        dependantIds.Add(id.Value);
                    }
                }
                catch
                {
                    // Selector threw an exception (e.g., null reference), skip this ID
                }
            }

            // Extract list IDs
            foreach (var selector in _dependantListIdSelectors)
            {
                try
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
                catch
                {
                    // Selector threw an exception (e.g., null reference), skip these IDs
                }
            }

            // Extract nullable list IDs
            foreach (var selector in _nullableDependantListIdSelectors)
            {
                try
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
                catch
                {
                    // Selector threw an exception (e.g., null reference), skip these IDs
                }
            }
        }

        // Remove any IDs we've already fetched events for
        // Get the event aggregateRootIds from initial events (not the ExternalDataEvent.aggregateRootId which is the projection id)
        var existingEventIds = initialEvents.SelectMany(e => e.events.Select(evt => evt.aggregateRootId)).ToHashSet();
        var newIds = dependantIds.Except(existingEventIds).ToList();

        // Only query if there are new IDs to fetch
        if (newIds.Any())
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
                
                // Get IDs from this projection's selectors
                foreach (var selector in _dependantIdSelectors)
                {
                    try
                    {
                        var id = selector(projection);
                        if (id != Guid.Empty && newIds.Contains(id))
                        {
                            projectionDependantIds.Add(id);
                        }
                    }
                    catch { }
                }

                // Get IDs from nullable single selectors
                foreach (var selector in _nullableDependantIdSelectors)
                {
                    try
                    {
                        var id = selector(projection);
                        if (id.HasValue && id.Value != Guid.Empty && newIds.Contains(id.Value))
                        {
                            projectionDependantIds.Add(id.Value);
                        }
                    }
                    catch { }
                }

                foreach (var selector in _dependantListIdSelectors)
                {
                    try
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
                    catch { }
                }

                // Get IDs from nullable list selectors
                foreach (var selector in _nullableDependantListIdSelectors)
                {
                    try
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
                    catch { }
                }

                // Get events for this projection's dependent IDs
                var projectionEvents = events
                    .Where(e => projectionDependantIds.Contains(e.aggregateRootId))
                    .ToList();

                if (projectionEvents.Any())
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

        // Get the consumer group from the projection's containerName
        // P is constrained to IProjection which has static abstract containerName
        var consumerGroup = P.containerName;
        var consumer = _nostify.GetOrCreateKafkaConsumer(consumerGroup);

        // Process each requestor
        foreach (var requestor in requestors)
        {
            // Collect all foreign IDs for this requestor
            var allSelectors = requestor.ListSelectors.Any()
                ? requestor.GetAllForeignIdSelectors(projections)
                : requestor.ForeignIdSelectors;

            var foreignIds = (
                from p in projections
                from f in allSelectors
                let foreignId = f(p)
                where foreignId.HasValue && foreignId.Value != Guid.Empty
                select foreignId!.Value
            ).Distinct().ToList();

            if (!foreignIds.Any())
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
                // Poll briefly to trigger partition assignment
                consumer.Consume(TimeSpan.FromMilliseconds(100));
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
                AsyncEventRequestResponse response;
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
            if (accumulatedEvents.Any())
            {
                var eventsByAggRoot = accumulatedEvents.ToLookup(e => e.aggregateRootId);

                var mappedEvents = (
                    from p in projections
                    from f in allSelectors
                    let foreignId = f(p)
                    where foreignId.HasValue
                    let eventList = eventsByAggRoot[foreignId!.Value].OrderBy(e => e.timestamp).ToList()
                    where eventList.Any()
                    select new ExternalDataEvent(p.id, eventList)
                ).ToList();

                result.AddRange(mappedEvents);
            }
        }

        return result;
    }
}