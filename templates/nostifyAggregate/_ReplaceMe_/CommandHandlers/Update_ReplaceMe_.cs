using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;

namespace _ServiceName__Service;

public class Update_ReplaceMe_
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public Update_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Update_ReplaceMe_))]
    public async Task<Guid> Run(
        [HttpTrigger("patch", Route = "_ReplaceMe_/{id:guid?}")] HttpRequestData req,
        FunctionContext context,
        Guid? id,
        ILogger log)
    {
        Guid userId = Guid.Empty; // You can replace this with actual user ID retrieval logic
        Guid tenantId = Guid.Empty; // You can replace this with actual partition key retrieval logic
        return await DefaultCommandHandler.HandlePatch<_ReplaceMe_>(_nostify, _ReplaceMe_Command.Update, req, context, userId, tenantId);
    }
}

