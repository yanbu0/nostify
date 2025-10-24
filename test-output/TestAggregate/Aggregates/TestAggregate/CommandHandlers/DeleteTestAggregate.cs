
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace TestAggregate_Service;

public class DeleteTestAggregate
{

    private readonly HttpClient _httpClient;
    private readonly INostify _nostify;
    public DeleteTestAggregate(HttpClient httpClient, INostify nostify)
    {
        this._httpClient = httpClient;
        this._nostify = nostify;
    }

    [Function(nameof(DeleteTestAggregate))]
    public async Task<Guid> Run(
        [HttpTrigger("delete", Route = "TestAggregate/{aggregateId:guid}")] HttpRequestData req,
        Guid aggregateId,
        ILogger log)
    {
        IEvent pe = new EventFactory().CreateNullPayloadEvent(TestAggregateCommand.Delete, aggregateId);
        await _nostify.PersistEventAsync(pe);

        return aggregateId;
    }
}

