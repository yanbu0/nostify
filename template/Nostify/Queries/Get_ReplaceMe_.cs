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

namespace _ReplaceMe__Service
{
    public class GetAccount
    {

        private readonly HttpClient _client;
        private readonly Nostify _nostify;
        public GetAccount(HttpClient httpClient, Nostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;
        }

        [FunctionName("Get_ReplaceMe_")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_ReplaceMe_/{aggregateId:string}")] HttpRequest req,
            string aggregateId,
            ILogger log)
        {
            _ReplaceMe_ currentState = await _nostify.RehydrateAsync<_ReplaceMe_>(Guid.Parse(aggregateId));
            return new OkObjectResult(JsonConvert.SerializeObject(currentState));
        }
    }
}
