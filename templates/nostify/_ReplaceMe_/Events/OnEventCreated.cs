using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;

namespace _ReplaceMe__Service;

public class OnEventCreated
{
    private readonly INostify _nostify;
    private readonly ILogger<OnEventCreated> _logger;

    public OnEventCreated(INostify nostify, ILogger<OnEventCreated> logger)
    {
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(OnEventCreated))]
    public async Task Run([CosmosDBTrigger(
            databaseName: "_ReplaceMe__DB",
            containerName: "eventStore",
            Connection = "CosmosConnectionString",
            CreateLeaseContainerIfNotExists = true,
            LeaseContainerPrefix = "OnEventCreated_",
            LeaseContainerName = "leases")] string peListString)
    {
        await _nostify.PublishEventAsync(peListString);
    }
}

