using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;

namespace _ReplaceMe__Service;

public class Update_ReplaceMe_
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public Update_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Update_ReplaceMe_))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "_ReplaceMe_")] HttpRequestData req,
        ILogger log)
    {
        dynamic? update_ReplaceMe_ = JsonConvert.DeserializeObject<dynamic>(await new StreamReader(req.Body).ReadToEndAsync());
        Guid aggRootId = Guid.Parse(update_ReplaceMe_.id.ToString());
        PersistedEvent pe = new PersistedEvent(_ReplaceMe_Command.Update, aggRootId, update_ReplaceMe_);
        await _nostify.PersistAsync(pe);

        return new OkObjectResult(update_ReplaceMe_.id);
    }
}

