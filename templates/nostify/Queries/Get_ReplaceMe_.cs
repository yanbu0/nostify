using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class GetAccount
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public GetAccount(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function("Get_ReplaceMe_")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_ReplaceMe_/{aggregateId}")] HttpRequestData req,
        string aggregateId,
        ILogger log)
    {
        Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync();
        _ReplaceMe_ retObj = await currentStateContainer
                            .GetItemLinqQueryable<_ReplaceMe_>()
                            .Where(x => x.id == Guid.Parse(aggregateId))
                            .FirstOrDefaultAsync();
                            
        return new OkObjectResult(retObj);
    }
}

