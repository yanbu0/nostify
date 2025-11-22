using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ServiceName__Service;

public class On_ReplaceMe_BulkUpdated
{
    private readonly INostify _nostify;
    
    public On_ReplaceMe_BulkUpdated(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ReplaceMe_BulkUpdated))]
    public async Task Run([KafkaTrigger("BrokerList",
                "BulkUpdate__ReplaceMe_",
                ConsumerGroup = "_ReplaceMe_",
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
                IsBatched = true)] string[] events,
        ILogger log)
    {
        await DefaultEventHandlers.HandleAggregateBulkUpdateEvent<_ReplaceMe_>(_nostify, events);
    }
}
