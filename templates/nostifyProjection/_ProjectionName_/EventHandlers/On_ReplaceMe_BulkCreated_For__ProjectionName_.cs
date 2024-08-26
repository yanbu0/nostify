using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_BulkCreated_For__ProjectionName_
{
    private readonly INostify _nostify;
    private readonly HttpClient _httpClient;
    
    public On_ReplaceMe_BulkCreated_For__ProjectionName_(INostify nostify, HttpClient httpClient)
    {
        this._nostify = nostify;
        _httpClient = httpClient;
    }

    [Function(nameof(On_ReplaceMe_BulkCreated_For__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Create__ReplaceMe_",
                ConsumerGroup = "_ProjectionName_",
                IsBatched = true)] string[] events,
        ILogger log)
    {
        try
        {
            Container currentStateContainer = await _nostify.GetProjectionContainerAsync<_ProjectionName_>();    
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<_ProjectionName_>(events);                        
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = (JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr) ?? throw new NostifyException("Event is null")).GetEvent();
                await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_BulkCreated_For__ProjectionName_), e.Message, @event);
            });
        }

        
    }
    
}

