using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using nostify;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;

namespace TestAggregate_Service;

public class CreateTestAggregate
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public CreateTestAggregate(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(CreateTestAggregate))]
    public async Task<Guid> Run(
        [HttpTrigger("post", Route = "TestAggregate")] HttpRequestData req,
        ILogger log)
    {
        dynamic newTestAggregate = await req.Body.ReadFromRequestBodyAsync(true);

        //Need new id for aggregate root since its new
        Guid newId = Guid.NewGuid();
        newTestAggregate.id = newId;
        
        IEvent pe = new EventFactory().Create<TestAggregate>(TestAggregateCommand.Create, newId, newTestAggregate);
        await _nostify.PersistEventAsync(pe);

        return newId;
    }
}

