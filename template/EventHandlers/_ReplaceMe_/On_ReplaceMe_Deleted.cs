using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_Deleted
{
    private readonly INostify _nostify;
    
    
    public On_ReplaceMe_Deleted(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(On_ReplaceMe_Deleted))]
    public async Task Run([KafkaTrigger("BrokerList",
                "Delete__ReplaceMe_",
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
                    .ReadAllAsync<_ReplaceMe_>())
                    .FirstOrDefault();

                //Null means it has already been deleted
                if (aggregate != null)
                {
                    await currentStateContainer.DeleteItemAsync<_ReplaceMe_>(aggregate.id, aggregate.tenantId);
                }
            }
        }
        catch (Exception e)
        {
            await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_Deleted), e.Message, pe);
        }

        
        
    }
}

