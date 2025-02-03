

using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Azure.Cosmos;
using nostify;
using Newtonsoft.Json;

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
    /// Gets the events needed to initialize a list of projections
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="eventStore">The Event Store Container</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="foreignIdSelectors">Functions to get the foreign id for the aggregates required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(Container eventStore, List<TProjection> projectionsToInit, params Func<TProjection, Guid?>[] foreignIdSelectors)
    where TProjection : IUniquelyIdentifiable
    {
        var foreignIds = 
            from p in projectionsToInit
            from f in foreignIdSelectors
            let foreignId = f(p)
            where foreignId.HasValue
            select foreignId!.Value;

        var events = (await eventStore
            .GetItemLinqQueryable<Event>()
            .Where(e => foreignIds.Contains(e.aggregateRootId))
            .OrderBy(e => e.timestamp)
            .ReadAllAsync())
            .ToLookup(e => e.aggregateRootId);

        var result = (
            from p in projectionsToInit
            from f in foreignIdSelectors
            let foreignId = f(p)
            where foreignId.HasValue
            select new ExternalDataEvent(p.id, events[foreignId!.Value].ToList())
        ).ToList();

        return result;
    }

    /// <summary>
    /// Gets the events needed to initialize a list of projections from an external service
    /// </summary>
    /// <typeparam name="TProjection">Type of the projection</typeparam>
    /// <param name="httpClient">An HTTP client to make the service call</param>
    /// <param name="url">The URL of the service endpoint</param>
    /// <param name="projectionsToInit">List of projections to initialize</param>
    /// <param name="foreignIdSelectors">Function to get the foreign id for the aggregate required to populate one or more fields in the projection</param>
    /// <returns>List of ExternalDataEvent</returns>
    public async static Task<List<ExternalDataEvent>> GetEventsAsync<TProjection>(HttpClient httpClient, string url, List<TProjection> projectionsToInit, params Func<TProjection, Guid?>[] foreignIdSelectors)
        where TProjection : IUniquelyIdentifiable
    {
        if (httpClient == null || string.IsNullOrEmpty(url))
        {
            return [];
        }
        
        var foreignIds = 
            from p in projectionsToInit
            from f in foreignIdSelectors
            let foreignId = f(p)
            where foreignId.HasValue
            select foreignId!.Value;

        if (!foreignIds.Any())
        {
            return [];
        }

        var foreignIdsJson = JsonConvert.SerializeObject(foreignIds);
        var response = await httpClient.PostAsync(url, new StringContent(foreignIdsJson, System.Text.Encoding.UTF8, "application/json"));
        if (response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync();
            var events = (JsonConvert.DeserializeObject<List<Event>>(responseText) ?? []).ToLookup(e => e.aggregateRootId);
            var result = (
                from p in projectionsToInit
                from f in foreignIdSelectors
                let foreignId = f(p)
                where foreignId.HasValue
                select new ExternalDataEvent(p.id, events[foreignId!.Value].OrderBy(e => e.timestamp).ToList())
            ).ToList();

            return result;
        }

        return [];
    }
}