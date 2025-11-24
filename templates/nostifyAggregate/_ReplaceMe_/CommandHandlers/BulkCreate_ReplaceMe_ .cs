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

public class BulkCreate_ReplaceMe_
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public BulkCreate_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(BulkCreate_ReplaceMe_))]
    public async Task<int> Run(
        [HttpTrigger("post", Route = "_ReplaceMe_/BulkCreate")] HttpRequestData req,
        FunctionContext context,
        ILogger log)
    {
        Guid userId = Guid.Empty; // You can replace this with actual user ID retrieval logic
        Guid tenantId = Guid.Empty; // You can replace this with actual partition key retrieval logic

        return await DefaultCommandHandler.HandleBulkCreate<_ReplaceMe_>(_ReplaceMe_Command.Create, req, userId, tenantId);
    }
}

