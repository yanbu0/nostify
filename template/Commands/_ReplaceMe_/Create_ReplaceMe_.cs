using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using nostify;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;

namespace _ReplaceMe__Service;

public class Create_ReplaceMe_
{

    private readonly HttpClient _client;
    private readonly INostify _nostify;
    public Create_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._client = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Create_ReplaceMe_))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "_ReplaceMe_")] HttpRequestData req,
        ILogger log)
    {
        var new_ReplaceMe_ = JsonConvert.DeserializeObject<dynamic>(await new StreamReader(req.Body).ReadToEndAsync());

        //Need new id for aggregate root since its new
        string newId = Guid.NewGuid().ToString();
        new_ReplaceMe_.id = newId;
        
        PersistedEvent pe = new PersistedEvent(_ReplaceMe_Command.Create, newId, new_ReplaceMe_);
        await _nostify.PersistAsync(pe);

        return new OkObjectResult(newId);
    }
}

