using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ServiceName__Service;

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
        [HttpTrigger("delete", Route = "_ReplaceMe_/{aggregateId}")] HttpRequestData req,
        Guid aggregateId,
        ILogger log)
    {
        Event pe = new Event(_ReplaceMe_Command.Delete, aggregateId, null);
        await _nostify.PersistEventAsync(pe);

        return aggregateId;
    }
}

