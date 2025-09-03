

using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Azure.Cosmos;
using nostify;
using Newtonsoft.Json;
using System.Net.Http;

namespace nostify;

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

    /// <summary>
    /// Gets the events needed to initialize a list of projections using a list of foreign ID selector functions that return lists of Guid?.
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="eventStore">The Event Store Container</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="pointInTime">Point in time to query events up to. If null, queries all events.</param>
    /// <param name="foreignIdSelectorsList">Functions to get lists of foreign ids for the aggregates required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(Container eventStore, List<TProjection> projectionsToInit, DateTime? pointInTime = null, params Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
        where TProjection : IUniquelyIdentifiable
    {
        // transform the foreignIdSelectors using a helper function
        Func<TProjection, Guid?>[] foreignIdSelectors = TransformForeignIdSelectors(projectionsToInit, foreignIdSelectorsList);
        return await GetEventsAsync(eventStore, projectionsToInit, pointInTime, foreignIdSelectors);
    }

    /// <summary>
    /// Helper method to transform foreign ID selectors into an array of Func<TProjection, Guid?>
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="foreignIdSelectorsList">Functions to get lists of foreign ids for the aggregates</param>
    /// <returns>Array of Func<TProjection, Guid?></returns>
    private static Func<TProjection, Guid?>[] TransformForeignIdSelectors<TProjection>(
        List<TProjection> projectionsToInit,
        Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
        where TProjection : IUniquelyIdentifiable
    {
        return projectionsToInit
            .SelectMany(p => foreignIdSelectorsList.SelectMany(selector => selector(p)))
            .Select(guid => new Func<TProjection, Guid?>(_ => guid))
            .ToArray();
    }

    /// <summary>
    /// Gets the events needed to initialize a list of projections from an external service using a list of foreign ID selector functions that return lists of Guid?.
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="httpClient">An HTTP client to make the service call. Must not be null.</param>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="pointInTime">Point in time to query events up to. If null, queries all events.</param>
    /// <param name="foreignIdSelectorsList">Functions to get lists of foreign ids for the aggregates required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(HttpClient httpClient, string url, List<TProjection> projectionsToInit, DateTime? pointInTime = null, params Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
        where TProjection : IUniquelyIdentifiable
    {
        // transform the foreignIdSelectors to an array of Func<TProjection, Guid?>
        Func<TProjection, Guid?>[] foreignIdSelectors = TransformForeignIdSelectors(projectionsToInit, foreignIdSelectorsList);
        return await GetEventsAsync(httpClient, url, projectionsToInit, pointInTime, foreignIdSelectors);
    }

    /// <summary>
    /// Gets the events needed to initialize a list of projections
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="eventStore">The Event Store Container</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="foreignIdSelectors">Functions to get the foreign id for the aggregates required to populate one or more fields in the projection</param>
    /// <param name="pointInTime">Point in time to query events up to. If null, queries all events.</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(Container eventStore, List<TProjection> projectionsToInit, DateTime? pointInTime = null, params Func<TProjection, Guid?>[] foreignIdSelectors)
    where TProjection : IUniquelyIdentifiable
    {
        var foreignIds =
            from p in projectionsToInit
            from f in foreignIdSelectors
            let foreignId = f(p)
            where foreignId.HasValue
            select foreignId!.Value;

        var eventsQuery = eventStore
            .GetItemLinqQueryable<Event>()
            .Where(e => foreignIds.Contains(e.aggregateRootId));

        // Filter by pointInTime if provided
        if (pointInTime.HasValue)
        {
            eventsQuery = eventsQuery.Where(e => e.timestamp <= pointInTime.Value);
        }

        var events = (await eventsQuery
            .OrderBy(e => e.timestamp)
            .ReadAllAsync())
            .ToLookup(e => e.aggregateRootId);

        var result = (
            from p in projectionsToInit
            from f in foreignIdSelectors
            let foreignId = f(p)
            where foreignId.HasValue
            let eventList = events[foreignId!.Value].ToList()
            where eventList.Any()
            select new ExternalDataEvent(p.id, eventList)
        ).ToList();

        return result;
    }

    /// <summary>
    /// Gets the events needed to initialize a list of projections from an external service
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="httpClient">An HTTP client to make the service call. Must not be null.</param>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="pointInTime">Point in time to query events up to. If null, queries all events.</param>
    /// <param name="foreignIdSelectors">Function to get the foreign id for the aggregate required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(HttpClient httpClient, string url, List<TProjection> projectionsToInit, DateTime? pointInTime = null, params Func<TProjection, Guid?>[] foreignIdSelectors)
        where TProjection : IUniquelyIdentifiable
    {
        if (httpClient == null || string.IsNullOrEmpty(url))
        {
            throw new NostifyException("HttpClient and URL of EventRequest endpoint are required to get events from an external service");
        }

        //Empty result set by default
        List<ExternalDataEvent> result = new List<ExternalDataEvent>();

        var foreignIds =
            from p in projectionsToInit
            from f in foreignIdSelectors
            let foreignId = f(p)
            where foreignId.HasValue
            select foreignId!.Value;

        //Don't run query if no ids present
        if (foreignIds.Any())
        {
            var requestJson = JsonConvert.SerializeObject(foreignIds.Distinct().ToList());
            var urlWithPath = url;
            if (pointInTime.HasValue)
            {
                urlWithPath = $"{url}/{pointInTime.Value:O}";
            }
            var response = await httpClient.PostAsync(urlWithPath, new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode)
            {
                throw new NostifyException($"{response.StatusCode} Error getting events from external service: {response.ReasonPhrase} || {response.Content}");
            }
            else
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var events = (JsonConvert.DeserializeObject<List<Event>>(responseText) ?? []).ToLookup(e => e.aggregateRootId);
                result = (
                    from p in projectionsToInit
                    from f in foreignIdSelectors
                    let foreignId = f(p)
                    where foreignId.HasValue
                    let eventList = events[foreignId!.Value].OrderBy(e => e.timestamp).ToList()
                    where eventList.Any()
                    select new ExternalDataEvent(p.id, eventList)
                ).ToList();
            }
        }

        return result;
    }

    /// Gets the events needed to initialize a list of projections (backward compatibility overload)
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="eventStore">The Event Store Container</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="foreignIdSelectors">Functions to get the foreign id for the aggregates required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(Container eventStore, List<TProjection> projectionsToInit, params Func<TProjection, Guid?>[] foreignIdSelectors)
    where TProjection : IUniquelyIdentifiable
    {
        return await GetEventsAsync(eventStore, projectionsToInit, null, foreignIdSelectors);
    }

    /// <summary>
    /// Gets the events needed to initialize a list of projections from an external service (backward compatibility overload)
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="httpClient">An HTTP client to make the service call. Must not be null.</param>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="foreignIdSelectors">Function to get the foreign id for the aggregate required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(HttpClient httpClient, string url, List<TProjection> projectionsToInit, params Func<TProjection, Guid?>[] foreignIdSelectors)
        where TProjection : IUniquelyIdentifiable
    {
        return await GetEventsAsync(httpClient, url, projectionsToInit, null, foreignIdSelectors);
    }

    /// <summary>
    /// Gets the events needed to initialize a list of projections using a list of foreign ID selector functions that return lists of Guid? (backward compatibility overload)
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="eventStore">The Event Store Container</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="foreignIdSelectorsList">Functions to get lists of foreign ids for the aggregates required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(Container eventStore, List<TProjection> projectionsToInit, params Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
        where TProjection : IUniquelyIdentifiable
    {
        return await GetEventsAsync(eventStore, projectionsToInit, null, foreignIdSelectorsList);
    }

    /// <summary>
    /// Gets the events needed to initialize a list of projections from an external service using a list of foreign ID selector functions that return lists of Guid? (backward compatibility overload)
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="httpClient">An HTTP client to make the service call. Must not be null.</param>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="foreignIdSelectorsList">Functions to get lists of foreign ids for the aggregates required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(HttpClient httpClient, string url, List<TProjection> projectionsToInit, params Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
        where TProjection : IUniquelyIdentifiable
    {
        return await GetEventsAsync(httpClient, url, projectionsToInit, null, foreignIdSelectorsList);
    }

    /// <summary>
    /// Gets events from multiple external services in parallel
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="httpClient">An HTTP client to make the service calls. Must not be null.</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="pointInTime">Point in time to query events up to. If null, queries all events.</param>
    /// <param name="eventRequests">Array of EventRequester objects containing URLs and foreign ID selectors</param>
    /// <returns>Combined list of ExternalDataEvent from all services</returns>
    public async static Task<List<ExternalDataEvent>> GetMultiServiceEventsAsync<TProjection>(HttpClient httpClient, List<TProjection> projectionsToInit, DateTime? pointInTime = null, params EventRequester<TProjection>[] eventRequests)
        where TProjection : IUniquelyIdentifiable
    {
        if (httpClient == null)
        {
            throw new NostifyException("HttpClient is required to get events from external services");
        }

        if (eventRequests == null || eventRequests.Length == 0)
        {
            return new List<ExternalDataEvent>();
        }

        // Create tasks for all service calls to run in parallel
        var tasks = eventRequests.Select(request =>
        {
            // Use the new method to get all foreign ID selectors if available, otherwise use the existing ones
            var allSelectors = request.ListSelectors.Any() 
                ? request.GetAllForeignIdSelectors(projectionsToInit)
                : request.ForeignIdSelectors;
            return GetEventsAsync(httpClient, request.Url, projectionsToInit, pointInTime, allSelectors);
        }).ToArray();

        // Wait for all tasks to complete
        var results = await Task.WhenAll(tasks);

        // Combine all results into a single list
        var combinedResults = new List<ExternalDataEvent>();
        foreach (var result in results)
        {
            combinedResults.AddRange(result);
        }

        return combinedResults;
    }

    /// <summary>
    /// Gets events from multiple external services in parallel (backward compatibility overload)
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="httpClient">An HTTP client to make the service calls. Must not be null.</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="eventRequests">Array of EventRequester objects containing URLs and foreign ID selectors</param>
    /// <returns>Combined list of ExternalDataEvent from all services</returns>
    public async static Task<List<ExternalDataEvent>> GetMultiServiceEventsAsync<TProjection>(HttpClient httpClient, List<TProjection> projectionsToInit, params EventRequester<TProjection>[] eventRequests)
        where TProjection : IUniquelyIdentifiable
    {
        return await GetMultiServiceEventsAsync(httpClient, projectionsToInit, null, eventRequests);
    }
}

/// <summary>
/// Represents a request for events from an external service
/// </summary>
/// <typeparam name="TProjection">Type of the projection</typeparam>
public class EventRequester<TProjection> where TProjection : IUniquelyIdentifiable
{
    /// <summary>
    /// The URL of the service EventRequest endpoint
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Functions to get the foreign id for the aggregates required to populate one or more fields in the projection
    /// </summary>
    public Func<TProjection, Guid?>[] ForeignIdSelectors { get; }

    /// <summary>
    /// Constructor for EventRequester
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="foreignIdSelectors">Functions to get the foreign id for the aggregates required to populate one or more fields in the projection</param>
    public EventRequester(string url, params Func<TProjection, Guid?>[] foreignIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        ForeignIdSelectors = foreignIdSelectors ?? Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Constructor for EventRequester that accepts a mix of single and list foreign ID selectors
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id for the aggregates</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids for the aggregates</param>
    public EventRequester(string url, Func<TProjection, Guid?>[] singleIdSelectors, Func<TProjection, List<Guid?>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        SingleSelectors = singleIdSelectors ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        
        // We'll compute ForeignIdSelectors when needed, but for backward compatibility,
        // we'll store the single selectors directly
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Constructor for EventRequester that accepts list foreign ID selectors
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids for the aggregates</param>
    public EventRequester(string url, params Func<TProjection, List<Guid?>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        SingleSelectors = Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        
        // For backward compatibility, ForeignIdSelectors will be empty initially
        // They will be expanded when GetAllForeignIdSelectors is called
        ForeignIdSelectors = Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Single ID selectors
    /// </summary>
    public Func<TProjection, Guid?>[] SingleSelectors { get; private set; } = Array.Empty<Func<TProjection, Guid?>>();

    /// <summary>
    /// List ID selectors for cases where we need to expand lists
    /// </summary>
    public Func<TProjection, List<Guid?>>[] ListSelectors { get; private set; } = Array.Empty<Func<TProjection, List<Guid?>>>();

    /// <summary>
    /// Gets all foreign ID selectors as Func&lt;TProjection, Guid?&gt;[] by expanding list selectors
    /// </summary>
    /// <param name="projectionsToInit">List of projections to use for expanding list selectors</param>
    /// <returns>Array of all foreign ID selectors</returns>
    public Func<TProjection, Guid?>[] GetAllForeignIdSelectors(List<TProjection> projectionsToInit)
    {
        var allSelectors = new List<Func<TProjection, Guid?>>();
        
        // Add single selectors directly
        allSelectors.AddRange(SingleSelectors);
        
        // Transform list selectors using the same logic as TransformForeignIdSelectors
        if (ListSelectors.Any())
        {
            var expandedSelectors = projectionsToInit
                .SelectMany(p => ListSelectors.SelectMany(selector => selector(p)))
                .Select(guid => new Func<TProjection, Guid?>(_ => guid))
                .ToArray();
            
            allSelectors.AddRange(expandedSelectors);
        }
        
        return allSelectors.ToArray();
    }
}