using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

// This is here as an example of how to handle an event that updates external data for a projection. 
// This is not required for all projections, but can be used when the projection has dependencies on external data sources that need to be updated when certain events are received.
// You will need to edit or delete this file based on the specific needs of your projection and the events it receives.

public class OnExternalDataExampleUpdated_For__ProjectionName_
{
    private readonly INostify _nostify;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OnExternalDataExampleUpdated_For__ProjectionName_> _logger;

    public OnExternalDataExampleUpdated_For__ProjectionName_(INostify nostify, HttpClient httpClient, ILogger<OnExternalDataExampleUpdated_For__ProjectionName_> logger)
    {
        this._nostify = nostify;
        _httpClient = httpClient;
        this._logger = logger;
    }

    [Function(nameof(OnExternalDataExampleUpdated_For__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Update_ExternalDataExample",
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
                ConsumerGroup = "_ProjectionName_")] NostifyKafkaTriggerEvent triggerEvent)
    {
        // Use the default event handler to apply the event to the projection and persist it
        // If the Projection has no external data dependencies, you can pass null for the HttpClient to avoid unnecessary overhead
        await DefaultEventHandlers.HandleMultiApplyEvent<_ProjectionName_>(_nostify, 
                                        triggerEvent, p => p.externalAggregateExample1Id);        
    }
    
}

