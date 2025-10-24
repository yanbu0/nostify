
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;

namespace TestAggregate_Service;

public class UpdateTestAggregate
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public UpdateTestAggregate(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(UpdateTestAggregate))]
    public async Task<Guid> Run(
        [HttpTrigger("patch", Route = "TestAggregate")] HttpRequestData req,
        ILogger log)
    {
        dynamic updateTestAggregate = await req.Body.ReadFromRequestBodyAsync();
        Guid aggRootId = Guid.Parse(updateTestAggregate.id.ToString());
        IEvent pe = new EventFactory().Create<TestAggregate>(TestAggregateCommand.Update, aggRootId, updateTestAggregate);
        await _nostify.PersistEventAsync(pe);

        return updateTestAggregate.id;
    }
}

