using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Covers handled-event discovery and the public filtering configuration contract.
/// </summary>
public class HandledEventTypeResolverTests
{
    [Fact]
    public void GetOrBuild_AttributeHandlers_ReturnsDeterminateLogicalNames()
    {
        var resolution = HandledEventTypeResolver.GetOrBuild(typeof(AttributeProjection));

        Assert.True(resolution.IsDeterminate);
        Assert.Equal(2, resolution.EventTypeNames.Count);
        Assert.Contains("Create_FilterTest", resolution.EventTypeNames);
        Assert.Contains("Update_FilterTest", resolution.EventTypeNames);
    }

    [Fact]
    public void GetOrBuild_LegacySpecificOverload_ReturnsDeterminateLogicalName()
    {
        var resolution = HandledEventTypeResolver.GetOrBuild(typeof(LegacyOverloadProjection));

        Assert.True(resolution.IsDeterminate);
        Assert.Contains("Create_FilterTest", resolution.EventTypeNames);
    }

    [Fact]
    public void GetOrBuild_CatchAllOverride_IsIndeterminate()
    {
        var resolution = HandledEventTypeResolver.GetOrBuild(typeof(CatchAllProjection));

        Assert.False(resolution.IsDeterminate);
        Assert.Contains("catch-all", resolution.IndeterminateReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_RemoveNonAppliedEvents_DefaultsToTrueAndSupportsOptOut()
    {
        var nostify = new Moq.Mock<INostify>().Object;
        var projections = new List<AttributeProjection>();

        var defaultFactory = new ExternalDataEventFactory<AttributeProjection>(nostify, projections);
        var optOutFactory = new ExternalDataEventFactory<AttributeProjection>(
            nostify,
            projections,
            removeNonAppliedEvents: false);

        Assert.True(defaultFactory.RemoveNonAppliedEvents);
        Assert.False(optOutFactory.RemoveNonAppliedEvents);
    }

    [Fact]
    public async Task GetEventsAsync_FilterEnabled_RemovesEventsWithoutHandlers()
    {
        Guid projectionId = Guid.NewGuid();
        Guid foreignId = Guid.NewGuid();
        var projection = new AttributeProjection { id = projectionId, ForeignId = foreignId };
        var events = CreateMixedEvents(foreignId);
        var nostify = CreateNostify(events);
        var factory = new ExternalDataEventFactory<AttributeProjection>(
            nostify.Object,
            new List<AttributeProjection> { projection },
            queryExecutor: InMemoryQueryExecutor.Default);

        List<ExternalDataEvent> result = await factory
            .WithSameServiceIdSelectors(p => p.ForeignId)
            .GetEventsAsync();

        Event retainedEvent = Assert.Single(Assert.Single(result).events);
        Assert.Equal("Create_FilterTest", retainedEvent.eventType.name);
    }

    [Fact]
    public async Task GetEventsAsync_FilterDisabled_RetainsEventsWithoutHandlers()
    {
        Guid foreignId = Guid.NewGuid();
        var projection = new AttributeProjection { id = Guid.NewGuid(), ForeignId = foreignId };
        var nostify = CreateNostify(CreateMixedEvents(foreignId));
        var factory = new ExternalDataEventFactory<AttributeProjection>(
            nostify.Object,
            new List<AttributeProjection> { projection },
            queryExecutor: InMemoryQueryExecutor.Default,
            removeNonAppliedEvents: false);

        List<ExternalDataEvent> result = await factory
            .WithSameServiceIdSelectors(p => p.ForeignId)
            .GetEventsAsync();

        Assert.Equal(2, Assert.Single(result).events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_IndeterminateCatchAll_RetainsEventsAndLogsWarning()
    {
        Guid foreignId = Guid.NewGuid();
        var projection = new CatchAllProjection { id = Guid.NewGuid(), ForeignId = foreignId };
        var nostify = CreateNostify(CreateMixedEvents(foreignId));
        var logger = new Moq.Mock<ILogger>();
        logger.Setup(candidate => candidate.IsEnabled(LogLevel.Warning)).Returns(true);
        nostify.SetupGet(candidate => candidate.Logger).Returns(logger.Object);
        var factory = new ExternalDataEventFactory<CatchAllProjection>(
            nostify.Object,
            new List<CatchAllProjection> { projection },
            queryExecutor: InMemoryQueryExecutor.Default);

        List<ExternalDataEvent> result = await factory
            .WithSameServiceIdSelectors(candidate => candidate.ForeignId)
            .GetEventsAsync();

        Assert.Equal(2, Assert.Single(result).events.Count);
        logger.Verify(
            candidate => candidate.Log(
                LogLevel.Warning,
                Moq.It.IsAny<EventId>(),
                Moq.It.Is<Moq.It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("will not be filtered", StringComparison.Ordinal)),
                Moq.It.IsAny<Exception?>(),
                Moq.It.IsAny<Func<Moq.It.IsAnyType, Exception?, string>>()),
            Moq.Times.Once);
    }

    private static List<Event> CreateMixedEvents(Guid aggregateRootId)
    {
        return new List<Event>
        {
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggregateRootId,
                timestamp = DateTime.UtcNow.AddMinutes(-2),
                eventType = new CreateFilterTest(),
                payload = new { }
            },
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggregateRootId,
                timestamp = DateTime.UtcNow.AddMinutes(-1),
                eventType = new UnhandledFilterTest(),
                payload = new { }
            }
        };
    }

    private static Moq.Mock<INostify> CreateNostify(List<Event> events)
    {
        var nostify = new Moq.Mock<INostify>();
        var container = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        nostify.Setup(n => n.GetEventStoreContainerAsync(Moq.It.IsAny<bool>()))
            .Returns(Task.FromResult(container.Object));
        return nostify;
    }

    private sealed class CreateFilterTest : EventType
    {
        public CreateFilterTest() : base("Create_FilterTest") { }
    }

    private sealed class UpdateFilterTest : EventType
    {
        public UpdateFilterTest() : base("Update_FilterTest") { }
    }

    private sealed class UnhandledFilterTest : EventType
    {
        public UnhandledFilterTest() : base("Unhandled_FilterTest") { }
    }

    private abstract class ProjectionBase : NostifyObject, IProjection
    {
        public bool initialized { get; set; }
        public static string containerName => "HandledEventTypeResolverTests";

        public Task<List<ExternalDataEvent>> GetExternalDataEvents(
            INostify nostify,
            System.Net.Http.HttpClient? httpClient = null,
            DateTime? pointInTime = null) => Task.FromResult(new List<ExternalDataEvent>());

        public void ApplyExternalDataEvents(List<ExternalDataEvent> externalDataEvents)
        {
            // No behavior is needed for metadata discovery tests.
        }
    }

    private sealed class AttributeProjection : ProjectionBase
    {
        public Guid ForeignId { get; set; }

        [ApplyEvents(typeof(CreateFilterTest))]
        private void ApplyCreate(IEvent evt) { }

        [ApplyEvents("Update_FilterTest")]
        private void ApplyUpdate(IEvent evt) { }
    }

    private sealed class LegacyOverloadProjection : ProjectionBase
    {
        private void Apply(CreateFilterTest eventType, IEvent evt) { }
    }

    private sealed class CatchAllProjection : ProjectionBase
    {
        public Guid ForeignId { get; set; }

        protected override void Apply(EventType eventType, IEvent eventToApply)
        {
            // Deliberately accepts arbitrary event types.
        }
    }
}
