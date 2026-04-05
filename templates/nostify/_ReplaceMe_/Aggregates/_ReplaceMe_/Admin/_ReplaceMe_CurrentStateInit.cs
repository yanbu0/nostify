using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using nostify;

namespace _ReplaceMe__Service;

public class _ReplaceMe_CurrentStateInit
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    private readonly ILogger<_ReplaceMe_CurrentStateInit> _logger;
    public _ReplaceMe_CurrentStateInit(HttpClient httpClient, INostify nostify, ILogger<_ReplaceMe_CurrentStateInit> logger)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(_ReplaceMe_CurrentStateInit))]
    public async Task<IActionResult> Run(
        [HttpTrigger("post", Route = "_ReplaceMe_CurrentStateInit")] HttpRequestData req)
    {
        await _nostify.RebuildCurrentStateContainerAsync<_ReplaceMe_>();
        return new OkResult();
    }
}