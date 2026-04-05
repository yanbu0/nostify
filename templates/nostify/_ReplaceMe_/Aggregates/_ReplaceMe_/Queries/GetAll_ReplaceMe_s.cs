using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class GetAll_ReplaceMe_s
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    private readonly ILogger<GetAll_ReplaceMe_s> _logger;
    public GetAll_ReplaceMe_s(HttpClient httpClient, INostify nostify, ILogger<GetAll_ReplaceMe_s> logger)
    {
        this._client = httpClient;
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(GetAll_ReplaceMe_s))]
    public async Task<List<_ReplaceMe_>> Run(
        [HttpTrigger("get", Route = "_ReplaceMe_")] HttpRequestData req,
        FunctionContext context)
    {
        Guid tenantId = Guid.Empty; // You can replace this with actual partition key retrieval logic
        Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync<_ReplaceMe_>();
        List<_ReplaceMe_> allList = await currentStateContainer
                            .FilteredQuery<_ReplaceMe_>(tenantId)
                            .ReadAllAsync();


        return allList;
    }
}

