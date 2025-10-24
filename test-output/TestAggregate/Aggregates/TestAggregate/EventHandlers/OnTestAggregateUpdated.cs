using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace TestAggregate_Service;

public class OnTestAggregateUpdated
{
    private readonly INostify _nostify;

    public OnTestAggregateUpdated(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(OnTestAggregateUpdated))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Update_TestAggregate",
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
            await _nostify.HandleUndeliverableAsync(nameof(OnTestAggregateUpdated), e.Message, newEvent);
        }

        
    }
    
}

