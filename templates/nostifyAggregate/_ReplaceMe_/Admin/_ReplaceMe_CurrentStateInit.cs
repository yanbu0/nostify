
using _ReplaceMe__Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using nostify;

namespace _ReplaceMe_Service;

public class _ReplaceMe_CurrentStateInit
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public _ReplaceMe_CurrentStateInit(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(_ReplaceMe_CurrentStateInit))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "_ReplaceMe_CurrentStateInit")] HttpRequestData req,
        ILogger log)
    {
        await _nostify.RebuildCurrentStateContainerAsync<_ReplaceMe_>();
        return new OkResult();
    }
}