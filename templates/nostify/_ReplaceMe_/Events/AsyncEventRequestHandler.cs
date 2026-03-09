
using Microsoft.Extensions.Logging;
using nostify;
using Microsoft.Azure.Functions.Worker;
using System.Threading.Tasks;

namespace _ReplaceMe__Service;

/// <summary>
/// Kafka trigger that handles incoming <see cref="AsyncEventRequest"/> messages.
/// Queries the event store for the requested aggregate root IDs and publishes
/// <see cref="AsyncEventRequestResponse"/> chunks back to the same topic.
/// </summary>
public class AsyncEventRequestHandler
{
    private readonly INostify _nostify;
    private readonly ILogger<AsyncEventRequestHandler> _logger;

    public AsyncEventRequestHandler(INostify nostify, ILogger<AsyncEventRequestHandler> logger)
    {
        this._nostify = nostify;
        this._logger = logger;
    }

    [Function(nameof(AsyncEventRequestHandler))]
    public async Task Run([
#if (eventHubs)
                KafkaTrigger("BrokerList",
                "_ReplaceMe__EventRequest",
                Username = "$ConnectionString",
                Password = "EventHubConnectionString",
                Protocol = BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "_ReplaceMe__AsyncEventRequestHandler")
#else
                KafkaTrigger("BrokerList",
                "_ReplaceMe__EventRequest",
//-:cnd:noEmit
                #if DEBUG
                Protocol = BrokerProtocol.NotSet,
                AuthenticationMode = BrokerAuthenticationMode.NotSet,
                #else
                Username = "KafkaApiKey",
                Password = "KafkaApiSecret",
                Protocol =  BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                #endif
//+:cnd:noEmit
                ConsumerGroup = "_ReplaceMe__AsyncEventRequestHandler")
#endif
                ] NostifyKafkaTriggerEvent triggerEvent)
    {
        await DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(_nostify, triggerEvent, _logger);
    }
}
