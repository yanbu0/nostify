using Confluent.Kafka;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;

namespace nostify.Tests;

[Collection(AsyncEventRequestEnvironmentCollection.Name)]
public class DefaultEventRequestHandlersTests
{
    [Fact]
    public async Task HandleAsyncEventRequestAsync_WithNullDependencies_Throws()
    {
        var nostify = new Mock<INostify>();
        var trigger = new NostifyKafkaTriggerEvent();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(
                null!,
                trigger,
                InMemoryQueryExecutor.Default));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(
                nostify.Object,
                null!,
                InMemoryQueryExecutor.Default));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(
                nostify.Object,
                trigger,
                (IQueryExecutor)null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task HandleAsyncEventRequestAsync_WithInvalidMessage_DoesNotQueryOrPublish(string message)
    {
        var (nostify, producer, logger) = CreateNostify();
        var trigger = new NostifyKafkaTriggerEvent { Value = message };

        await DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(
            nostify.Object,
            trigger,
            InMemoryQueryExecutor.Default,
            logger.Object);

        nostify.Verify(candidate => candidate.GetEventStoreContainerAsync(It.IsAny<bool>()), Times.Never);
        producer.Verify(candidate => candidate.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsyncEventRequestAsync_PublicOverload_InvalidMessage_DoesNotAccessCosmosOrKafka()
    {
        var (nostify, producer, logger) = CreateNostify();
        var trigger = new NostifyKafkaTriggerEvent { Value = "not-json" };

        await DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(
            nostify.Object,
            trigger,
            logger.Object);

        nostify.Verify(candidate => candidate.GetEventStoreContainerAsync(It.IsAny<bool>()), Times.Never);
        producer.Verify(candidate => candidate.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<Message<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsyncEventRequestAsync_QueriesFiltersAndPublishesToResponseTopic()
    {
        DateTime cutoff = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Guid requestedId = Guid.NewGuid();
        Guid otherId = Guid.NewGuid();
        var events = new List<Event>
        {
            CreateEvent(requestedId, cutoff.AddMinutes(-2), "Before"),
            CreateEvent(requestedId, cutoff.AddMinutes(2), "After"),
            CreateEvent(otherId, cutoff.AddMinutes(-3), "Other")
        };
        var (nostify, producer, logger) = CreateNostify(events);
        var published = new List<(string Topic, AsyncEventRequestResponse Response)>();
        producer.Setup(candidate => candidate.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, message, _) =>
            {
                published.Add((topic, JsonConvert.DeserializeObject<AsyncEventRequestResponse>(message.Value)!));
            })
            .ReturnsAsync(new DeliveryResult<string, string>());
        var request = new AsyncEventRequest
        {
            topic = "request-topic",
            responseTopic = "response-topic",
            subtopic = "tenant-a",
            correlationId = "correlation-1",
            aggregateRootIds = [requestedId],
            pointInTime = cutoff
        };

        await DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(
            nostify.Object,
            new NostifyKafkaTriggerEvent { Value = JsonConvert.SerializeObject(request) },
            InMemoryQueryExecutor.Default,
            logger.Object,
            maxMessageBytes: 900_000);

        var publication = Assert.Single(published);
        Assert.Equal("response-topic", publication.Topic);
        Assert.Equal("response-topic", publication.Response.topic);
        Assert.Equal("tenant-a", publication.Response.subtopic);
        Assert.Equal("correlation-1", publication.Response.correlationId);
        Assert.True(publication.Response.complete);
        Event returnedEvent = Assert.Single(publication.Response.events);
        Assert.Equal("Before", returnedEvent.command.name);
    }

    [Fact]
    public async Task HandleAsyncEventRequestAsync_UsesRequestTopicAndEnvironmentChunkLimit()
    {
        string? previousValue = Environment.GetEnvironmentVariable("AsyncEventRequestMaxMessageBytes");
        Environment.SetEnvironmentVariable("AsyncEventRequestMaxMessageBytes", "1");
        try
        {
            Guid requestedId = Guid.NewGuid();
            var (nostify, producer, _) = CreateNostify(
            [
                CreateEvent(requestedId, DateTime.UtcNow.AddMinutes(-2), "First"),
                CreateEvent(requestedId, DateTime.UtcNow.AddMinutes(-1), "Second")
            ]);
            var published = new List<AsyncEventRequestResponse>();
            producer.Setup(candidate => candidate.ProduceAsync(
                    "request-topic",
                    It.IsAny<Message<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, Message<string, string>, CancellationToken>((_, message, _) =>
                    published.Add(JsonConvert.DeserializeObject<AsyncEventRequestResponse>(message.Value)!))
                .ReturnsAsync(new DeliveryResult<string, string>());
            var request = new AsyncEventRequest
            {
                topic = "request-topic",
                correlationId = "correlation-2",
                aggregateRootIds = [requestedId]
            };

            await DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(
                nostify.Object,
                new NostifyKafkaTriggerEvent { Value = JsonConvert.SerializeObject(request) },
                InMemoryQueryExecutor.Default,
                logger: null,
                maxMessageBytes: null);

            Assert.Equal(2, published.Count);
            Assert.False(published[0].complete);
            Assert.True(published[1].complete);
            Assert.All(published, response => Assert.Equal("request-topic", response.topic));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AsyncEventRequestMaxMessageBytes", previousValue);
        }
    }

    [Fact]
    public async Task HandleAsyncEventRequestAsync_WhenQueryFails_PublishesCompleteEmptyResponse()
    {
        var (nostify, producer, logger) = CreateNostify();
        nostify.Setup(candidate => candidate.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("query failed"));
        Message<string, string>? publishedMessage = null;
        producer.Setup(candidate => candidate.ProduceAsync(
                "response-topic",
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((_, message, _) => publishedMessage = message)
            .ReturnsAsync(new DeliveryResult<string, string>());
        var request = new AsyncEventRequest
        {
            topic = "request-topic",
            responseTopic = "response-topic",
            subtopic = null,
            correlationId = "correlation-error",
            aggregateRootIds = [Guid.NewGuid()]
        };

        await DefaultEventRequestHandlers.HandleAsyncEventRequestAsync(
            nostify.Object,
            new NostifyKafkaTriggerEvent { Value = JsonConvert.SerializeObject(request) },
            InMemoryQueryExecutor.Default,
            logger.Object);

        Assert.NotNull(publishedMessage);
        AsyncEventRequestResponse response =
            JsonConvert.DeserializeObject<AsyncEventRequestResponse>(publishedMessage!.Value)!;
        Assert.Equal("response-topic", response.topic);
        Assert.Equal(string.Empty, response.subtopic);
        Assert.Equal("correlation-error", response.correlationId);
        Assert.Empty(response.events);
        Assert.True(response.complete);
    }

    private static (Mock<INostify> Nostify, Mock<IProducer<string, string>> Producer, Mock<ILogger> Logger)
        CreateNostify(List<Event>? events = null)
    {
        var nostify = new Mock<INostify>();
        var producer = new Mock<IProducer<string, string>>();
        var logger = new Mock<ILogger>();
        logger.Setup(candidate => candidate.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        nostify.Setup(candidate => candidate.Logger).Returns(logger.Object);
        nostify.Setup(candidate => candidate.KafkaProducer).Returns(producer.Object);

        if (events != null)
        {
            // Use the standard in-memory queryable so the injected executor evaluates the
            // production filters and ordering rather than a Cosmos-specific mock provider.
            Mock<Container> container = CosmosTestHelpers.CreateMockContainer(events);
            nostify.Setup(candidate => candidate.GetEventStoreContainerAsync(It.IsAny<bool>()))
                .ReturnsAsync(container.Object);
        }

        return (nostify, producer, logger);
    }

    private static Event CreateEvent(Guid aggregateRootId, DateTime timestamp, string eventType)
    {
        EventType type = eventType switch
        {
            "Before" => new BeforeEventType(),
            "After" => new AfterEventType(),
            "Other" => new OtherEventType(),
            "First" => new FirstEventType(),
            "Second" => new SecondEventType(),
            _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unsupported test event type.")
        };

        return new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = aggregateRootId,
            partitionKey = aggregateRootId,
            timestamp = timestamp,
            eventType = type
        };
    }

    // Concrete, parameterless event definitions mirror production event metadata and permit
    // the response JSON converter to resolve each event after Kafka publication.
    private sealed class BeforeEventType() : EventType("Before");
    private sealed class AfterEventType() : EventType("After");
    private sealed class OtherEventType() : EventType("Other");
    private sealed class FirstEventType() : EventType("First");
    private sealed class SecondEventType() : EventType("Second");
}
