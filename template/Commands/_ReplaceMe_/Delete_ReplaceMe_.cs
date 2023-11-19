using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class Delete_ReplaceMe_
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public Delete_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Delete_ReplaceMe_))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "_ReplaceMe_/{aggregateId}")] HttpRequestData req,
        string aggregateId,
        ILogger log)
    {
        Guid aggRootId = aggregateId.ToGuid();
        PersistedEvent pe = new PersistedEvent(_ReplaceMe_Command.Delete, aggRootId, null);
        await _nostify.PersistAsync(pe);

        return new OkObjectResult(aggregateId);
    }
}

