using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace TestAggregate_Service;

public class GetTestAggregate
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public GetTestAggregate(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(GetTestAggregate))]
    public async Task<TestAggregate> Run(
        [HttpTrigger("get", Route = "TestAggregate/{aggregateId:guid}")] HttpRequestData req,
        FunctionContext context,
        Guid aggregateId,
        ILogger log)
    {
        Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync<TestAggregate>();
        TestAggregate retObj = await currentStateContainer
                            .GetItemLinqQueryable<TestAggregate>()
                            .Where(x => x.id == aggregateId)
                            .FirstOrDefaultAsync();
                            
        return retObj;
    }
}

