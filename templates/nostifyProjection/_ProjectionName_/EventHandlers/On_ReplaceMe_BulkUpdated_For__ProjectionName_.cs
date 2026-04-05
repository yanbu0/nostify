using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_BulkUpdatedFor__ProjectionName_
{
    private readonly INostify _nostify;
    private readonly HttpClient _httpClient;
    private readonly ILogger<On_ReplaceMe_BulkUpdatedFor__ProjectionName_> _logger;
    
    public On_ReplaceMe_BulkUpdatedFor__ProjectionName_(INostify nostify, HttpClient httpClient, ILogger<On_ReplaceMe_BulkUpdatedFor__ProjectionName_> logger)
    {
        this._nostify = nostify;
        _httpClient = httpClient;
        this._logger = logger;
    }

    [Function(nameof(On_ReplaceMe_BulkUpdatedFor__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "BulkUpdate__ReplaceMe_",
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
        // Optional: Add retry options for eventual consistency scenarios
        // var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), retryWhenNotFound: true);
        // int updatedCount = await DefaultEventHandlers.HandleProjectionBulkUpdateEventAsync<_ProjectionName_>(_nostify, events, retryOptions: retryOptions);
        // Note: If WithLogger() was called during Nostify setup, the logger is passed automatically via nostify.Logger
        int updatedCount = await DefaultEventHandlers.HandleProjectionBulkUpdateEventAsync<_ProjectionName_>(_nostify, events);
        _logger.LogInformation("{Handler} processed {Count} records", nameof(On_ReplaceMe_BulkUpdatedFor__ProjectionName_), updatedCount);
    }
    
}

