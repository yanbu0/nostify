
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace nostify;

/// <summary>
/// Provides default handlers for event request operations, including async Kafka-based
/// event requests and synchronous HTTP event requests.
/// </summary>
public static class DefaultEventRequestHandlers
{
    /// <summary>
    /// Handles an incoming <see cref="AsyncEventRequest"/> from a Kafka trigger.
    /// Deserializes the request, queries the event store for the requested aggregate root IDs,
    /// chunks the results into <see cref="AsyncEventRequestResponse"/> messages, and publishes
    /// them back to the request topic via the Kafka producer.
    /// </summary>
    /// <param name="nostify">The nostify instance for accessing the event store and Kafka producer.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the serialized <see cref="AsyncEventRequest"/>.</param>
    /// <param name="logger">Optional logger. Falls back to <c>nostify.Logger</c> if null.</param>
    /// <param name="maxMessageBytes">Optional maximum byte size per response chunk. 
    /// Defaults to the <c>AsyncEventRequestMaxMessageBytes</c> environment variable, or 900,000 bytes.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task HandleAsyncEventRequestAsync(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, ILogger? logger = null, int? maxMessageBytes = null)
    {
        logger ??= nostify.Logger;
        var sw = Stopwatch.StartNew();

        string messageValue = triggerEvent.Value;
        if (string.IsNullOrWhiteSpace(messageValue))
        {
            logger?.LogWarning("Received empty Kafka message on EventRequest topic");
            return;
        }

        AsyncEventRequest request;
        try
        {
            request = JsonConvert.DeserializeObject<AsyncEventRequest>(messageValue);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to deserialize AsyncEventRequest: {Message}", messageValue);
            return;
        }

        if (request == null || request.aggregateRootIds == null || request.aggregateRootIds.Count == 0)
        {
            logger?.LogWarning("Received invalid AsyncEventRequest: missing aggregateRootIds");
            return;
        }

        logger?.LogInformation("Processing AsyncEventRequest for {Count} aggregate root IDs, correlationId: {CorrelationId}",
            request.aggregateRootIds.Count, request.correlationId);

        // Determine the response topic (falls back to request topic for backward compatibility)
        string responseTopic = !string.IsNullOrEmpty(request.responseTopic) ? request.responseTopic : request.topic;

        try
        {
            Container eventStore = await nostify.GetEventStoreContainerAsync();

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
            int maxBytes = maxMessageBytes
                ?? (int.TryParse(Environment.GetEnvironmentVariable("AsyncEventRequestMaxMessageBytes"), out int mb) ? mb : 900_000);
            var chunks = AsyncEventRequestResponse.ChunkEvents(
                allEvents,
                maxBytes,
                responseTopic,
                request.subtopic ?? string.Empty,
                request.correlationId
            );

            logger?.LogInformation("Sending {ChunkCount} response chunk(s) with {EventCount} total events for correlationId: {CorrelationId}",
                chunks.Count, allEvents.Count, request.correlationId);

            foreach (var chunk in chunks)
            {
                var responseJson = JsonConvert.SerializeObject(chunk);
                await nostify.KafkaProducer.ProduceAsync(
                    responseTopic,
                    new Message<string, string> { Value = responseJson }
                );
            }

            sw.Stop();
            logger?.LogInformation("HandleAsyncEventRequestAsync completed in {ElapsedMs}ms for correlationId: {CorrelationId}",
                sw.ElapsedMilliseconds, request.correlationId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger?.LogError(ex, "Error processing AsyncEventRequest for correlationId: {CorrelationId} after {ElapsedMs}ms",
                request.correlationId, sw.ElapsedMilliseconds);

            // Send an error response so the requester doesn't hang waiting
            var errorResponse = new AsyncEventRequestResponse
            {
                topic = responseTopic,
                subtopic = request.subtopic ?? string.Empty,
                correlationId = request.correlationId,
                events = new List<Event>(),
                complete = true
            };
            var errorJson = JsonConvert.SerializeObject(errorResponse);
            await nostify.KafkaProducer.ProduceAsync(
                responseTopic,
                new Message<string, string> { Value = errorJson }
            );
        }
    }

    /// <summary>
    /// Handles a synchronous event request by querying the event store for the specified
    /// aggregate root IDs, optionally filtered to a point in time.
    /// </summary>
    /// <param name="nostify">The nostify instance for accessing the event store.</param>
    /// <param name="aggregateRootIds">The list of aggregate root IDs to query events for.</param>
    /// <param name="pointInTime">Optional point in time to filter events up to.</param>
    /// <param name="logger">Optional logger. Falls back to <c>nostify.Logger</c> if null.</param>
    /// <returns>A list of events ordered by timestamp.</returns>
    public static async Task<List<Event>> HandleEventRequestAsync(INostify nostify, List<Guid> aggregateRootIds, DateTime? pointInTime = null, ILogger? logger = null)
    {
        logger ??= nostify.Logger;
        var sw = Stopwatch.StartNew();

        Container eventStore = await nostify.GetEventStoreContainerAsync();

        var eventsQuery = eventStore
            .GetItemLinqQueryable<Event>()
            .Where(x => aggregateRootIds.Contains(x.aggregateRootId));

        // Filter by pointInTime if provided
        if (pointInTime.HasValue)
        {
            eventsQuery = eventsQuery.Where(e => e.timestamp <= pointInTime.Value);
        }

        List<Event> allEvents = await eventsQuery
            .OrderBy(e => e.timestamp)
            .ReadAllAsync();

        sw.Stop();
        logger?.LogInformation("HandleEventRequestAsync completed in {ElapsedMs}ms for {Count} aggregate root IDs, returned {EventCount} events",
            sw.ElapsedMilliseconds, aggregateRootIds.Count, allEvents.Count);

        return allEvents;
    }

    /// <summary>
    /// Handles a gRPC event request by querying the event store for the specified
    /// aggregate root IDs, optionally filtered to a point in time, and returns a protobuf response.
    /// This method is intended to be called from a gRPC service implementation that extends
    /// <c>EventRequestService.EventRequestServiceBase</c>.
    /// </summary>
    /// <param name="nostify">The nostify instance for accessing the event store.</param>
    /// <param name="request">The gRPC request message containing aggregate root IDs and optional point in time.</param>
    /// <param name="logger">Optional logger. Falls back to <c>nostify.Logger</c> if null.</param>
    /// <returns>A gRPC response message containing the matching events.</returns>
    public static async Task<nostify.Grpc.EventResponseMessage> HandleGrpcEventRequestAsync(
        INostify nostify,
        nostify.Grpc.EventRequestMessage request,
        ILogger? logger = null)
    {
        logger ??= nostify.Logger;
        var sw = Stopwatch.StartNew();

        if (request == null || request.AggregateRootIds == null || request.AggregateRootIds.Count == 0)
        {
            logger?.LogWarning("Received invalid gRPC EventRequest: missing aggregateRootIds");
            return new nostify.Grpc.EventResponseMessage();
        }

        var aggregateRootIds = request.AggregateRootIds
            .Where(id => Guid.TryParse(id, out _))
            .Select(id => Guid.Parse(id))
            .ToList();

        DateTime? pointInTime = request.HasPointInTime && request.PointInTime != null
            ? request.PointInTime.ToDateTime()
            : null;

        logger?.LogInformation("Processing gRPC EventRequest for {Count} aggregate root IDs",
            aggregateRootIds.Count);

        List<Event> allEvents = await HandleEventRequestAsync(nostify, aggregateRootIds, pointInTime, logger);

        var response = new nostify.Grpc.EventResponseMessage();
        response.Events.AddRange(GrpcEventMapping.MapToProto(allEvents));

        sw.Stop();
        logger?.LogInformation("HandleGrpcEventRequestAsync completed in {ElapsedMs}ms, returned {EventCount} events",
            sw.ElapsedMilliseconds, allEvents.Count);

        return response;
    }
}
