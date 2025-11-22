using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _ServiceName__Service;

public class On_ReplaceMe_Deleted
{
    private readonly INostify _nostify;
    
    
    public On_ReplaceMe_Deleted(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ReplaceMe_Deleted))]
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
                ConsumerGroup = "_ReplaceMe_")] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
    {
        await DefaultEventHandlers.HandleAggregateEvent<_ReplaceMe_>(_nostify, triggerEvent);
    }
}

