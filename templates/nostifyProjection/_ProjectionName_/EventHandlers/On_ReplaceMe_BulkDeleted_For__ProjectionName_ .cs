using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;

namespace _ReplaceMe__Service;

public class On_ReplaceMe_BulkDeletedFor__ProjectionName_
{
    private readonly INostify _nostify;
    private readonly HttpClient _httpClient;
    private readonly ILogger<On_ReplaceMe_BulkDeletedFor__ProjectionName_> _logger;
    
    public On_ReplaceMe_BulkDeletedFor__ProjectionName_(INostify nostify, HttpClient httpClient, ILogger<On_ReplaceMe_BulkDeletedFor__ProjectionName_> logger)
    {
        this._nostify = nostify;
        _httpClient = httpClient;
        this._logger = logger;
    }

    [Function(nameof(On_ReplaceMe_BulkDeletedFor__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "BulkDelete__ReplaceMe_",
                ConsumerGroup = "_ProjectionName_",
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
                IsBatched = true)] string[] events)
    {
        try
        {
            Container bulkDeleteContainer = await _nostify.GetBulkProjectionContainerAsync<_ProjectionName_>();
            await bulkDeleteContainer.BulkDeleteFromEventsAsync<_ProjectionName_>(events);
        }
        catch (Exception e)
        {
            events.ToList().ForEach(async eventStr =>
            {
                Event @event = JsonConvert.DeserializeObject<NostifyKafkaTriggerEvent>(eventStr)?.GetEvent() ?? throw new NostifyException("Event is null");
                await _nostify.HandleUndeliverableAsync(nameof(On_ReplaceMe_BulkDeletedFor__ProjectionName_), e.Message, @event);
            });
        }

        
    }
    
}

