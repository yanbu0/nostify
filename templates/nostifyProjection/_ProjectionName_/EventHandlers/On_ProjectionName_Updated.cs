using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ProjectionName_Updated
{
    private readonly INostify _nostify;

    public On_ProjectionName_Updated(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ProjectionName_Updated))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Update__ReplaceMe_",
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
                await projectionContainer.ApplyAndPersistAsync<_ProjectionName_>(newEvent);
            }                       

        }
        catch (Exception e)
        {
            await _nostify.HandleUndeliverableAsync(nameof(On_ProjectionName_Updated), e.Message, newEvent);
        }

        
    }
    
}

