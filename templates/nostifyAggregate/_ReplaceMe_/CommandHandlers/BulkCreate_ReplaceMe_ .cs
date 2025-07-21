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
        ILogger log)
    {
        List<dynamic> new_ReplaceMe_List = JsonConvert.DeserializeObject<List<dynamic>>(await new StreamReader(req.Body).ReadToEndAsync()) ?? new List<dynamic>();
        List<Event> peList = new List<Event>();

        new_ReplaceMe_List.ForEach(e =>
        {
            //Need new id for aggregate root since its new
            Guid newId = Guid.NewGuid();
            e.id = newId;
            
            Event pe = new Event(_ReplaceMe_Command.BulkCreate, newId, e, Guid.Empty, Guid.Empty); //Empty guids should be replaced with user id and tenant id respectively
            peList.Add(pe);
        });

        await _nostify.BulkPersistEventAsync(peList);

        return new_ReplaceMe_List.Count;
    }
}

