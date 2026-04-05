using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ServiceName__Service;

public class On_ReplaceMe_BulkCreated
{
    private readonly INostify _nostify;
    private readonly ILogger<On_ReplaceMe_BulkCreated> _logger;
    
    public On_ReplaceMe_BulkCreated(INostify nostify, ILogger<On_ReplaceMe_BulkCreated> logger)
    {
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(On_ReplaceMe_BulkCreated))]
    public async Task Run([KafkaTrigger("BrokerList",
                "BulkCreate__ReplaceMe_",
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
                IsBatched = true)] string[] events)
    {
        int createdCount = await DefaultEventHandlers.HandleAggregateBulkCreateEventAsync<_ReplaceMe_>(_nostify, events);
        _logger.LogInformation("{Handler} processed {Count} records", nameof(On_ReplaceMe_BulkCreated), createdCount);        
    }    
}

