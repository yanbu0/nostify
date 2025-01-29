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
                #if DEBUG
                Protocol = BrokerProtocol.NotSet,
                AuthenticationMode = BrokerAuthenticationMode.NotSet,
                #else
                Username = "KafkaApiKey",
                Password = "KafkaApiSecret",
                Protocol =  BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                #endif
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
                //Update projection container
                var newProj = await projectionContainer.ApplyAndPersistAsync<_ProjectionName_>(newEvent);
                //Initialize projection with external data
                await newProj.InitAsync(_nostify, _httpClient);
            }                           
        }
        catch (Exception e)
        {
            await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_Created_For__ProjectionName_), e.Message, newEvent);
        }

        
    }
    
}

