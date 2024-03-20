using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class Get_ReplaceMe_
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public Get_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Get_ReplaceMe_))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_ReplaceMe_/{aggregateId:guid}")] HttpRequestData req,
        Guid aggregateId,
        ILogger log)
    {
        Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync<_ReplaceMe_>();
        _ReplaceMe_ retObj = await currentStateContainer
                            .GetItemLinqQueryable<_ReplaceMe_>()
                            .Where(x => x.id == aggregateId)
                            .FirstOrDefaultAsync();
                            
        return new OkObjectResult(retObj);
    }
}

