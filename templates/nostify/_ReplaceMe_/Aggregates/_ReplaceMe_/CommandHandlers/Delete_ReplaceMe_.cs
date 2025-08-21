
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class Delete_ReplaceMe_
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public Delete_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Delete_ReplaceMe_))]
    public async Task<Guid> Run(
        [HttpTrigger("delete", Route = "_ReplaceMe_/{aggregateId:guid}")] HttpRequestData req,
        Guid aggregateId,
        ILogger log)
    {
        Event pe = (Event)EventBuilder.Create<_ReplaceMe_>(_ReplaceMe_Command.Delete, aggregateId, new { id = aggregateId });
        await _nostify.PersistEventAsync(pe);

        return aggregateId;
    }
}

