using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace TestAggregate_Service;

public class RehydrateTestAggregate
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public RehydrateTestAggregate(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(RehydrateTestAggregate))]
    public async Task<TestAggregate> Run(
        [HttpTrigger("get", Route = "RehydrateTestAggregate/{aggregateId:guid}/{datetime:datetime?}")] HttpRequestData req,
        Guid aggregateId,
        DateTime? dateTime,
        ILogger log)
    {
        TestAggregate retObj = await _nostify.RehydrateAsync<TestAggregate>(aggregateId, dateTime);
                            
        return retObj;
    }
}

