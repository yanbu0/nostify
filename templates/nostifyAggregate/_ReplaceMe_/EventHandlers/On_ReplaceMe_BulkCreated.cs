using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_BulkCreated
{
    private readonly INostify _nostify;
    
    public On_ReplaceMe_BulkCreated(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ReplaceMe_BulkCreated))]
    public async Task Run([KafkaTrigger("BrokerList",
                "BulkCreate__ReplaceMe_",
                ConsumerGroup = "_ReplaceMe_",
                IsBatched = true)] string[] events,
        ILogger log)
    {
        try
        {
            Container currentStateContainer = await _nostify.GetBulkCurrentStateContainerAsync<_ReplaceMe_>();
            await currentStateContainer.BulkCreateFromKafkaTriggerEventsAsync<_ReplaceMe_>(events);                         
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent() ?? throw new NostifyException("Event is null");
                await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_BulkCreated), e.Message, @event);
            });            
        }        
    }    
}

