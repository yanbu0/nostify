using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service
{
    public class On_ReplaceMe_Created
    {
        private readonly INostify _nostify;
        
        public On_ReplaceMe_Created(INostify nostify)
        {
            this._nostify = nostify;
        }

        [Function(nameof(On_ReplaceMe_Created))]
        public async Task Run([KafkaTrigger("BrokerList",
                  "Create__ReplaceMe_",
                  ConsumerGroup = "$Default")] NostifyKafkaTriggerEvent triggerEvent,
            ILogger log)
        {
            PersistedEvent? pe = triggerEvent.GetPersistedEvent();
            try
            {
                if (pe != null)
                {
                    var agg = new _ReplaceMe_();
                    agg.Apply(pe);

                    //Update aggregate current state projection
                    Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync();
                    await currentStateContainer.UpsertItemAsync<_ReplaceMe_>(agg); 
                }                           
            }
            catch (Exception e)
            {
                await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_Created), e.Message, pe);
            }

            
        }
        
    }
}
