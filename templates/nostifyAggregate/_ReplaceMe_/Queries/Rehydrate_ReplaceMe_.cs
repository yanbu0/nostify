using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ServiceName__Service;

public class Rehydrate_ReplaceMe_
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    private readonly ILogger<Rehydrate_ReplaceMe_> _logger;
    public Rehydrate_ReplaceMe_(HttpClient httpClient, INostify nostify, ILogger<Rehydrate_ReplaceMe_> logger)
    {
        this._client = httpClient;
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(Rehydrate_ReplaceMe_))]
    public async Task<_ReplaceMe_> Run(
        [HttpTrigger("get", Route = "Rehydrate_ReplaceMe_/{aggregateId:guid}/{datetime:datetime?}")] HttpRequestData req,
        Guid aggregateId,
        DateTime? dateTime)
    {
        _ReplaceMe_ retObj = await _nostify.RehydrateAsync<_ReplaceMe_>(aggregateId, dateTime);
                            
        return retObj;
    }
}

