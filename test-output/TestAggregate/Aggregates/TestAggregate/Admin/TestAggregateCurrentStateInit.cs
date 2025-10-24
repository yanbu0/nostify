using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using nostify;

namespace TestAggregate_Service;

public class TestAggregateCurrentStateInit
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public TestAggregateCurrentStateInit(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(TestAggregateCurrentStateInit))]
    public async Task<IActionResult> Run(
        [HttpTrigger("post", Route = "TestAggregateCurrentStateInit")] HttpRequestData req,
        ILogger log)
    {
        await _nostify.RebuildCurrentStateContainerAsync<TestAggregate>();
        return new OkResult();
    }
}