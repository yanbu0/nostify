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
using Microsoft.Azure.Cosmos;
using System.Linq;

namespace _ReplaceMe__Service
{
    public class GetAccount
    {

        private readonly HttpClient _client;
        private readonly INostify _nostify;
        public GetAccount(HttpClient httpClient, INostify nostify)
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
            Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync();
            _ReplaceMe_ retObj = await currentStateContainer
                                .GetItemLinqQueryable<_ReplaceMe_>()
                                .Where(x => x.id == Guid.Parse(aggregateId))
                                .FirstOrDefaultAsync();
                                
            return new OkObjectResult(retObj);
        }
    }
}
