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
    private readonly ILogger<On_ReplaceMe_Deleted_For__ProjectionName_> _logger;
    
    
    public On_ReplaceMe_Deleted_For__ProjectionName_(INostify nostify, ILogger<On_ReplaceMe_Deleted_For__ProjectionName_> logger)
    {
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(On_ReplaceMe_Deleted_For__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Delete__ReplaceMe_",                
//-:cnd:noEmit
                #if DEBUG
                Protocol = BrokerProtocol.NotSet,
                AuthenticationMode = BrokerAuthenticationMode.NotSet,
                #else
                Username = "KafkaApiKey",
                Password = "KafkaApiSecret",
                Protocol =  BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                #endif
//+:cnd:noEmit
                ConsumerGroup = "_ProjectionName_")] NostifyKafkaTriggerEvent triggerEvent)
    {
        // Use the default event handler to apply the event to the projection and persist it
        // This will delete the projection from the projection container based on the event's aggregateRootId
        await DefaultEventHandlers.HandleProjectionEvent<_ProjectionName_>(_nostify, triggerEvent, null);           
    }
}

