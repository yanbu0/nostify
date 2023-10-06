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
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "_ReplaceMe_")] _ReplaceMe_ create_ReplaceMe_,
            ILogger log)
        {
            //Need new id for aggregate root since its new
            create_ReplaceMe_.id = Guid.NewGuid();

            PersistedEvent pe = new PersistedEvent(NostifyCommand.Create, create_ReplaceMe_.id, create_ReplaceMe_);
            await _nostify.PersistAsync(pe);

            return new OkObjectResult(create_ReplaceMe_.id);
        }
    }
}
