using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;

namespace _ReplaceMe__Service;

public class OnEventCreated
{
    private readonly INostify _nostify;

    public OnEventCreated(INostify nostify)
    {
        this._nostify = nostify;
    }

    [Function(nameof(OnEventCreated))]
    public async Task Run([CosmosDBTrigger(
            databaseName: "_ReplaceMe__DB",
            containerName: "eventStore",
            Connection = "CosmosEmulatorConnectionString",
            CreateLeaseContainerIfNotExists = true,
            LeaseContainerPrefix = "OnEventCreated_",
            LeaseContainerName = "leases")] string peListString,
        ILogger log)
    {
        await _nostify.PublishEventAsync(peListString);
    }
}

