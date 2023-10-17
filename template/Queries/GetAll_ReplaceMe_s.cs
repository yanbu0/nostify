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
using System.Collections.Generic;
using Microsoft.Azure.Cosmos;
using nostify;

namespace _ReplaceMe__Service
{
    public class GetAllAccounts
    {

        private readonly HttpClient _client;
        private readonly INostify _nostify;
        public GetAllAccounts(HttpClient httpClient, INostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;
        }

        [FunctionName("GetAll_ReplaceMe_s")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_ReplaceMe_")] HttpRequest req,
            ILogger log)
        {
            Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync();
            List<_ReplaceMe_> allList = await currentStateContainer
                                .GetItemLinqQueryable<_ReplaceMe_>()
                                .ReadAllAsync();


            return new OkObjectResult(allList);
        }
    }
}
