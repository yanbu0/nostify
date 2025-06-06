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

namespace _ServiceName_Service;

public class Create_ReplaceMe_
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public Create_ReplaceMe_(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(Create_ReplaceMe_))]
    public async Task<IActionResult> Run(
        [HttpTrigger("post", Route = "_ReplaceMe_")] HttpRequestData req,
        ILogger log)
    {
        dynamic new_ReplaceMe_ = await req.Body.ReadFromRequestBodyAsync(true);

        //Need new id for aggregate root since its new
        Guid newId = Guid.NewGuid();
        new_ReplaceMe_.id = newId;
        
        Event pe = new Event(_ReplaceMe_Command.Create, newId, new_ReplaceMe_);
        await _nostify.PersistEventAsync(pe);

        return new OkObjectResult(newId);
    }
}

