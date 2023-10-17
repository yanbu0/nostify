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
    public class On_ReplaceMe_Created
    {

        private readonly HttpClient _client;
        private readonly INostify _nostify;
        public On_ReplaceMe_Created(HttpClient httpClient, INostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;;
        }

        [FunctionName("On_ReplaceMe_Created")]
        public async Task Run([CosmosDBTrigger(
            databaseName: "_ReplaceMe__DB",
            collectionName: "persistedEvents",
            ConnectionStringSetting = "<YourConnectionStringHere>",
            CreateLeaseCollectionIfNotExists = true,
            LeaseCollectionPrefix = "On_ReplaceMe_Created_",
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
                        await _nostify.HandleUndeliverableAsync("On_ReplaceMe_Created", e.Message, pe);
                    }

                }
            }
        }
    }
}
