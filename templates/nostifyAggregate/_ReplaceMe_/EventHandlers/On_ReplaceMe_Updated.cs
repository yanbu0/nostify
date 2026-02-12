using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ServiceName__Service;

public class On_ReplaceMe_Updated
{
    private readonly INostify _nostify;
    private readonly ILogger<On_ReplaceMe_Updated> _logger;

    public On_ReplaceMe_Updated(INostify nostify, ILogger<On_ReplaceMe_Updated> logger)
    {
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(On_ReplaceMe_Updated))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Update__ReplaceMe_",
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
                ConsumerGroup = "_ReplaceMe_")] NostifyKafkaTriggerEvent triggerEvent)
    {
        await DefaultEventHandlers.HandleAggregateEvent<_ReplaceMe_>(_nostify, triggerEvent);
    }
    
}

