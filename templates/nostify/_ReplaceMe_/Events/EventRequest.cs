
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class EventRequest
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public EventRequest(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(EventRequest))]
    public async Task<List<Event>> Run(
        [HttpTrigger("post", Route = "EventRequest/{pointInTime:datetime?}")] HttpRequestData req,
        [FromBody] List<Guid> aggregateRootIds,
        DateTime? pointInTime,
        FunctionContext context,
        ILogger log)
    {
        Container eventStore = await _nostify.GetEventStoreContainerAsync();
        
        var eventsQuery = eventStore
            .GetItemLinqQueryable<Event>()
            .Where(x => aggregateRootIds.Contains(x.aggregateRootId));

        // Filter by pointInTime if provided
        if (pointInTime.HasValue)
        {
            eventsQuery = eventsQuery.Where(e => e.timestamp <= pointInTime.Value);
        }

        List<Event> allEvents = await eventsQuery
            .OrderBy(e => e.timestamp)
            .ReadAllAsync();

        return allEvents;
    }
}

