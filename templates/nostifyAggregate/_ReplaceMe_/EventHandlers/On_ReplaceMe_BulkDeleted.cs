using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ServiceName__Service;

public class On_ReplaceMe_BulkDeleted
{
    private readonly INostify _nostify;
    
    public On_ReplaceMe_BulkDeleted(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ReplaceMe_BulkDeleted))]
    public async Task Run([KafkaTrigger("BrokerList",
                "BulkDelete__ReplaceMe_",
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
        await DefaultEventHandlers.HandleAggregateBulkDeleteEvent<_ReplaceMe_>(_nostify, events);
    }
}
