using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ServiceName__Service;

public class Delete_ReplaceMe_
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    private readonly ILogger<Delete_ReplaceMe_> _logger;
    public Delete_ReplaceMe_(HttpClient httpClient, INostify nostify, ILogger<Delete_ReplaceMe_> logger)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(Delete_ReplaceMe_))]
    public async Task<Guid> Run(
        [HttpTrigger("delete", Route = "_ReplaceMe_/{aggregateId:guid}")] HttpRequestData req,
        FunctionContext context,
        Guid aggregateId)
    {
        Guid userId = Guid.Empty; // You can replace this with actual user ID retrieval logic
        Guid tenantId = Guid.Empty; // You can replace this with actual partition key retrieval logic
        return await DefaultCommandHandler.HandleDelete<_ReplaceMe_>(_nostify, _ReplaceMe_Command.Delete, aggregateId, userId, tenantId);
    }
}

