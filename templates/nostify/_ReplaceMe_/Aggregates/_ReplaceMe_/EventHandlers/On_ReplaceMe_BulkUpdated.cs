using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_BulkUpdated
{
    private readonly INostify _nostify;
    private readonly ILogger<On_ReplaceMe_BulkUpdated> _logger;
    
    public On_ReplaceMe_BulkUpdated(INostify nostify, ILogger<On_ReplaceMe_BulkUpdated> logger)
    {
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(On_ReplaceMe_BulkUpdated))]
    public async Task Run([
#if (eventHubs)
                KafkaTrigger("BrokerList",
                "BulkUpdate__ReplaceMe_",
                ConsumerGroup = "_ReplaceMe_",
                Username = "$ConnectionString",
                Password = "EventHubConnectionString",
                Protocol = BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                IsBatched = true)
#else
                KafkaTrigger("BrokerList",
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
                IsBatched = true)
#endif
                ] string[] events)
    {
        // Optional: Add retry options for eventual consistency scenarios
        // var retryOptions = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), retryWhenNotFound: true);
        // int updatedCount = await DefaultEventHandlers.HandleAggregateBulkUpdateEventAsync<_ReplaceMe_>(_nostify, events, retryOptions: retryOptions);
        // Note: If WithLogger() was called during Nostify setup, the logger is passed automatically via nostify.Logger
        int updatedCount = await DefaultEventHandlers.HandleAggregateBulkUpdateEventAsync<_ReplaceMe_>(_nostify, events);
        _logger.LogInformation("{Handler} processed {Count} records", nameof(On_ReplaceMe_BulkUpdated), updatedCount);
    }
}
