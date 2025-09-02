
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
        [HttpTrigger("post", Route = "EventRequest")] HttpRequestData req,
        [FromBody] EventRequestData requestData,
        FunctionContext context,
        ILogger log)
    {
        Container eventStore = await _nostify.GetEventStoreContainerAsync();
        
        var eventsQuery = eventStore
            .GetItemLinqQueryable<Event>()
            .Where(x => requestData.ForeignIds.Contains(x.aggregateRootId));

        // Filter by pointInTime if provided
        if (requestData.PointInTime.HasValue)
        {
            eventsQuery = eventsQuery.Where(e => e.timestamp <= requestData.PointInTime.Value);
        }

        List<Event> allEvents = await eventsQuery
            .OrderBy(e => e.timestamp)
            .ReadAllAsync();

        return allEvents;
    }
}

