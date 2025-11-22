using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_Created
{
    private readonly INostify _nostify;
    
    public On_ReplaceMe_Created(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ReplaceMe_Created))]
    public async Task Run([
#if (eventHubs)
                KafkaTrigger("BrokerList",
                "Create__ReplaceMe_",
                Username = "$ConnectionString",
                Password = "EventHubConnectionString",
                Protocol = BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "_ReplaceMe_")
#else
                KafkaTrigger("BrokerList",
                "Create__ReplaceMe_",
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
                ConsumerGroup = "_ReplaceMe_")
#endif
                ] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
    {
        await DefaultEventHandlers.HandleAggregateEvent<_ReplaceMe_>(_nostify, triggerEvent);
    }
    
}

