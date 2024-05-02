using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class Rehydrate_ReplaceMe_
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public Rehydrate_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Rehydrate_ReplaceMe_))]
    public async Task<_ReplaceMe_> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Rehydrate_ReplaceMe_/{aggregateId:guid}/{datetime:datetime?}")] HttpRequestData req,
        Guid aggregateId,
        DateTime? dateTime,
        ILogger log)
    {
        _ReplaceMe_ retObj = await _nostify.RehydrateAsync<_ReplaceMe_>(aggregateId, dateTime);
                            
        return retObj;
    }
}

