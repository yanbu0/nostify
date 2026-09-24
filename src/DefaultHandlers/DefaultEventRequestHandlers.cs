
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
    // Compiled message templates avoid repeated parsing and unnecessary argument evaluation on hot request paths.
    private static readonly Action<ILogger, Exception?> LogEmptyKafkaMessage =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(LogEmptyKafkaMessage)),
            "Received empty Kafka message on EventRequest topic");

    private static readonly Action<ILogger, string, Exception?> LogDeserializationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(LogDeserializationFailure)),
            "Failed to deserialize AsyncEventRequest: {Message}");

    private static readonly Action<ILogger, Exception?> LogInvalidAsyncRequest =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(LogInvalidAsyncRequest)),
            "Received invalid AsyncEventRequest: missing aggregateRootIds");

    private static readonly Action<ILogger, int, string, Exception?> LogProcessingAsyncRequest =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(4, nameof(LogProcessingAsyncRequest)),
            "Processing AsyncEventRequest for {Count} aggregate root IDs, correlationId: {CorrelationId}");

    private static readonly Action<ILogger, int, int, string, Exception?> LogSendingResponseChunks =
        LoggerMessage.Define<int, int, string>(
            LogLevel.Information,
            new EventId(5, nameof(LogSendingResponseChunks)),
            "Sending {ChunkCount} response chunk(s) with {EventCount} total events for correlationId: {CorrelationId}");

    private static readonly Action<ILogger, long, string, Exception?> LogAsyncRequestCompleted =
        LoggerMessage.Define<long, string>(
            LogLevel.Information,
            new EventId(6, nameof(LogAsyncRequestCompleted)),
            "HandleAsyncEventRequestAsync completed in {ElapsedMs}ms for correlationId: {CorrelationId}");

    private static readonly Action<ILogger, string, long, Exception?> LogAsyncRequestFailure =
        LoggerMessage.Define<string, long>(
            LogLevel.Error,
            new EventId(7, nameof(LogAsyncRequestFailure)),
            "Error processing AsyncEventRequest for correlationId: {CorrelationId} after {ElapsedMs}ms");

    private static readonly Action<ILogger, long, int, int, Exception?> LogEventRequestCompleted =
        LoggerMessage.Define<long, int, int>(
            LogLevel.Information,
            new EventId(8, nameof(LogEventRequestCompleted)),
            "HandleEventRequestAsync completed in {ElapsedMs}ms for {Count} aggregate root IDs, returned {EventCount} events");

    private static readonly Action<ILogger, Exception?> LogInvalidGrpcRequest =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(9, nameof(LogInvalidGrpcRequest)),
            "Received invalid gRPC EventRequest: missing aggregateRootIds");

    private static readonly Action<ILogger, int, Exception?> LogProcessingGrpcRequest =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(10, nameof(LogProcessingGrpcRequest)),
            "Processing gRPC EventRequest for {Count} aggregate root IDs");

    private static readonly Action<ILogger, long, int, Exception?> LogGrpcRequestCompleted =
        LoggerMessage.Define<long, int>(
            LogLevel.Information,
            new EventId(11, nameof(LogGrpcRequestCompleted)),
            "HandleGrpcEventRequestAsync completed in {ElapsedMs}ms, returned {EventCount} events");

    /// <summary>
    /// Handles an incoming <see cref="AsyncEventRequest"/> from a Kafka trigger.
    /// Deserializes the request, queries the event store for the requested aggregate root IDs,
    /// chunks the results into <see cref="AsyncEventRequestResponse"/> messages, and publishes
    /// them to the response topic specified in the request (falling back to the request topic
    /// when no dedicated response topic is provided) via the Kafka producer.
    /// </summary>
    /// <param name="nostify">The nostify instance for accessing the event store and Kafka producer.</param>
    /// <param name="triggerEvent">The Kafka trigger event containing the serialized <see cref="AsyncEventRequest"/>.</param>
    /// <param name="logger">Optional logger. Falls back to <c>nostify.Logger</c> if null.</param>
    /// <param name="maxMessageBytes">Optional maximum byte size per response chunk. 
    /// Defaults to the <c>AsyncEventRequestMaxMessageBytes</c> environment variable, or 900,000 bytes.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static Task HandleAsyncEventRequestAsync(INostify nostify, NostifyKafkaTriggerEvent triggerEvent, ILogger? logger = null, int? maxMessageBytes = null)
    {
        return HandleAsyncEventRequestAsync(
            nostify,
            triggerEvent,
            CosmosQueryExecutor.Default,
            logger,
            maxMessageBytes);
    }

    /// <summary>
    /// Handles an asynchronous event request using an injectable query executor for deterministic testing.
    /// </summary>
    internal static async Task HandleAsyncEventRequestAsync(
        INostify nostify,
        NostifyKafkaTriggerEvent triggerEvent,
        IQueryExecutor queryExecutor,
        ILogger? logger = null,
        int? maxMessageBytes = null)
    {
        ArgumentNullException.ThrowIfNull(nostify);
        ArgumentNullException.ThrowIfNull(triggerEvent);
        ArgumentNullException.ThrowIfNull(queryExecutor);

        logger ??= nostify.Logger;
        var sw = Stopwatch.StartNew();

        string messageValue = triggerEvent.Value;
        if (string.IsNullOrWhiteSpace(messageValue))
        {
            if (logger != null)
            {
                LogEmptyKafkaMessage(logger, null);
            }

            return;
        }

        AsyncEventRequest? request;
        try
        {
            request = JsonConvert.DeserializeObject<AsyncEventRequest>(messageValue);
        }
        catch (Exception ex)
        {
            if (logger != null)
            {
                LogDeserializationFailure(logger, messageValue, ex);
            }

            return;
        }

        if (request?.aggregateRootIds == null || request.aggregateRootIds.Count == 0)
        {
            if (logger != null)
            {
                LogInvalidAsyncRequest(logger, null);
            }

            return;
        }

        if (logger != null)
        {
            LogProcessingAsyncRequest(logger, request.aggregateRootIds.Count, request.correlationId, null);
        }

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

            List<Event> allEvents = await queryExecutor.ReadAllAsync(
                eventsQuery.OrderBy(e => e.timestamp));

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

            if (logger != null)
            {
                LogSendingResponseChunks(logger, chunks.Count, allEvents.Count, request.correlationId, null);
            }

            foreach (var chunk in chunks)
            {
                var responseJson = JsonConvert.SerializeObject(chunk);
                await nostify.KafkaProducer.ProduceAsync(
                    responseTopic,
                    new Message<string, string> { Value = responseJson }
                );
            }

            sw.Stop();
            if (logger != null)
            {
                LogAsyncRequestCompleted(logger, sw.ElapsedMilliseconds, request.correlationId, null);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (logger != null)
            {
                LogAsyncRequestFailure(logger, request.correlationId, sw.ElapsedMilliseconds, ex);
            }

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
    public static Task<List<Event>> HandleEventRequestAsync(INostify nostify, List<Guid> aggregateRootIds, DateTime? pointInTime = null, ILogger? logger = null)
    {
        return HandleEventRequestAsync(nostify, aggregateRootIds, CosmosQueryExecutor.Default, pointInTime, logger);
    }

    /// <summary>
    /// Handles a synchronous event request by querying the event store for the specified
    /// aggregate root IDs, optionally filtered to a point in time.
    /// This overload accepts an <see cref="IQueryExecutor"/> for testability.
    /// </summary>
    /// <param name="nostify">The nostify instance for accessing the event store.</param>
    /// <param name="aggregateRootIds">The list of aggregate root IDs to query events for.</param>
    /// <param name="queryExecutor">The query executor to use for running LINQ queries.</param>
    /// <param name="pointInTime">Optional point in time to filter events up to.</param>
    /// <param name="logger">Optional logger. Falls back to <c>nostify.Logger</c> if null.</param>
    /// <returns>A list of events ordered by timestamp.</returns>
    public static async Task<List<Event>> HandleEventRequestAsync(INostify nostify, List<Guid> aggregateRootIds, IQueryExecutor queryExecutor, DateTime? pointInTime = null, ILogger? logger = null)
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

        List<Event> allEvents = await queryExecutor.ReadAllAsync(
            eventsQuery.OrderBy(e => e.timestamp));

        sw.Stop();
        if (logger != null)
        {
            LogEventRequestCompleted(logger, sw.ElapsedMilliseconds, aggregateRootIds.Count, allEvents.Count, null);
        }

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
    public static Task<nostify.Grpc.EventResponseMessage> HandleGrpcEventRequestAsync(
        INostify nostify,
        nostify.Grpc.EventRequestMessage request,
        ILogger? logger = null)
    {
        return HandleGrpcEventRequestAsync(nostify, request, CosmosQueryExecutor.Default, logger);
    }

    /// <summary>
    /// Handles a gRPC event request by querying the event store for the specified
    /// aggregate root IDs, optionally filtered to a point in time, and returns a protobuf response.
    /// This overload accepts an <see cref="IQueryExecutor"/> for testability.
    /// </summary>
    /// <param name="nostify">The nostify instance for accessing the event store.</param>
    /// <param name="request">The gRPC request message containing aggregate root IDs and optional point in time.</param>
    /// <param name="queryExecutor">The query executor to use for running LINQ queries.</param>
    /// <param name="logger">Optional logger. Falls back to <c>nostify.Logger</c> if null.</param>
    /// <returns>A gRPC response message containing the matching events.</returns>
    public static async Task<nostify.Grpc.EventResponseMessage> HandleGrpcEventRequestAsync(
        INostify nostify,
        nostify.Grpc.EventRequestMessage request,
        IQueryExecutor queryExecutor,
        ILogger? logger = null)
    {
        logger ??= nostify.Logger;
        var sw = Stopwatch.StartNew();

        if (request == null || request.AggregateRootIds == null || request.AggregateRootIds.Count == 0)
        {
            if (logger != null)
            {
                LogInvalidGrpcRequest(logger, null);
            }

            return new nostify.Grpc.EventResponseMessage();
        }

        var aggregateRootIds = request.AggregateRootIds
            .Where(id => Guid.TryParse(id, out _))
            .Select(id => Guid.Parse(id))
            .ToList();

        DateTime? pointInTime = request.HasPointInTime && request.PointInTime != null
            ? request.PointInTime.ToDateTime()
            : null;

        if (logger != null)
        {
            LogProcessingGrpcRequest(logger, aggregateRootIds.Count, null);
        }

        List<Event> allEvents = await HandleEventRequestAsync(nostify, aggregateRootIds, queryExecutor, pointInTime, logger);

        var response = new nostify.Grpc.EventResponseMessage();
        response.Events.AddRange(GrpcEventMapping.MapToProto(allEvents));

        sw.Stop();
        if (logger != null)
        {
            LogGrpcRequestCompleted(logger, sw.ElapsedMilliseconds, allEvents.Count, null);
        }

        return response;
    }
}
