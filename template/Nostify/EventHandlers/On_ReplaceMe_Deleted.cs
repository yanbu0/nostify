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
    public class On_ReplaceMe_Deleted
    {

        private readonly HttpClient _client;
        private readonly Nostify _nostify;
        public On_ReplaceMe_Deleted(HttpClient httpClient, Nostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;;
        }

        [FunctionName("On_ReplaceMe_Deleted")]
        public async Task Run([CosmosDBTrigger(
            databaseName: "_ReplaceMe__DB",
            collectionName: "persistedEvents",
            ConnectionStringSetting = "<YourConnectionStringHere>",
            CreateLeaseCollectionIfNotExists = true,
            LeaseCollectionPrefix = "On_ReplaceMe_Deleted_",
            LeaseCollectionName = "leases")]IReadOnlyList<Document> input,
            ILogger log)
        {
            if (input != null)
            {
                foreach (Document doc in input)
                {
                    PersistedEvent pe = null;
                    try
                    {
                        Guid aggId = pe.id;
                        
                        //Update aggregate current state projection
                        Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync();
                        _ReplaceMe_ aggregate = (await currentStateContainer
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
                    catch (Exception e)
                    {
                        await _nostify.HandleUndeliverableAsync("On_ReplaceMe_Deleted", e.Message, pe);
                    }

                }
            }
        }
    }
}
