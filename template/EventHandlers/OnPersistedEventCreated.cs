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
using Microsoft.Azure.WebJobs.Extensions.Kafka;
using Confluent.Kafka;

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
                        var config = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>("bootstrap.servers", "localhost:64546")
                        };


                        using (var p = new ProducerBuilder<string,string>(config).Build())
                        {
                            var result = p.ProduceAsync("test", new Message<string, string>{  Value = doc.ToString() });
                        }                    

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
