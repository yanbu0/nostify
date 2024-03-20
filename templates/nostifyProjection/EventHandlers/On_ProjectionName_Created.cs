using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ProjectionName_Created
{
    private readonly INostify _nostify;
    private readonly HttpClient _httpClient;
    
    public On_ProjectionName_Created(INostify nostify, HttpClient httpClient)
    {
        this._nostify = nostify;
        _httpClient = httpClient;
    }

    [Function(nameof(On_ProjectionName_Created))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Create__ReplaceMe_",
                ConsumerGroup = "$Default")] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
    {
        Event? newEvent = triggerEvent.GetEvent();
        try
        {
            if (newEvent != null)
            {
                //Get projection container
                Container projectionContainer = await _nostify.GetProjectionContainerAsync(_ProjectionName_.containerName);

                //Create projection
                _ProjectionName_ proj = new _ProjectionName_();
                //Get external data
                Event externalData = await proj.SeedExternalDataAsync(_nostify, _httpClient);
                //Update projection container
                await projectionContainer.ApplyAndPersistAsync<_ProjectionName_>(new List<Event>(){newEvent,externalData});
            }                           
        }
        catch (Exception e)
        {
            await _nostify.HandleUndeliverableAsync(nameof(On_ProjectionName_Created), e.Message, newEvent);
        }

        
    }
    
}

