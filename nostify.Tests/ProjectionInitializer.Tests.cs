using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace nostify.Tests;

public class ProjectionInitializerTests
{
    [Fact]
    public void INostify_InitContainerAsync_DefaultLoopSize_ShouldBe100()
    {
        var method = typeof(INostify)
            .GetMethods()
            .Single(m => m.Name == nameof(INostify.InitContainerAsync) && m.GetParameters().Length == 2);

        var loopSizeParameter = method.GetParameters().Single(p => p.Name == "loopSize");

        Assert.True(loopSizeParameter.IsOptional);
        Assert.Equal(100, Assert.IsType<int>(loopSizeParameter.DefaultValue));
    }

    [Fact]
    public void Nostify_InitContainerAsync_DefaultLoopSize_ShouldBe100()
    {
        var method = typeof(Nostify)
            .GetMethods()
            .Single(m => m.Name == nameof(Nostify.InitContainerAsync) && m.GetParameters().Length == 2);

        var loopSizeParameter = method.GetParameters().Single(p => p.Name == "loopSize");

        Assert.True(loopSizeParameter.IsOptional);
        Assert.Equal(100, Assert.IsType<int>(loopSizeParameter.DefaultValue));
    }

    [Fact]
    public void ProjectionInitializer_InitContainerAsync_DefaultLoopSize_ShouldBe100()
    {
        var interfaceMethod = typeof(IProjectionInitializer)
            .GetMethods()
            .Single(m => m.Name == nameof(IProjectionInitializer.InitContainerAsync));
        var interfaceLoopSizeParameter = interfaceMethod.GetParameters().Single(p => p.Name == "loopSize");

        Assert.True(interfaceLoopSizeParameter.IsOptional);
        Assert.Equal(100, Assert.IsType<int>(interfaceLoopSizeParameter.DefaultValue));

        var implementationMethods = typeof(ProjectionInitializer)
            .GetMethods()
            .Where(m => m.Name == nameof(ProjectionInitializer.InitContainerAsync))
            .ToList();

        Assert.NotEmpty(implementationMethods);
        Assert.All(implementationMethods, method =>
        {
            var loopSizeParameter = method.GetParameters().Single(p => p.Name == "loopSize");
            Assert.True(loopSizeParameter.IsOptional);
            Assert.Equal(100, Assert.IsType<int>(loopSizeParameter.DefaultValue));
        });
    }

    [Fact]
    public void InternalConstructor_RejectsNullInfrastructureAdapters()
    {
        Func<Container, RetryOptions, IRetryableContainer> factory =
            (_, _) => Mock.Of<IRetryableContainer>();
        Func<TimeSpan, Task> delay = _ => Task.CompletedTask;

        Assert.Throws<ArgumentNullException>(() =>
            new ProjectionInitializer(null!, factory, delay));
        Assert.Throws<ArgumentNullException>(() =>
            new ProjectionInitializer(InMemoryQueryExecutor.Default, null!, delay));
        Assert.Throws<ArgumentNullException>(() =>
            new ProjectionInitializer(InMemoryQueryExecutor.Default, factory, null!));
    }

    [Fact]
    public async Task InitAsync_ExistingProjections_AppliesGroupedEventsAndPersistsWithSuppliedRetryOptions()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        DateTime pointInTime = new(2025, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        var projections = new List<InitializerProjection>
        {
            new() { id = firstId, requestExternalData = true },
            new() { id = secondId, requestExternalData = false }
        };
        var projectionContainer = new Mock<Container>();
        var nostify = new Mock<INostify>();
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<InitializerProjection>("/tenantId"))
            .ReturnsAsync(projectionContainer.Object);
        var retryable = new Mock<IRetryableContainer>();
        List<InitializerProjection>? persisted = null;
        retryable
            .Setup(r => r.DoBulkUpsertAsync(
                It.IsAny<List<InitializerProjection>>(),
                It.IsAny<Func<InitializerProjection, Exception, Task>>()))
            .Callback<List<InitializerProjection>, Func<InitializerProjection, Exception, Task>?>(
                (items, _) => persisted = items.ToList())
            .Returns(Task.CompletedTask);
        var retryOptions = new RetryOptions { MaxRetries = 7 };
        RetryOptions? observedOptions = null;
        var initializer = CreateInitializer(
            retryable.Object,
            (_, options) => observedOptions = options);

        List<InitializerProjection> result = await initializer.InitAsync(
            projections,
            nostify.Object,
            pointInTime: pointInTime,
            retryOptions: retryOptions);

        Assert.Equal(projections.Count, result.Count);
        Assert.Same(projections[0], result[0]);
        Assert.Same(projections[1], result[1]);
        Assert.All(result, projection => Assert.True(projection.initialized));
        Assert.Equal(2, result[0].appliedEventCount);
        Assert.Equal("second-2025-04-05", result[0].name);
        Assert.Equal(0, result[1].appliedEventCount);
        Assert.Same(retryOptions, observedOptions);
        Assert.Equal(result, persisted);
    }

    [Fact]
    public async Task InitAsync_WithoutRetryOptions_CreatesDefaultOptionsAndPersistsEmptyInput()
    {
        var projectionContainer = new Mock<Container>();
        var nostify = new Mock<INostify>();
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<InitializerProjection>("/tenantId"))
            .ReturnsAsync(projectionContainer.Object);
        var retryable = new Mock<IRetryableContainer>();
        retryable
            .Setup(r => r.DoBulkUpsertAsync(
                It.IsAny<List<InitializerProjection>>(),
                It.IsAny<Func<InitializerProjection, Exception, Task>>()))
            .Returns(Task.CompletedTask);
        RetryOptions? observedOptions = null;
        var initializer = CreateInitializer(
            retryable.Object,
            (_, options) => observedOptions = options);

        List<InitializerProjection> result = await initializer.InitAsync(
            new List<InitializerProjection>(),
            nostify.Object,
            pointInTime: null,
            retryOptions: null);

        Assert.Empty(result);
        Assert.NotNull(observedOptions);
        retryable.Verify(r => r.DoBulkUpsertAsync(
            It.Is<List<InitializerProjection>>(items => items.Count == 0),
            It.IsAny<Func<InitializerProjection, Exception, Task>>()), Times.Once);
    }

    [Fact]
    public async Task InitAsync_WithIds_FiltersAggregatesConvertsThemAndDelegatesToProjectionInitialization()
    {
        Guid includedId = Guid.NewGuid();
        Guid excludedId = Guid.NewGuid();
        var aggregates = new List<InitializerAggregate>
        {
            new() { id = includedId, name = "included", requestExternalData = false },
            new() { id = excludedId, name = "excluded", requestExternalData = false }
        };
        Mock<Container> aggregateContainer = CosmosTestHelpers.CreateMockContainer(aggregates);
        var projectionContainer = new Mock<Container>();
        var nostify = new Mock<INostify>();
        nostify.Setup(n => n.GetCurrentStateContainerAsync<InitializerAggregate>("/tenantId"))
            .ReturnsAsync(aggregateContainer.Object);
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<InitializerProjection>("/tenantId"))
            .ReturnsAsync(projectionContainer.Object);
        var retryable = new Mock<IRetryableContainer>();
        List<InitializerProjection>? persisted = null;
        retryable
            .Setup(r => r.DoBulkUpsertAsync(
                It.IsAny<List<InitializerProjection>>(),
                It.IsAny<Func<InitializerProjection, Exception, Task>>()))
            .Callback<List<InitializerProjection>, Func<InitializerProjection, Exception, Task>?>(
                (items, _) => persisted = items.ToList())
            .Returns(Task.CompletedTask);
        var initializer = CreateInitializer(retryable.Object);

        List<InitializerProjection> result = await initializer.InitAsync<InitializerProjection, InitializerAggregate>(
            new List<Guid> { includedId },
            nostify.Object,
            pointInTime: null);

        InitializerProjection projection = Assert.Single(result);
        Assert.Equal(includedId, projection.id);
        Assert.Equal("included", projection.name);
        Assert.True(projection.initialized);
        Assert.Equal(result, persisted);
    }

    [Fact]
    public async Task InitAsync_SingleId_UsesTheRequestedAggregateId()
    {
        Guid requestedId = Guid.NewGuid();
        var aggregates = new List<InitializerAggregate>
        {
            new() { id = requestedId, name = "requested" },
            new() { id = Guid.NewGuid(), name = "other" }
        };
        Mock<Container> aggregateContainer = CosmosTestHelpers.CreateMockContainer(aggregates);
        var projectionContainer = new Mock<Container>();
        var nostify = new Mock<INostify>();
        nostify.Setup(n => n.GetCurrentStateContainerAsync<InitializerAggregate>("/tenantId"))
            .ReturnsAsync(aggregateContainer.Object);
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<InitializerProjection>("/tenantId"))
            .ReturnsAsync(projectionContainer.Object);
        var retryable = CreateSuccessfulRetryable();
        var initializer = CreateInitializer(retryable.Object);

        List<InitializerProjection> result = await initializer.InitAsync<InitializerProjection, InitializerAggregate>(
            requestedId,
            nostify.Object,
            pointInTime: null);

        Assert.Equal(requestedId, Assert.Single(result).id);
    }

    [Fact]
    public async Task InitContainerAsync_DeletesExistingDataBatchesActiveAggregatesAndHonorsPointInTime()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Guid thirdId = Guid.NewGuid();
        Guid deletedId = Guid.NewGuid();
        DateTime cutoff = new(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var aggregates = new List<InitializerAggregate>
        {
            new() { id = firstId },
            new() { id = secondId },
            new() { id = thirdId },
            new() { id = deletedId, isDeleted = true }
        };
        var events = new List<Event>
        {
            CreateEvent(firstId, "first", cutoff.AddDays(-2)),
            CreateEvent(secondId, "second-old", cutoff.AddDays(-1)),
            CreateEvent(secondId, "second-new", cutoff.AddDays(1)),
            CreateEvent(thirdId, "third", cutoff),
            CreateEvent(deletedId, "deleted", cutoff.AddDays(-1))
        };
        Mock<Container> aggregateContainer = CosmosTestHelpers.CreateMockContainer(aggregates);
        Mock<Container> eventContainer = CosmosTestHelpers.CreateMockContainer(events);
        var projectionContainer = new Mock<Container>();
        var nostify = new Mock<INostify>();
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<InitializerProjection>("/customPk"))
            .ReturnsAsync(projectionContainer.Object);
        nostify.Setup(n => n.GetEventStoreContainerAsync(false))
            .ReturnsAsync(eventContainer.Object);
        nostify.Setup(n => n.GetCurrentStateContainerAsync<InitializerAggregate>("/customPk"))
            .ReturnsAsync(aggregateContainer.Object);
        var retryable = new Mock<IRetryableContainer>();
        var persistedBatches = new List<List<InitializerProjection>>();
        retryable
            .Setup(r => r.DoBulkUpsertAsync(
                It.IsAny<List<InitializerProjection>>(),
                It.IsAny<Func<InitializerProjection, Exception, Task>>()))
            .Callback<List<InitializerProjection>, Func<InitializerProjection, Exception, Task>?>(
                (items, _) => persistedBatches.Add(items.ToList()))
            .Returns(Task.CompletedTask);
        var initializer = new DeleteTrackingProjectionInitializer(
            InMemoryQueryExecutor.Default,
            (_, _) => retryable.Object,
            _ => Task.CompletedTask);

        await initializer.InitContainerAsync<InitializerProjection, InitializerAggregate>(
            nostify.Object,
            partitionKeyPath: "/customPk",
            loopSize: 2,
            pointInTime: cutoff);

        Assert.Equal(1, initializer.DeleteCallCount);
        Assert.Same(projectionContainer.Object, initializer.DeletedContainer);
        Assert.Equal(new[] { 2, 1 }, persistedBatches.Select(batch => batch.Count));
        List<InitializerProjection> persisted = persistedBatches.SelectMany(batch => batch).ToList();
        Assert.Equal(3, persisted.Count);
        Assert.DoesNotContain(persisted, projection => projection.id == deletedId);
        Assert.Equal("second-old", persisted.Single(projection => projection.id == secondId).name);
        Assert.All(persisted, projection => Assert.True(projection.initialized));
    }

    [Fact]
    public async Task InitAllUninitialized_InitializesItemsThenPerformsOneDeterministicStabilizationCheck()
    {
        var projections = new List<InitializerProjection>
        {
            new() { id = Guid.NewGuid(), name = "first" },
            new() { id = Guid.NewGuid(), name = "second" },
            new() { id = Guid.NewGuid(), name = "already initialized", initialized = true }
        };
        Mock<Container> queryContainer = CosmosTestHelpers.CreateMockContainer(projections);
        var persistenceContainer = new Mock<Container>();
        var nostify = new Mock<INostify>();
        nostify.Setup(n => n.GetProjectionContainerAsync<InitializerProjection>("/tenantId"))
            .ReturnsAsync(queryContainer.Object);
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<InitializerProjection>("/tenantId"))
            .ReturnsAsync(persistenceContainer.Object);
        var retryable = new Mock<IRetryableContainer>();
        List<InitializerProjection>? persisted = null;
        retryable
            .Setup(r => r.DoBulkUpsertAsync(
                It.IsAny<List<InitializerProjection>>(),
                It.IsAny<Func<InitializerProjection, Exception, Task>>()))
            .Callback<List<InitializerProjection>, Func<InitializerProjection, Exception, Task>?>(
                (items, _) => persisted = items.ToList())
            .Returns(Task.CompletedTask);
        var delays = new List<TimeSpan>();
        var initializer = new ProjectionInitializer(
            InMemoryQueryExecutor.Default,
            (_, _) => retryable.Object,
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        await initializer.InitAllUninitialized<InitializerProjection>(
            nostify.Object,
            pointInTime: null);

        Assert.NotNull(persisted);
        Assert.Equal(2, persisted!.Count);
        Assert.All(persisted, projection => Assert.True(projection.initialized));
        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, delays);
    }

    [Fact]
    public async Task CompatibilityOverloads_DelegateToPointInTimeImplementations()
    {
        Guid id = Guid.NewGuid();
        var aggregates = new List<InitializerAggregate> { new() { id = id, name = "aggregate" } };
        Mock<Container> aggregateContainer = CosmosTestHelpers.CreateMockContainer(aggregates);
        Mock<Container> emptyProjectionQuery = CosmosTestHelpers.CreateMockContainer(new List<InitializerProjection>());
        Mock<Container> emptyEventQuery = CosmosTestHelpers.CreateMockContainer(new List<Event>());
        var projectionContainer = new Mock<Container>();
        var nostify = new Mock<INostify>();
        nostify.Setup(n => n.GetCurrentStateContainerAsync<InitializerAggregate>("/tenantId"))
            .ReturnsAsync(aggregateContainer.Object);
        nostify.Setup(n => n.GetBulkProjectionContainerAsync<InitializerProjection>("/tenantId"))
            .ReturnsAsync(projectionContainer.Object);
        nostify.Setup(n => n.GetProjectionContainerAsync<InitializerProjection>("/tenantId"))
            .ReturnsAsync(emptyProjectionQuery.Object);
        nostify.Setup(n => n.GetEventStoreContainerAsync(false))
            .ReturnsAsync(emptyEventQuery.Object);
        var retryable = CreateSuccessfulRetryable();
        var initializer = new DeleteTrackingProjectionInitializer(
            InMemoryQueryExecutor.Default,
            (_, _) => retryable.Object,
            _ => Task.CompletedTask);

        Assert.Single(await initializer.InitAsync<InitializerProjection, InitializerAggregate>(id, nostify.Object, null));
        Assert.Single(await initializer.InitAsync<InitializerProjection, InitializerAggregate>(new List<Guid> { id }, nostify.Object, null));
        Assert.Single(await initializer.InitAsync(new List<InitializerProjection> { new() { id = id } }, nostify.Object, null));
        await initializer.InitContainerAsync<InitializerProjection, InitializerAggregate>(nostify.Object, null, "/tenantId", 100);
        await initializer.InitAllUninitialized<InitializerProjection>(nostify.Object, null, 10);

        Assert.Equal(1, initializer.DeleteCallCount);
    }

    private static ProjectionInitializer CreateInitializer(
        IRetryableContainer retryableContainer,
        Action<Container, RetryOptions>? onFactoryCall = null)
    {
        return new ProjectionInitializer(
            InMemoryQueryExecutor.Default,
            (container, options) =>
            {
                onFactoryCall?.Invoke(container, options);
                return retryableContainer;
            },
            _ => Task.CompletedTask);
    }

    private static Mock<IRetryableContainer> CreateSuccessfulRetryable()
    {
        var retryable = new Mock<IRetryableContainer>();
        retryable
            .Setup(r => r.DoBulkUpsertAsync(
                It.IsAny<List<InitializerProjection>>(),
                It.IsAny<Func<InitializerProjection, Exception, Task>>()))
            .Returns(Task.CompletedTask);
        return retryable;
    }

    private static Event CreateEvent(Guid id, string name, DateTime timestamp)
    {
        return new Event(InitializerEventType.Instance, id, new { id, name })
        {
            timestamp = timestamp
        };
    }

    public sealed class InitializerEventType : EventType
    {
        public static InitializerEventType Instance { get; } = new();

        public InitializerEventType()
            : base("InitializerEvent", false, false)
        {
        }
    }

    public sealed class InitializerAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => "InitializerAggregate";
        public static string currentStateContainerName => "InitializerAggregateCurrentState";
        public bool isDeleted { get; set; }
        public string name { get; set; } = string.Empty;
        public bool requestExternalData { get; set; }
    }

    public sealed class InitializerProjection : NostifyObject, IProjection, IHasExternalData<InitializerProjection>
    {
        public static string containerName => "InitializerProjection";
        public bool initialized { get; set; }
        public string name { get; set; } = string.Empty;
        public bool requestExternalData { get; set; }
        public int appliedEventCount { get; set; }

        protected override void Apply(EventType eventType, IEvent eventToApply)
        {
            JObject payload = JObject.FromObject(eventToApply.payload);
            id = payload.Value<Guid?>(nameof(id)) ?? id;
            name = payload.Value<string>(nameof(name)) ?? name;
            appliedEventCount++;
        }

        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
            List<InitializerProjection> projectionsToInit,
            INostify nostify,
            HttpClient? httpClient = null,
            DateTime? pointInTime = null)
        {
            string date = (pointInTime ?? DateTime.UnixEpoch).ToString("yyyy-MM-dd");
            List<ExternalDataEvent> events = projectionsToInit
                .Where(projection => projection.requestExternalData)
                .SelectMany(projection => new[]
                {
                    new ExternalDataEvent(
                        projection.id,
                        new List<Event>
                        {
                            new(InitializerEventType.Instance, projection.id, new { name = $"first-{date}" })
                        }),
                    new ExternalDataEvent(
                        projection.id,
                        new List<Event>
                        {
                            new(InitializerEventType.Instance, projection.id, new { name = $"second-{date}" })
                        })
                })
                .ToList();

            return Task.FromResult(events);
        }
    }

    private sealed class DeleteTrackingProjectionInitializer : ProjectionInitializer
    {
        public DeleteTrackingProjectionInitializer(
            IQueryExecutor queryExecutor,
            Func<Container, RetryOptions, IRetryableContainer> retryableContainerFactory,
            Func<TimeSpan, Task> delayAsync)
            : base(queryExecutor, retryableContainerFactory, delayAsync)
        {
        }

        public int DeleteCallCount { get; private set; }
        public Container? DeletedContainer { get; private set; }

        internal override Task<int> DeleteAllProjectionsAsync<P>(Container container)
        {
            DeleteCallCount++;
            DeletedContainer = container;
            return Task.FromResult(0);
        }
    }
}
