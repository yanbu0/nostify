using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_Created_For__ProjectionName_
{
    private readonly INostify _nostify;
    private readonly HttpClient _httpClient;
    
    public On_ReplaceMe_Created_For__ProjectionName_(INostify nostify, HttpClient httpClient)
    {
        this._nostify = nostify;
        _httpClient = httpClient;
    }

    [Function(nameof(On_ReplaceMe_Created_For__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Create__ReplaceMe_",
                ConsumerGroup = "_ProjectionName_")] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
    {
        Event? newEvent = triggerEvent.GetEvent();
        try
        {
            if (newEvent != null)
            {
                //Get projection container
                Container projectionContainer = await _nostify.GetProjectionContainerAsync<_ProjectionName_>();

                //Create projection
                _ProjectionName_ proj = new _ProjectionName_();
                //Apply create
                proj.Apply(newEvent);
                //Get external data
                Event externalData = await proj.SeedExternalDataAsync(_nostify, _httpClient);
                //Update projection container
                await projectionContainer.ApplyAndPersistAsync<_ProjectionName_>(new List<Event>(){newEvent,externalData});
            }                           
        }
        catch (Exception e)
        {
            await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_Created_For__ProjectionName_), e.Message, newEvent);
        }

        
    }
    
}

