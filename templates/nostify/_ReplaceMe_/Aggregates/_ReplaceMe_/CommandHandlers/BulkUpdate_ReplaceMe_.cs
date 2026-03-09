using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using nostify;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;

namespace _ReplaceMe__Service;

public class BulkUpdate_ReplaceMe_
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    private readonly ILogger<BulkUpdate_ReplaceMe_> _logger;
    public BulkUpdate_ReplaceMe_(HttpClient httpClient, INostify nostify, ILogger<BulkUpdate_ReplaceMe_> logger)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(BulkUpdate_ReplaceMe_))]
    public async Task<int> Run(
        [HttpTrigger("patch", Route = "_ReplaceMe_/BulkUpdate")] HttpRequestData req,
        FunctionContext context)
    {
        Guid userId = Guid.Empty; // You can replace this with actual user ID retrieval logic
        Guid tenantId = Guid.Empty; // You can replace this with actual partition key retrieval logic

        return await DefaultCommandHandler.HandleBulkUpdate<_ReplaceMe_>(_nostify, _ReplaceMe_Command.Update, req, userId, tenantId);
    }
}

