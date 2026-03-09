
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using nostify;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Confluent.Kafka;
using System;
using System.Collections.Generic;
using System.Linq;
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
        string messageValue = triggerEvent.Value;
        if (string.IsNullOrWhiteSpace(messageValue))
        {
            _logger.LogWarning("Received empty Kafka message on _ReplaceMe__EventRequest topic");
            return;
        }

        // Ignore response messages (they have the "complete" property)
        if (messageValue.Contains("\"complete\""))
        {
            return;
        }

        AsyncEventRequest request;
        try
        {
            request = JsonConvert.DeserializeObject<AsyncEventRequest>(messageValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize AsyncEventRequest: {Message}", messageValue);
            return;
        }

        if (request == null || request.aggregateRootIds == null || request.aggregateRootIds.Count == 0)
        {
            _logger.LogWarning("Received invalid AsyncEventRequest: missing aggregateRootIds");
            return;
        }

        _logger.LogInformation("Processing AsyncEventRequest for {Count} aggregate root IDs, correlationId: {CorrelationId}",
            request.aggregateRootIds.Count, request.correlationId);

        try
        {
            Container eventStore = await _nostify.GetEventStoreContainerAsync();

            var eventsQuery = eventStore
                .GetItemLinqQueryable<Event>()
                .Where(x => request.aggregateRootIds.Contains(x.aggregateRootId));

            if (request.pointInTime.HasValue)
            {
                eventsQuery = eventsQuery.Where(e => e.timestamp <= request.pointInTime.Value);
            }

            List<Event> allEvents = await eventsQuery
                .OrderBy(e => e.timestamp)
                .ReadAllAsync();

            // Chunk events into response messages
            int maxBytes = int.TryParse(Environment.GetEnvironmentVariable("AsyncEventRequestMaxMessageBytes"), out int mb) ? mb : 900_000;
            var chunks = AsyncEventRequestResponse.ChunkEvents(
                allEvents,
                maxBytes,
                request.topic,
                request.subtopic ?? string.Empty,
                request.correlationId
            );

            _logger.LogInformation("Sending {ChunkCount} response chunk(s) with {EventCount} total events for correlationId: {CorrelationId}",
                chunks.Count, allEvents.Count, request.correlationId);

            foreach (var chunk in chunks)
            {
                var responseJson = JsonConvert.SerializeObject(chunk);
                await _nostify.KafkaProducer.ProduceAsync(
                    request.topic,
                    new Message<string, string> { Value = responseJson }
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AsyncEventRequest for correlationId: {CorrelationId}", request.correlationId);

            // Send an error response so the requester doesn't hang waiting
            var errorResponse = new AsyncEventRequestResponse
            {
                topic = request.topic,
                subtopic = request.subtopic ?? string.Empty,
                correlationId = request.correlationId,
                events = new List<Event>(),
                complete = true
            };
            var errorJson = JsonConvert.SerializeObject(errorResponse);
            await _nostify.KafkaProducer.ProduceAsync(
                request.topic,
                new Message<string, string> { Value = errorJson }
            );
        }
    }
}
