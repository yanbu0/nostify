
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;

namespace _ReplaceMe__Service;

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
        [HttpTrigger("patch", Route = "_ReplaceMe_")] HttpRequestData req,
        ILogger log)
    {
        dynamic update_ReplaceMe_ = await req.Body.ReadFromRequestBodyAsync();
        Guid aggRootId = Guid.Parse(update_ReplaceMe_.id.ToString());
        Event pe = new Event(_ReplaceMe_Command.Update, aggRootId, update_ReplaceMe_);
        await _nostify.PersistEventAsync(pe);

        return update_ReplaceMe_.id;
    }
}

