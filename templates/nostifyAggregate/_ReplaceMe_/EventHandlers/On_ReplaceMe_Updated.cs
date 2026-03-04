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

    public On_ReplaceMe_Updated(INostify nostify)
    {
        this._nostify = nostify;
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
                ConsumerGroup = "_ReplaceMe_")] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
    {
        // Optional: Add retry options for eventual consistency scenarios
        // var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), retryWhenNotFound: true);
        // await DefaultEventHandlers.HandleAggregateEventAsync<_ReplaceMe_>(_nostify, triggerEvent, retryOptions: retryOptions);
        // Note: If WithLogger() was called during Nostify setup, the logger is passed automatically via nostify.Logger
        await DefaultEventHandlers.HandleAggregateEventAsync<_ReplaceMe_>(_nostify, triggerEvent);
    }
    
}

