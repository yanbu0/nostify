using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using nostify;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;

namespace _ServiceName__Service;

public class BulkDelete_ReplaceMe_
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    private readonly ILogger<BulkDelete_ReplaceMe_> _logger;
    public BulkDelete_ReplaceMe_(HttpClient httpClient, INostify nostify, ILogger<BulkDelete_ReplaceMe_> logger)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(BulkDelete_ReplaceMe_))]
    public async Task<int> Run(
        [HttpTrigger("delete", Route = "_ReplaceMe_/BulkDelete")] HttpRequestData req,
        FunctionContext context)
    {
        Guid userId = Guid.Empty; // You can replace this with actual user ID retrieval logic
        Guid tenantId = Guid.Empty; // You can replace this with actual partition key retrieval logic

        return await DefaultCommandHandler.HandleBulkDelete<_ReplaceMe_>(_nostify, _ReplaceMe_Command.Delete, req, userId, tenantId);
    }
}

