using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace nostify;

/// <summary>
/// Represents a response to an <see cref="AsyncEventRequest"/> sent via Kafka.
/// Responses may arrive in multiple chunks — the consumer accumulates events until <see cref="complete"/> is true.
/// </summary>
public class AsyncEventRequestResponse
{
    /// <summary>
    /// The Kafka topic this response was published to.
    /// </summary>
    public string topic { get; set; }

    /// <summary>
    /// Reserved for future use. Subtopic for more granular filtering.
    /// </summary>
    public string subtopic { get; set; }

    /// <summary>
    /// Whether this is the final chunk of the response.
    /// When true, the consumer can stop waiting for more messages for this correlationId.
    /// </summary>
    public bool complete { get; set; }

    /// <summary>
    /// The events in this chunk of the response.
    /// </summary>
    public List<Event> events { get; set; } = new List<Event>();

    /// <summary>
    /// The correlation ID matching the original <see cref="AsyncEventRequest.correlationId"/>.
    /// </summary>
    public string correlationId { get; set; }

    /// <summary>
    /// Splits a list of events into chunks that fit within the specified maximum byte size when serialized.
    /// Each chunk becomes an <see cref="AsyncEventRequestResponse"/> with <see cref="complete"/> = false,
    /// except the last chunk which has <see cref="complete"/> = true.
    /// </summary>
    /// <param name="events">The full list of events to chunk</param>
    /// <param name="maxBytes">Maximum serialized size per chunk in bytes (default ~900KB)</param>
    /// <param name="topic">The topic for each response chunk</param>
    /// <param name="subtopic">The subtopic for each response chunk</param>
    /// <param name="correlationId">The correlation ID for each response chunk</param>
    /// <returns>List of response chunks</returns>
    public static List<AsyncEventRequestResponse> ChunkEvents(List<Event> events, int maxBytes, string topic, string subtopic, string correlationId)
    {
        var chunks = new List<AsyncEventRequestResponse>();

        if (events == null || events.Count == 0)
        {
            // Always produce at least one response with complete = true
            chunks.Add(new AsyncEventRequestResponse
            {
                topic = topic,
                subtopic = subtopic,
                correlationId = correlationId,
                events = new List<Event>(),
                complete = true
            });
            return chunks;
        }

        var currentChunk = new List<Event>();
        int currentSize = 0;

        foreach (var evt in events)
        {
            var eventJson = JsonConvert.SerializeObject(evt);
            var eventSize = System.Text.Encoding.UTF8.GetByteCount(eventJson);

            // If adding this event would exceed max, finalize current chunk
            if (currentChunk.Count > 0 && currentSize + eventSize > maxBytes)
            {
                chunks.Add(new AsyncEventRequestResponse
                {
                    topic = topic,
                    subtopic = subtopic,
                    correlationId = correlationId,
                    events = new List<Event>(currentChunk),
                    complete = false
                });
                currentChunk.Clear();
                currentSize = 0;
            }

            currentChunk.Add(evt);
            currentSize += eventSize;
        }

        // Final chunk
        chunks.Add(new AsyncEventRequestResponse
        {
            topic = topic,
            subtopic = subtopic,
            correlationId = correlationId,
            events = new List<Event>(currentChunk),
            complete = true
        });

        return chunks;
    }
}
