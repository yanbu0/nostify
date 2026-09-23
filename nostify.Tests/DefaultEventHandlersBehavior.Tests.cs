using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Covers event-handler behavior that is independent of live Cosmos query execution.
/// </summary>
public sealed class DefaultEventHandlersBehaviorTests
{
    private readonly Mock<INostify> _nostify = new();

    public DefaultEventHandlersBehaviorTests()
    {
        // Disable implicit retries so each test exercises the direct persistence path.
        _nostify.SetupGet(n => n.DefaultRetryOptions).Returns((RetryOptions?)null);
        _nostify
            .Setup(n => n.HandleUndeliverableAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEvent>(),
                It.IsAny<ErrorCommand?>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAggregateEventAsync_WithPayloadTargetId_ReadsSelectedAggregate()
    {
        Guid eventAggregateId = Guid.NewGuid();
        Guid selectedAggregateId = Guid.NewGuid();
        Event @event = CreateEvent(eventAggregateId, new { targetId = selectedAggregateId, name = "updated" });
        Mock<Container> container = CreateReadableContainer(new TestAggregate { id = selectedAggregateId });
        _nostify
            .Setup(n => n.GetCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(container.Object);

        TestAggregate? result = await DefaultEventHandlers.HandleAggregateEventAsync<TestAggregate>(
            _nostify.Object,
            CreateTriggerEvent(@event),
            idToApplyToPropertyName: "targetId");

        Assert.NotNull(result);
        Assert.Equal(selectedAggregateId, result.id);
        container.Verify(c => c.ReadItemAsync<TestAggregate>(
            selectedAggregateId.ToString(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleProjectionEventAsync_WithPayloadTargetId_ReadsAndInitializesSelectedProjection()
    {
        Guid eventAggregateId = Guid.NewGuid();
        Guid selectedProjectionId = Guid.NewGuid();
        Event @event = CreateEvent(eventAggregateId, new { targetId = selectedProjectionId, name = "updated" });
        var projection = new TestProjection { id = selectedProjectionId };
        Mock<Container> container = CreateReadableContainer(projection);
        var initializer = new Mock<IProjectionInitializer>();
        initializer
            .Setup(i => i.InitAsync(
                It.IsAny<List<TestProjection>>(),
                _nostify.Object,
                null,
                null,
                null))
            .ReturnsAsync((List<TestProjection> projections, INostify _, HttpClient? _, DateTime? _, RetryOptions? _) => projections);
        _nostify.SetupGet(n => n.ProjectionInitializer).Returns(initializer.Object);
        _nostify
            .Setup(n => n.GetProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ReturnsAsync(container.Object);

        TestProjection? result = await DefaultEventHandlers.HandleProjectionEventAsync<TestProjection>(
            _nostify.Object,
            CreateTriggerEvent(@event),
            httpClient: null,
            idToApplyToPropertyName: "targetId");

        Assert.Same(projection, result);
        container.Verify(c => c.ReadItemAsync<TestProjection>(
            selectedProjectionId.ToString(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        initializer.Verify(i => i.InitAsync(
            It.Is<List<TestProjection>>(items => items.Count == 1 && ReferenceEquals(items[0], projection)),
            _nostify.Object,
            null,
            null,
            null), Times.Once);
    }

    [Fact]
    public async Task HandleMultiApplyEventAsync_WhenEventIsFilteredOut_ReturnsZeroWithoutResolvingContainer()
    {
        Event @event = CreateEvent(Guid.NewGuid(), new { name = "ignored" });

        int updated = await DefaultEventHandlers.HandleMultiApplyEventAsync<TestProjection>(
            _nostify.Object,
            CreateTriggerEvent(@event),
            projection => projection.id,
            eventTypeFilter: "DifferentEventType");

        Assert.Equal(0, updated);
        _nostify.Verify(
            n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()),
            Times.Never);
        _nostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IEvent>(),
            It.IsAny<ErrorCommand?>()), Times.Never);
    }

    [Fact]
    public async Task HandleMultiApplyEventAsync_WhenContainerResolutionFails_ReportsEventAndRethrows()
    {
        Event @event = CreateEvent(Guid.NewGuid(), new { name = "failed" });
        var expected = new InvalidOperationException("container unavailable");
        _nostify
            .Setup(n => n.GetBulkProjectionContainerAsync<TestProjection>(It.IsAny<string>()))
            .ThrowsAsync(expected);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DefaultEventHandlers.HandleMultiApplyEventAsync<TestProjection>(
                _nostify.Object,
                CreateTriggerEvent(@event),
                projection => projection.id));

        Assert.Same(expected, actual);
        _nostify.Verify(n => n.HandleUndeliverableAsync(
            "HandleMultiApplyEventAsync:P",
            expected.Message,
            It.Is<IEvent>(reported => reported.id == @event.id),
            It.IsAny<ErrorCommand?>()), Times.Once);
    }

    private static Event CreateEvent(Guid aggregateId, object payload)
    {
        return new Event(
            new BehaviorEventType(),
            aggregateId,
            payload,
            partitionKey: Guid.NewGuid());
    }

    private static NostifyKafkaTriggerEvent CreateTriggerEvent(Event @event)
    {
        return new NostifyKafkaTriggerEvent
        {
            Topic = "behavior",
            Key = @event.aggregateRootId.ToString(),
            Value = JsonConvert.SerializeObject(@event, SerializationSettings.NostifyDefault),
            Headers = []
        };
    }

    private static Mock<Container> CreateReadableContainer<T>(T value)
        where T : NostifyObject
    {
        var response = new Mock<ItemResponse<T>>();
        response.SetupGet(r => r.Resource).Returns(value);
        var container = new Mock<Container>();
        container
            .Setup(c => c.ReadItemAsync<T>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        container
            .Setup(c => c.PatchItemAsync<T>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        return container;
    }

    private sealed class BehaviorEventType : EventType
    {
        public BehaviorEventType()
            : base("BehaviorUpdated", false, false)
        {
        }
    }
}
