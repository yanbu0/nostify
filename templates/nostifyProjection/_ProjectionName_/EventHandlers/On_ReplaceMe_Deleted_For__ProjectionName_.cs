using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_Deleted_For__ProjectionName_
{
    private readonly INostify _nostify;
    
    
    public On_ReplaceMe_Deleted_For__ProjectionName_(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ReplaceMe_Deleted_For__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Delete__ReplaceMe_",
                ConsumerGroup = "_ProjectionName_")] NostifyKafkaTriggerEvent triggerEvent,
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
            await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_Deleted_For__ProjectionName_), e.Message, newEvent);
        }

        
        
    }
}

