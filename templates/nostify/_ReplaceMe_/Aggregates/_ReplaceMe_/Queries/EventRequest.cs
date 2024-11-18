
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
        [FromBody] List<Guid> aggregateRootIds,
        FunctionContext context,
        ILogger log)
    {
        Container eventStore = await _nostify.GetEventStoreContainerAsync();
        List<Event> allEvents = await eventStore
                            .GetItemLinqQueryable<Event>()
                            .Where(x => aggregateRootIds.Contains(x.aggregateRootId))
                            .ReadAllAsync();

        return allEvents;
    }
}

