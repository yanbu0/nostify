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
    
    public On_ReplaceMe_BulkDeletedFor__ProjectionName_(INostify nostify, HttpClient httpClient)
    {
        this._nostify = nostify;
        _httpClient = httpClient;
    }

    [Function(nameof(On_ReplaceMe_BulkDeletedFor__ProjectionName_))]
    public async Task Run([KafkaTrigger("BrokerList",
                "BulkDelete__ReplaceMe_",
                ConsumerGroup = "_ProjectionName_",
                #if DEBUG
                Protocol = BrokerProtocol.NotSet,
                AuthenticationMode = BrokerAuthenticationMode.NotSet,
                #else
                Username = "KafkaApiKey",
                Password = "KafkaApiSecret",
                Protocol =  BrokerProtocol.SaslSsl,
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                #endif
                IsBatched = true)] string[] events,
        ILogger log)
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

