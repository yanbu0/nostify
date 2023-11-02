using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;

namespace _ReplaceMe__Service
{
    public class OnPersistedEventCreated
    {
        private readonly INostify _nostify;

        public OnPersistedEventCreated(INostify nostify)
        {
            this._nostify = nostify;
        }

        [Function(nameof(OnPersistedEventCreated))]
        public async Task Run([CosmosDBTrigger(
                databaseName: "Location_DB",
                containerName: "persistedEvents",
                Connection = "CosmosEmulatorConnectionString",
                CreateLeaseContainerIfNotExists = true,
                LeaseContainerPrefix = "OnPersistedEventCreated_",
                LeaseContainerName = "leases")] string peListString,
            ILogger log)
        {
            await _nostify.PublishEventAsync(peListString);
        }
    }
}
