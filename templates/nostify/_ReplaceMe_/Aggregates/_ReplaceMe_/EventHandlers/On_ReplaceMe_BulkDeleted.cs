using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_BulkDeleted
{
    private readonly INostify _nostify;
    private readonly ILogger<On_ReplaceMe_BulkDeleted> _logger;
    
    public On_ReplaceMe_BulkDeleted(INostify nostify, ILogger<On_ReplaceMe_BulkDeleted> logger)
    {
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(On_ReplaceMe_BulkDeleted))]
    public async Task Run([
#if (eventHubs)
                KafkaTrigger("BrokerList",
                "BulkDelete__ReplaceMe_",
                ConsumerGroup = "_ReplaceMe_",
                Username = "$ConnectionString",
                Password = "EventHubConnectionString",
                Protocol = BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                IsBatched = true)
#else
                KafkaTrigger("BrokerList",
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
                IsBatched = true)
#endif
                ] string[] events)
    {
        await DefaultEventHandlers.HandleAggregateBulkDeleteEvent<_ReplaceMe_>(_nostify, events);
    }
}
