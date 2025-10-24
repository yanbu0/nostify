using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace TestAggregate_Service;

public class GetAllTestAggregates
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public GetAllTestAggregates(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(GetAllTestAggregates))]
    public async Task<List<TestAggregate>> Run(
        [HttpTrigger("get", Route = "TestAggregate")] HttpRequestData req,
        FunctionContext context,
        ILogger log)
    {
        Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync<TestAggregate>();
        List<TestAggregate> allList = await currentStateContainer
                            .GetItemLinqQueryable<TestAggregate>()
                            .ReadAllAsync();


        return allList;
    }
}

