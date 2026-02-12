using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class GetAll_ProjectionName_s
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    private readonly ILogger<GetAll_ProjectionName_s> _logger;
    public GetAll_ProjectionName_s(HttpClient httpClient, INostify nostify, ILogger<GetAll_ProjectionName_s> logger)
    {
        this._client = httpClient;
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(GetAll_ProjectionName_s))]
    public async Task<List<_ProjectionName_>> Run(
        [HttpTrigger("get", Route = "_ProjectionName_")] HttpRequestData req,
        FunctionContext context)
    {
        Guid tenantId = Guid.Empty; // You can replace this with actual partition key retrieval logic
        Container projectionContainer = await _nostify.GetProjectionContainerAsync<_ProjectionName_>();
        List<_ProjectionName_> allList = await projectionContainer
                            .FilteredQuery<_ProjectionName_>(tenantId)
                            .ReadAllAsync();


        return allList;
    }

}