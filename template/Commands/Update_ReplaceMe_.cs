using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using nostify;

namespace nostify_example
{

    public class Update_ReplaceMe_
    {

        private readonly HttpClient _client;
        private readonly INostify _nostify;
        public Update_ReplaceMe_(HttpClient httpClient, INostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;
        }

        [FunctionName(nameof(Update_ReplaceMe_))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "_ReplaceMe_")] dynamic update_ReplaceMe_, HttpRequest httpRequest,
            ILogger log)
        {
            Guid aggRootId = Guid.Parse(update_ReplaceMe_.id.ToString());
            PersistedEvent pe = new PersistedEvent(NostifyCommand.Update, aggRootId, update_ReplaceMe_);
            await _nostify.PersistAsync(pe);

            return new OkObjectResult(update_ReplaceMe_.id);
        }
    }
}
