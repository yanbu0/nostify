using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_Updated
{
    private readonly INostify _nostify;

    public On_ReplaceMe_Updated(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ReplaceMe_Updated))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Update__ReplaceMe_",
                ConsumerGroup = "$Default")] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
    {
        PersistedEvent? pe = triggerEvent.GetPersistedEvent();
        try
        {
            if (pe != null)
            {
                Guid aggId = pe.aggregateRootId.ToGuid();
                
                //Update aggregate current state projection
                Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync();
                _ReplaceMe_? aggregate = (await currentStateContainer
                    .GetItemLinqQueryable<_ReplaceMe_>()
                    .Where(agg => agg.id == aggId)
                    .ReadAllAsync())
                    .FirstOrDefault();

                //Null means it has been deleted
                if (aggregate != null)
                {
                    aggregate.Apply(pe);
                    await currentStateContainer.UpsertItemAsync<_ReplaceMe_>(aggregate);
                }
            }                       

        }
        catch (Exception e)
        {
            await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_Updated), e.Message, pe);
        }

        
    }
    
}

