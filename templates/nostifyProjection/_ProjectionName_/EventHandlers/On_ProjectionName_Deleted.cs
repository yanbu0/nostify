using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ProjectionName_Deleted
{
    private readonly INostify _nostify;
    
    
    public On_ProjectionName_Deleted(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ProjectionName_Deleted))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Delete__ReplaceMe_",
                ConsumerGroup = "$Default")] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
    {
        Event? newEvent = triggerEvent.GetEvent();
        try
        {
            if (newEvent != null)
            {
                //Update projection container
                Container projectionContainer = await _nostify.GetProjectionContainerAsync<_ProjectionName_>();
                //Remove from the container.  If you wish to set isDeleted instead, remove the code below and ApplyAndPersist the Event
                await projectionContainer.DeleteItemAsync<_ProjectionName_>(newEvent.id);
            }
        }
        catch (Exception e)
        {
            await _nostify.HandleUndeliverableAsync(nameof(On_ProjectionName_Deleted), e.Message, newEvent);
        }

        
        
    }
}

