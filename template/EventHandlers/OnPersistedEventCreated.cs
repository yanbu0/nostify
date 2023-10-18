using System;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using nostify;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;

namespace _ReplaceMe__Service
{
    public class OnPersistedEventCreated
    {
        private readonly INostify _nostify;

        public OnPersistedEventCreated(INostify nostify)
        {
            this._nostify = nostify;
        }

        [FunctionName(nameof(OnPersistedEventCreated))]
        public async Task Run([CosmosDBTrigger(
            databaseName: "_ReplaceMe__DB",
            collectionName: "persistedEvents",
            ConnectionStringSetting = "<YourConnectionStringHere>",
            CreateLeaseCollectionIfNotExists = true,
            LeaseCollectionPrefix = "OnPersistedEventCreated_",
            LeaseCollectionName = "leases")] IReadOnlyList<Document> input,
            ILogger log)
        {
            if (input != null)
            {
                foreach (Document doc in input)
                {
                    PersistedEvent pe = null;
                    try
                    {
                        pe = JsonConvert.DeserializeObject<PersistedEvent>(doc.ToString());

                        var agg = new _ReplaceMe_();
                        agg.Apply(pe);

                        //Update aggregate current state projection
                        Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync();
                        await currentStateContainer.UpsertItemAsync<_ReplaceMe_>(agg);                            

                    }
                    catch (Exception e)
                    {
                        await _nostify.HandleUndeliverableAsync(nameof(OnPersistedEventCreated), e.Message, pe);
                    }

                }
            }
        }
    }
}
