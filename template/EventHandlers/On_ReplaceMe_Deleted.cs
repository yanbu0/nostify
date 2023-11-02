using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json;

namespace _ReplaceMe__Service
{
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
                  ConsumerGroup = "$Default")] string eventData,
            ILogger log)
        {
            if (eventData != null)
            {
                PersistedEvent? pe = JsonConvert.DeserializeObject<PersistedEvent>(eventData);
                try
                {
                    if (pe != null)
                    {
                        Guid aggId = pe.id;
                        log.LogInformation($"Deleting _ReplaceMe_ {aggId}");
                        
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
                            await currentStateContainer.DeleteItemAsync<_ReplaceMe_>(aggregate.id.ToString(), aggregate.tenantId.ToString().ToPartitionKey());
                        }
                    }
                }
                catch (Exception e)
                {
                    await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_Deleted), e.Message, pe);
                }

            
            }
        }
    }
}
