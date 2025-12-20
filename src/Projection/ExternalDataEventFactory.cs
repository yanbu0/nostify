

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace nostify;

public class ExternalDataEventFactory<P> where P : IProjection, IUniquelyIdentifiable
{
    private readonly HttpClient? _httpClient;
    private readonly INostify _nostify;
    private readonly IQueryExecutor _queryExecutor;
    private List<Guid> _sameServiceIds = new List<Guid>();
    private List<Func<P, Guid>> _foreignKeySelectors = new List<Func<P, Guid>>();
    private List<Func<P, List<Guid>>> _foreignKeyListSelectors = new List<Func<P, List<Guid>>>();
    private EventRequester<P>[] _eventRequestors = new EventRequester<P>[0];
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

        if (_httpClient == null)
        {
            return result;
        }

        // Handle external service IDs
        // Use GetMultiServiceEventsAsync to get events from other services
        var externalEvents = await ExternalDataEvent.GetMultiServiceEventsAsync<P>(_httpClient!,
            this._projectionsToInit,
            this._pointInTime,
            this._eventRequestors);

        result.AddRange(externalEvents);

        return result; 
    }
}