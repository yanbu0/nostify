using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_BulkCreated_For__ProjectionName_
{
    private readonly INostify _nostify;
    private readonly HttpClient _httpClient;
    private readonly ILogger<On_ReplaceMe_BulkCreated_For__ProjectionName_> _logger;
    
    public On_ReplaceMe_BulkCreated_For__ProjectionName_(INostify nostify, HttpClient httpClient, ILogger<On_ReplaceMe_BulkCreated_For__ProjectionName_> logger)
    {
        this._nostify = nostify;
        _httpClient = httpClient;
        this._logger = logger;
    }

    [Function(nameof(On_ReplaceMe_BulkCreated_For__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "BulkCreate__ReplaceMe_",
                ConsumerGroup = "_ProjectionName_",
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
        DefaultEventHandler.HandleProjectionBulkCreateEvents<_ReplaceMe_, _ProjectionName_>(
            _nostify,
            _httpClient,
            events,
            log);

        
    }
    
}

