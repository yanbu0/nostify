
using _ReplaceMe__Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using nostify;

namespace _ReplaceMe_Service;

public class _ProjectionName_Init
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    private readonly ILogger<_ProjectionName_Init> _logger;
    public _ProjectionName_Init(HttpClient httpClient, INostify nostify, ILogger<_ProjectionName_Init> logger)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(_ProjectionName_Init))]
    public async Task<IActionResult> Run(
        [HttpTrigger("post", Route = "_ProjectionName_Init")] HttpRequestData req)
    {
        await _nostify.InitContainerAsync<_ProjectionName_,_ReplaceMe_>();
        return new OkResult();
    }
}