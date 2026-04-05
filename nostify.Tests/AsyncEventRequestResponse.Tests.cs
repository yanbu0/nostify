using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nostify;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace nostify.Tests;

public class AsyncEventRequestResponseTests
{
    private Event CreateTestEvent(Guid? aggregateRootId = null)
    {
        return new Event
        {
            aggregateRootId = aggregateRootId ?? Guid.NewGuid(),
            timestamp = DateTime.UtcNow,
            command = new NostifyCommand("TestCommand"),
            payload = JObject.FromObject(new { name = "test" })
        };
    }

    #region ChunkEvents - Empty / Null Events

    [Fact]
    public void ChunkEvents_NullEvents_ReturnsSingleCompleteResponse()
    {
        // Act
        var chunks = AsyncEventRequestResponse.ChunkEvents(null, 900_000, "topic", "sub", "corr-123");

        // Assert
        Assert.Single(chunks);
        Assert.True(chunks[0].complete);
        Assert.Empty(chunks[0].events);
        Assert.Equal("corr-123", chunks[0].correlationId);
        Assert.Equal("topic", chunks[0].topic);
        Assert.Equal("sub", chunks[0].subtopic);
    }

    [Fact]
    public void ChunkEvents_EmptyEvents_ReturnsSingleCompleteResponse()
    {
        // Act
        var chunks = AsyncEventRequestResponse.ChunkEvents(new List<Event>(), 900_000, "topic", "sub", "corr-456");

        // Assert
        Assert.Single(chunks);
        Assert.True(chunks[0].complete);
        Assert.Empty(chunks[0].events);
        Assert.Equal("corr-456", chunks[0].correlationId);
    }

    #endregion

    #region ChunkEvents - Single Chunk Scenarios

    [Fact]
    public void ChunkEvents_SmallEventList_ReturnsSingleChunk()
    {
        // Arrange
        var events = new List<Event> { CreateTestEvent(), CreateTestEvent() };

        // Act
        var chunks = AsyncEventRequestResponse.ChunkEvents(events, 900_000, "topic", "", "corr-1");

        // Assert
        Assert.Single(chunks);
        Assert.True(chunks[0].complete);
        Assert.Equal(2, chunks[0].events.Count);
        Assert.Equal("corr-1", chunks[0].correlationId);
    }

    [Fact]
    public void ChunkEvents_SingleEvent_ReturnsSingleChunk()
    {
        // Arrange
        var events = new List<Event> { CreateTestEvent() };

        // Act
        var chunks = AsyncEventRequestResponse.ChunkEvents(events, 900_000, "myTopic", "mySub", "corr-single");

        // Assert
        Assert.Single(chunks);
        Assert.True(chunks[0].complete);
        Assert.Single(chunks[0].events);
    }

    #endregion

    #region ChunkEvents - Multi Chunk Scenarios

    [Fact]
    public void ChunkEvents_LargeEventList_SplitsIntoMultipleChunks()
    {
        // Arrange - Create many events that exceed a small max to force chunking
        var events = Enumerable.Range(0, 50).Select(_ => CreateTestEvent()).ToList();
        
        // Calculate size of one serialized event to set a maxBytes that forces chunking
        var singleEventJson = JsonConvert.SerializeObject(events[0]);
        var singleEventSize = Encoding.UTF8.GetByteCount(singleEventJson);
        
        // Set max to fit ~5 events per chunk
        var maxBytes = singleEventSize * 5;

        // Act
        var chunks = AsyncEventRequestResponse.ChunkEvents(events, maxBytes, "topic", "", "corr-multi");

        // Assert
        Assert.True(chunks.Count > 1, $"Expected multiple chunks but got {chunks.Count}");

        // Only the last chunk should be complete
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            Assert.False(chunks[i].complete, $"Chunk {i} should not be complete");
        }
        Assert.True(chunks[^1].complete, "Last chunk should be complete");

        // All chunks should have the same correlation ID
        Assert.All(chunks, c => Assert.Equal("corr-multi", c.correlationId));
        Assert.All(chunks, c => Assert.Equal("topic", c.topic));

        // Total events across all chunks should equal input
        var totalEvents = chunks.Sum(c => c.events.Count);
        Assert.Equal(50, totalEvents);
    }

    [Fact]
    public void ChunkEvents_MaxBytesVerySmall_PutsOneEventPerChunk()
    {
        // Arrange
        var events = new List<Event> { CreateTestEvent(), CreateTestEvent(), CreateTestEvent() };

        // Use a maxBytes of 1 so that each event forces a new chunk (except first goes into first chunk)
        // Actually we need maxBytes ≥ first event size for the first to go in,
        // but the algorithm always adds the first event. Let's use a sufficiently small value.
        var singleEventJson = JsonConvert.SerializeObject(events[0]);
        var singleEventSize = Encoding.UTF8.GetByteCount(singleEventJson);

        // maxBytes = exactly one event. Second event would exceed, so new chunk.
        var maxBytes = singleEventSize;

        // Act
        var chunks = AsyncEventRequestResponse.ChunkEvents(events, maxBytes, "t", "s", "c");

        // Assert - should be 3 chunks (one per event)
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.Single(c.events));
        Assert.True(chunks[^1].complete);
        Assert.False(chunks[0].complete);
        Assert.False(chunks[1].complete);
    }

    #endregion

    #region ChunkEvents - Metadata Preservation

    [Fact]
    public void ChunkEvents_PreservesTopicAndSubtopicAndCorrelationId()
    {
        // Arrange
        var events = new List<Event> { CreateTestEvent() };

        // Act
        var chunks = AsyncEventRequestResponse.ChunkEvents(events, 900_000, "my-topic", "my-subtopic", "my-correlation");

        // Assert
        Assert.All(chunks, c =>
        {
            Assert.Equal("my-topic", c.topic);
            Assert.Equal("my-subtopic", c.subtopic);
            Assert.Equal("my-correlation", c.correlationId);
        });
    }

    #endregion

    #region AsyncEventRequest Serialization

    [Fact]
    public void AsyncEventRequest_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var request = new AsyncEventRequest
        {
            topic = "TestService_EventRequest",
            responseTopic = "TestService_EventRequestResponse",
            subtopic = "",
            aggregateRootIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            pointInTime = DateTime.UtcNow.AddHours(-1),
            correlationId = Guid.NewGuid().ToString()
        };

        // Act
        var json = JsonConvert.SerializeObject(request);
        var deserialized = JsonConvert.DeserializeObject<AsyncEventRequest>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.topic, deserialized.topic);
        Assert.Equal(request.responseTopic, deserialized.responseTopic);
        Assert.Equal(request.subtopic, deserialized.subtopic);
        Assert.Equal(request.aggregateRootIds.Count, deserialized.aggregateRootIds.Count);
        Assert.Equal(request.correlationId, deserialized.correlationId);
    }

    [Fact]
    public void AsyncEventRequest_NullPointInTime_SerializesCorrectly()
    {
        // Arrange
        var request = new AsyncEventRequest
        {
            topic = "Svc_EventRequest",
            subtopic = "",
            aggregateRootIds = new List<Guid> { Guid.NewGuid() },
            pointInTime = null,
            correlationId = "abc-123"
        };

        // Act
        var json = JsonConvert.SerializeObject(request);
        var deserialized = JsonConvert.DeserializeObject<AsyncEventRequest>(json);

        // Assert
        Assert.Null(deserialized.pointInTime);
        Assert.Equal("abc-123", deserialized.correlationId);
    }

    #endregion

    #region AsyncEventRequestResponse Serialization

    [Fact]
    public void AsyncEventRequestResponse_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var response = new AsyncEventRequestResponse
        {
            topic = "TestService_EventRequest",
            subtopic = "",
            complete = true,
            events = new List<Event> { CreateTestEvent() },
            correlationId = "corr-ser"
        };

        // Act
        var json = JsonConvert.SerializeObject(response);
        var deserialized = JsonConvert.DeserializeObject<AsyncEventRequestResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.topic, deserialized.topic);
        Assert.True(deserialized.complete);
        Assert.Single(deserialized.events);
        Assert.Equal("corr-ser", deserialized.correlationId);
    }

    [Fact]
    public void AsyncEventRequestResponse_EmptyEvents_SerializesCorrectly()
    {
        // Arrange
        var response = new AsyncEventRequestResponse
        {
            topic = "t",
            subtopic = "s",
            complete = false,
            events = new List<Event>(),
            correlationId = "c"
        };

        // Act
        var json = JsonConvert.SerializeObject(response);
        var deserialized = JsonConvert.DeserializeObject<AsyncEventRequestResponse>(json);

        // Assert
        Assert.Empty(deserialized.events);
        Assert.False(deserialized.complete);
    }

    #endregion
}
