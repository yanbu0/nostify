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
    public GetAll_ProjectionName_s(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(GetAll_ProjectionName_s))]
    public async Task<List<_ProjectionName_>> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_ProjectionName_")] HttpRequestData req,
        ILogger log)
    {
        Container projectionContainer = await _nostify.GetProjectionContainerAsync<_ProjectionName_>();
        List<_ProjectionName_> allList = await projectionContainer
                            .GetItemLinqQueryable<_ProjectionName_>()
                            .ReadAllAsync();


        return allList;
    }

}