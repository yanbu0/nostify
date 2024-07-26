using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service;

public class Get_ProjectionName_
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public Get_ProjectionName_(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Get_ProjectionName_))]
    public async Task<_ProjectionName_> Run(
        [HttpTrigger("get", Route = "_ProjectionName_/{aggregateId:guid}")] HttpRequestData req,
        Guid aggregateId,
        FunctionContext context,
        ILogger log)
    {
        Container projectionContainer = await _nostify.GetProjectionContainerAsync<_ProjectionName_>();
        _ProjectionName_ retObj = await projectionContainer
                            .GetItemLinqQueryable<_ProjectionName_>()
                            .Where(x => x.id == aggregateId)
                            .FirstOrDefaultAsync();
                            
        return retObj;
    }
}

