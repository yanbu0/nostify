using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TestAggregate_Service;

public class OnTestAggregateDeleted
{
    private readonly INostify _nostify;
    
    
    public OnTestAggregateDeleted(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(OnTestAggregateDeleted))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Delete_TestAggregate",
                #if DEBUG
                Protocol = BrokerProtocol.NotSet,
                AuthenticationMode = BrokerAuthenticationMode.NotSet,
                #else
                Username = "KafkaApiKey",
                Password = "KafkaApiSecret",
                Protocol =  BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                #endif
                ConsumerGroup = "TestAggregate")] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
    {
        Event? newEvent = triggerEvent.GetEvent();
        try
        {
            if (newEvent != null)
            {
                //Update aggregate current state projection
                Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync<TestAggregate>();
                await currentStateContainer.ApplyAndPersistAsync<TestAggregate>(newEvent);
            }
        }
        catch (Exception e)
        {
            await _nostify.HandleUndeliverableAsync(nameof(OnTestAggregateDeleted), e.Message, newEvent);
        }

        
        
    }
}

