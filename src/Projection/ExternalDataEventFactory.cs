

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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
    private List<Func<P, List<Guid>>> _foreignKeyListSelectors = new List<Func<P, List<Guid>>>();
    private List<Func<P, Guid>> _dependantIdSelectors = new List<Func<P, Guid>>();
    private List<Func<P, List<Guid>>> _dependantListIdSelectors = new List<Func<P, List<Guid>>>();
    private EventRequester<P>[] _eventRequestors = new EventRequester<P>[0];
    private EventRequester<P>[] _dependantEventRequestors = new EventRequester<P>[0];
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

    public void WithSameServiceIdSelectors(params Func<P, Guid>[] selectors)
    {
        _foreignKeySelectors.AddRange(selectors);
    }

    public void WithSameServiceListIdSelectors(params Func<P, List<Guid>>[] selectors)
    {
        _foreignKeyListSelectors.AddRange(selectors);
    }

    /// <summary>
    /// Adds selectors for IDs that depend on values populated by events from the primary selectors.
    /// These selectors are evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// </summary>
    /// <param name="selectors">Functions that extract dependent foreign key IDs from a projection after initial events are applied</param>
    public void WithSameServiceDependantIdSelectors(params Func<P, Guid>[] selectors)
    {
        _dependantIdSelectors.AddRange(selectors);
    }

    /// <summary>
    /// Adds selectors for lists of IDs that depend on values populated by events from the primary selectors.
    /// These selectors are evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// </summary>
    /// <param name="selectors">Functions that extract lists of dependent foreign key IDs from a projection after initial events are applied</param>
    public void WithSameServiceDependantListIdSelectors(params Func<P, List<Guid>>[] selectors)
    {
        _dependantListIdSelectors.AddRange(selectors);
    }

    public void AddEventRequestors(params EventRequester<P>[] eventRequestors)
    {
        if (_httpClient == null)
        {
            throw new InvalidOperationException("HttpClient is not provided. Cannot add external event requestors.");
        }
        this._eventRequestors = this._eventRequestors.Concat(eventRequestors).ToArray();
    }

    public void WithEventRequestor(string serviceUrl, params Func<P, Guid?>[] foreignIdSelectors)
    {
        if (_httpClient == null)
        {
            throw new InvalidOperationException("HttpClient is not provided. Cannot add external event requestors.");
        }
        this._eventRequestors = this._eventRequestors.Append(new EventRequester<P>(serviceUrl, foreignIdSelectors)).ToArray();
    }

    /// <summary>
    /// Adds event requestors for external services that depend on values populated by events from the primary selectors.
    /// These requestors are evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// </summary>
    /// <param name="eventRequestors">Event requestors for external services with dependent ID selectors</param>
    public void AddDependantEventRequestors(params EventRequester<P>[] eventRequestors)
    {
        if (_httpClient == null)
        {
            throw new InvalidOperationException("HttpClient is not provided. Cannot add external event requestors.");
        }
        this._dependantEventRequestors = this._dependantEventRequestors.Concat(eventRequestors).ToArray();
    }

    /// <summary>
    /// Adds an event requestor for an external service that depends on values populated by events from the primary selectors.
    /// This requestor is evaluated after the first round of events are applied to projections,
    /// allowing you to fetch events for IDs that weren't known until the first events were processed.
    /// </summary>
    /// <param name="serviceUrl">The URL of the external service's event endpoint</param>
    /// <param name="foreignIdSelectors">Functions that extract dependent foreign key IDs from a projection after initial events are applied</param>
    public void WithDependantEventRequestor(string serviceUrl, params Func<P, Guid?>[] foreignIdSelectors)
    {
        if (_httpClient == null)
        {
            throw new InvalidOperationException("HttpClient is not provided. Cannot add external event requestors.");
        }
        this._dependantEventRequestors = this._dependantEventRequestors.Append(new EventRequester<P>(serviceUrl, foreignIdSelectors)).ToArray();
    }

    public async Task<List<ExternalDataEvent>> GetEventsAsync()
    {
        var result = new List<ExternalDataEvent>();

        // Handle same service IDs using ExternalDataEvent.GetEventsAsync
        Container eventStoreContainer = await _nostify.GetEventStoreContainerAsync();
        
        // Get events for single-ID selectors
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

        // Get events for list-ID selectors
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

        // Handle dependent selectors - these require applying ALL initial events first to get the IDs
        // This runs after both local and external events have been collected
        if (_dependantIdSelectors.Any() || _dependantListIdSelectors.Any())
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
}