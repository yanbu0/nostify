using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using nostify;
using System.Linq;
using Microsoft.Azure.Cosmos.Linq;

namespace _ReplaceMe__Service
{
    public class Create_ReplaceMe_
    {

        private readonly HttpClient _client;
        private readonly Nostify _nostify;
        public Create_ReplaceMe_(HttpClient httpClient, Nostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;
        }

        [FunctionName("Create_ReplaceMe_")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] _ReplaceMe_ aggregate, HttpRequest httpRequest,
            ILogger log)
        {
            var peContainer = await _nostify.GetPersistedEventsContainerAsync();

            //Need new id for aggregate root since its new
            aggregate.id = Guid.NewGuid();

            PersistedEvent pe = new PersistedEvent(NostifyCommand.Create, aggregate.id, aggregate);
            await _nostify.PersistAsync(pe);

            return new OkObjectResult(new{ message = $"_ReplaceMe_ {aggregate.id} was created"});
        }
    }
}
