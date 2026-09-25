using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace nostify.Tests;

/// <summary>Behavioral tests for durable aggregate current-state initialization.</summary>
public class DurableCurrentStateInitializerTests
{
    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        var nostify = new Mock<INostify>();
        Func<Container, RetryOptions, IRetryableContainer> factory = (_, _) => Mock.Of<IRetryableContainer>();

        Assert.Throws<ArgumentNullException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(null!, "aggregate-init"));
        Assert.Throws<ArgumentException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(nostify.Object, " "));
        Assert.Throws<ArgumentException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(nostify.Object, "aggregate-init", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(nostify.Object, "aggregate-init", batchSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(nostify.Object, "aggregate-init", concurrentBatchCount: 0));
        Assert.Throws<OverflowException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(nostify.Object, "aggregate-init", batchSize: int.MaxValue, concurrentBatchCount: 2));
        Assert.Throws<ArgumentNullException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(
                nostify.Object, "aggregate-init", "/tenantId", 1, 1, null, null, null!, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new DurableCurrentStateInitializer<TestAggregate>(
                nostify.Object, "aggregate-init", "/tenantId", 1, 1, null, null,
                InMemoryQueryExecutor.Default, null!));
    }

    [Fact]
    public void PageInfo_ValidatesAndStoresPageNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DurableCurrentStatePageInfo(-1));
        Assert.Equal(0, new DurableCurrentStatePageInfo(0).PageNumber);
        Assert.Equal(7, new DurableCurrentStatePageInfo(7).PageNumber);
    }

    [Fact]
    public void DefaultTaskOptions_UsesRetryPolicy()
    {
        TaskOptions options = DurableCurrentStateInitializer<TestAggregate>.CreateDefaultTaskOptions();

        Assert.NotNull(options);
    }

    [Theory]
    [InlineData(OrchestrationRuntimeStatus.Running)]
    [InlineData(OrchestrationRuntimeStatus.Pending)]
    [InlineData(OrchestrationRuntimeStatus.Suspended)]
    public async Task StartOrchestration_WhenInstanceIsActive_ReturnsConflictWithoutScheduling(
        OrchestrationRuntimeStatus status)
    {
        var client = new Mock<DurableTaskClient>("test");
        client.Setup(candidate => candidate.GetInstanceAsync(
                "aggregate-init", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadata(status));
        var initializer = CreateInitializer(Mock.Of<INostify>());

        HttpResponseData response = await initializer.StartOrchestration(
            MockHttpRequestData.Create(), client.Object, "RebuildOrchestrator");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body);
        Assert.Contains("aggregate-init is already running", await reader.ReadToEndAsync());
        client.Verify(candidate => candidate.ScheduleNewOrchestrationInstanceAsync(
            It.IsAny<TaskName>(), It.IsAny<StartOrchestrationOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartOrchestration_WhenInstanceIsAbsent_SchedulesFixedInstance()
    {
        var client = new Mock<DurableTaskClient>("test");
        StartOrchestrationOptions? capturedOptions = null;
        client.Setup(candidate => candidate.GetInstanceAsync(
                "aggregate-init", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null);
        client.Setup(candidate => candidate.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(name => name.Name == "RebuildOrchestrator"),
                It.IsAny<StartOrchestrationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<TaskName, StartOrchestrationOptions?, CancellationToken>((_, options, _) => capturedOptions = options)
            .ReturnsAsync("aggregate-init");
        var initializer = CreateInitializer(Mock.Of<INostify>());

        HttpResponseData response = await initializer.StartOrchestration(
            CreateRequestWithUrl(), client.Object, "RebuildOrchestrator");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(capturedOptions);
        Assert.Equal("aggregate-init", capturedOptions!.InstanceId);
    }

    [Fact]
    public async Task CancelOrchestration_WhenInstanceIsAbsent_ReturnsOkWithoutMutation()
    {
        var client = new Mock<DurableTaskClient>("test");
        client.Setup(candidate => candidate.GetInstanceAsync(
                "aggregate-init", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null);
        var initializer = CreateInitializer(Mock.Of<INostify>());

        HttpResponseData response = await initializer.CancelOrchestration(
            MockHttpRequestData.Create(), client.Object);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.Verify(candidate => candidate.TerminateInstanceAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(candidate => candidate.PurgeInstanceAsync(
            It.IsAny<string>(), It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelOrchestration_WhenSuspended_ResumesTerminatesWaitsAndPurgesInOrder()
    {
        var client = new Mock<DurableTaskClient>("test");
        var suspended = CreateMetadata(OrchestrationRuntimeStatus.Suspended);
        var terminated = CreateMetadata(OrchestrationRuntimeStatus.Terminated);
        var calls = new List<string>();
        client.SetupSequence(candidate => candidate.GetInstanceAsync(
                "aggregate-init", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suspended)
            .ReturnsAsync(terminated);
        client.Setup(candidate => candidate.ResumeInstanceAsync(
                "aggregate-init", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("resume"))
            .Returns(Task.CompletedTask);
        client.Setup(candidate => candidate.TerminateInstanceAsync(
                "aggregate-init", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("terminate"))
            .Returns(Task.CompletedTask);
        client.Setup(candidate => candidate.WaitForInstanceCompletionAsync(
                "aggregate-init", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("wait"))
            .ReturnsAsync(terminated);
        client.Setup(candidate => candidate.PurgeInstanceAsync(
                "aggregate-init", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("purge"))
            .ReturnsAsync(new PurgeResult(1));
        var initializer = CreateInitializer(Mock.Of<INostify>());

        HttpResponseData response = await initializer.CancelOrchestration(
            MockHttpRequestData.Create(), client.Object);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "resume", "terminate", "wait", "purge" }, calls);
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body);
        Assert.Contains("aggregate-init cancelled", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CancelOrchestration_WhenCompleted_PurgesWithoutTerminating()
    {
        var client = new Mock<DurableTaskClient>("test");
        client.Setup(candidate => candidate.GetInstanceAsync(
                "aggregate-init", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadata(OrchestrationRuntimeStatus.Completed));
        client.Setup(candidate => candidate.PurgeInstanceAsync(
                "aggregate-init", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurgeResult(1));
        var initializer = CreateInitializer(Mock.Of<INostify>());

        await initializer.CancelOrchestration(MockHttpRequestData.Create(), client.Object);

        client.Verify(candidate => candidate.TerminateInstanceAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(candidate => candidate.PurgeInstanceAsync(
            "aggregate-init", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrchestrateInitAsync_DeletesPagesBatchesAndLogsProgress()
    {
        var context = new Mock<TaskOrchestrationContext>();
        var logger = new Mock<ILogger>();
        var ids = Enumerable.Range(1, 5).Select(CreateDeterministicGuid).ToList();
        var calls = new List<string>();
        var pageRequests = new List<int>();
        var batches = new List<List<Guid>>();
        int pageCall = 0;
        logger.Setup(candidate => candidate.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        context.Setup(candidate => candidate.CallActivityAsync(
                It.Is<TaskName>(name => name.Name == "Delete"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback(() => calls.Add("delete"))
            .Returns(Task.CompletedTask);
        context.Setup(candidate => candidate.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(name => name.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback<TaskName, object?, TaskOptions?>((_, input, _) =>
            {
                pageRequests.Add(((DurableCurrentStatePageInfo)input!).PageNumber);
                calls.Add("page");
            })
            .ReturnsAsync(() => pageCall++ == 0 ? ids : new List<Guid>());
        context.Setup(candidate => candidate.CallActivityAsync(
                It.Is<TaskName>(name => name.Name == "Process"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback<TaskName, object?, TaskOptions?>((_, input, _) =>
            {
                batches.Add(((List<Guid>)input!).ToList());
                calls.Add("batch");
            })
            .Returns(Task.CompletedTask);
        var initializer = CreateInitializer(Mock.Of<INostify>(), batchSize: 2, concurrentBatchCount: 2);

        await initializer.OrchestrateInitAsync(context.Object, "Delete", "GetIds", "Process", logger.Object);

        Assert.Equal("delete", calls[0]);
        Assert.Equal(new[] { 0, 1 }, pageRequests);
        Assert.Equal(3, batches.Count);
        Assert.Equal(ids.Take(2), batches[0]);
        Assert.Equal(ids.Skip(2).Take(2), batches[1]);
        Assert.Equal(ids.Skip(4), batches[2]);
        VerifyLogCount(logger, 3);
    }

    [Fact]
    public async Task OrchestrateInitAsync_ShortPageCompletesWithoutRequestingAnotherPage()
    {
        var context = new Mock<TaskOrchestrationContext>();
        int pageCalls = 0;
        context.Setup(candidate => candidate.CallActivityAsync(
                It.Is<TaskName>(name => name.Name == "Delete"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);
        context.Setup(candidate => candidate.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(name => name.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback(() => pageCalls++)
            .ReturnsAsync(new List<Guid> { Guid.NewGuid() });
        context.Setup(candidate => candidate.CallActivityAsync(
                It.Is<TaskName>(name => name.Name == "Process"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);
        var initializer = CreateInitializer(Mock.Of<INostify>(), batchSize: 2, concurrentBatchCount: 2);

        await initializer.OrchestrateInitAsync(context.Object, "Delete", "GetIds", "Process");

        Assert.Equal(1, pageCalls);
    }

    [Fact]
    public async Task GetAggregateIds_ReturnsStableDistinctRequestedPage()
    {
        Guid first = CreateDeterministicGuid(1);
        Guid second = CreateDeterministicGuid(2);
        Guid third = CreateDeterministicGuid(3);
        var events = new List<Event>
        {
            CreateEvent(third, "third", DateTime.UnixEpoch.AddMinutes(3)),
            CreateEvent(first, "first", DateTime.UnixEpoch.AddMinutes(1)),
            CreateEvent(second, "second", DateTime.UnixEpoch.AddMinutes(2)),
            CreateEvent(first, "duplicate", DateTime.UnixEpoch.AddMinutes(4))
        };
        Mock<Container> eventStore = CosmosTestHelpers.CreateMockContainer(events);
        var nostify = new Mock<INostify>();
        nostify.Setup(candidate => candidate.GetEventStoreContainerAsync(false)).ReturnsAsync(eventStore.Object);
        var initializer = CreateInitializer(nostify.Object, batchSize: 1, concurrentBatchCount: 2);

        List<Guid> result = await initializer.GetAggregateIds(new DurableCurrentStatePageInfo(1));

        Assert.Equal(new[] { third }, result);
    }

    [Fact]
    public async Task DeleteAllCurrentState_QueriesConfiguredContainerAndDeletesReturnedAggregates()
    {
        var aggregates = new List<TestAggregate>
        {
            new() { id = CreateDeterministicGuid(1), name = "one" },
            new() { id = CreateDeterministicGuid(2), name = "two" }
        };
        Mock<Container> container = CosmosTestHelpers.CreateMockContainer(aggregates);
        var nostify = new Mock<INostify>();
        nostify.Setup(candidate => candidate.GetBulkCurrentStateContainerAsync<TestAggregate>("/organizationId"))
            .ReturnsAsync(container.Object);
        var initializer = new DeleteTrackingInitializer(
            nostify.Object, container.Object, InMemoryQueryExecutor.Default);

        await initializer.DeleteAllCurrentState();

        Assert.Equal(1, initializer.DeleteCallCount);
        Assert.Same(container.Object, initializer.DeletedContainer);
        Assert.Equal(aggregates.Select(item => item.id), initializer.DeletedAggregates!.Select(item => item.id));
    }

    [Fact]
    public async Task DeleteAllCurrentState_WhenCancellationRequested_DoesNotAccessCosmos()
    {
        var nostify = new Mock<INostify>(MockBehavior.Strict);
        var client = CreateClientReturning(OrchestrationRuntimeStatus.Completed);
        var initializer = CreateInitializer(nostify.Object);

        await initializer.DeleteAllCurrentState(client.Object);

        nostify.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessBatch_RehydratesInTimestampOrderAndForwardsRetryOptions()
    {
        Guid firstId = CreateDeterministicGuid(1);
        Guid secondId = CreateDeterministicGuid(2);
        var events = new List<Event>
        {
            CreateEvent(firstId, "latest", DateTime.UnixEpoch.AddMinutes(3)),
            CreateEvent(secondId, "second aggregate", DateTime.UnixEpoch.AddMinutes(2)),
            CreateEvent(firstId, "earliest", DateTime.UnixEpoch.AddMinutes(1)),
            CreateEvent(Guid.NewGuid(), "unrelated", DateTime.UnixEpoch)
        };
        Mock<Container> eventStore = CosmosTestHelpers.CreateMockContainer(events);
        Mock<Container> currentState = CosmosTestHelpers.CreateMockContainer(new List<TestAggregate>());
        var nostify = new Mock<INostify>();
        nostify.Setup(candidate => candidate.GetEventStoreContainerAsync(false)).ReturnsAsync(eventStore.Object);
        nostify.Setup(candidate => candidate.GetBulkCurrentStateContainerAsync<TestAggregate>("/organizationId"))
            .ReturnsAsync(currentState.Object);
        var retryable = new Mock<IRetryableContainer>();
        List<TestAggregate>? persisted = null;
        retryable.Setup(candidate => candidate.DoBulkUpsertAsync(
                It.IsAny<List<TestAggregate>>(), It.IsAny<Func<TestAggregate, Exception, Task>?>()))
            .Callback<List<TestAggregate>, Func<TestAggregate, Exception, Task>?>((items, _) => persisted = items)
            .Returns(Task.CompletedTask);
        var retryOptions = new RetryOptions(7, TimeSpan.FromMilliseconds(5), true);
        RetryOptions? capturedOptions = null;
        Container? capturedContainer = null;
        var initializer = CreateInitializer(
            nostify.Object,
            retryOptions: retryOptions,
            factory: (container, options) =>
            {
                capturedContainer = container;
                capturedOptions = options;
                return retryable.Object;
            });

        await initializer.ProcessBatch(new List<Guid> { firstId, secondId });

        Assert.NotNull(persisted);
        Assert.Equal(2, persisted!.Count);
        Assert.Equal("latest", persisted.Single(item => item.id == firstId).name);
        Assert.Equal("second aggregate", persisted.Single(item => item.id == secondId).name);
        Assert.Same(currentState.Object, capturedContainer);
        Assert.Same(retryOptions, capturedOptions);
    }

    [Fact]
    public async Task ProcessBatch_WhenCancelledBeforeRead_DoesNotAccessCosmos()
    {
        var nostify = new Mock<INostify>(MockBehavior.Strict);
        var client = CreateClientReturning(OrchestrationRuntimeStatus.Terminated);
        var initializer = CreateInitializer(nostify.Object);

        await initializer.ProcessBatch(new List<Guid> { Guid.NewGuid() }, client.Object);

        nostify.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessBatch_WhenCancelledAfterRead_DoesNotPersist()
    {
        Guid id = CreateDeterministicGuid(1);
        Mock<Container> eventStore = CosmosTestHelpers.CreateMockContainer(
            new List<Event> { CreateEvent(id, "state", DateTime.UnixEpoch) });
        var nostify = new Mock<INostify>();
        nostify.Setup(candidate => candidate.GetEventStoreContainerAsync(false)).ReturnsAsync(eventStore.Object);
        var client = new Mock<DurableTaskClient>("test");
        client.SetupSequence(candidate => candidate.GetInstanceAsync(
                "aggregate-init", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadata(OrchestrationRuntimeStatus.Running))
            .ReturnsAsync(CreateMetadata(OrchestrationRuntimeStatus.Completed));
        var initializer = CreateInitializer(nostify.Object);

        await initializer.ProcessBatch(new List<Guid> { id }, client.Object);

        nostify.Verify(candidate => candidate.GetBulkCurrentStateContainerAsync<TestAggregate>(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(OrchestrationRuntimeStatus.Running, false)]
    [InlineData(OrchestrationRuntimeStatus.Pending, false)]
    [InlineData(OrchestrationRuntimeStatus.Suspended, false)]
    [InlineData(OrchestrationRuntimeStatus.Completed, true)]
    [InlineData(OrchestrationRuntimeStatus.Terminated, true)]
    public async Task IsCancellationRequestedAsync_ReflectsInstanceLifecycle(
        OrchestrationRuntimeStatus status,
        bool expected)
    {
        var client = CreateClientReturning(status);
        var initializer = CreateInitializer(Mock.Of<INostify>());

        bool result = await initializer.IsCancellationRequestedAsync(client.Object);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task IsCancellationRequestedAsync_WhenInstanceDoesNotExist_ReturnsTrue()
    {
        var client = new Mock<DurableTaskClient>("test");
        client.Setup(candidate => candidate.GetInstanceAsync(
                "aggregate-init", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null);
        var initializer = CreateInitializer(Mock.Of<INostify>());

        Assert.True(await initializer.IsCancellationRequestedAsync(client.Object));
    }

    private static DurableCurrentStateInitializer<TestAggregate> CreateInitializer(
        INostify nostify,
        int batchSize = 2,
        int concurrentBatchCount = 2,
        RetryOptions? retryOptions = null,
        Func<Container, RetryOptions, IRetryableContainer>? factory = null)
    {
        factory ??= (_, _) => Mock.Of<IRetryableContainer>();
        return new DurableCurrentStateInitializer<TestAggregate>(
            nostify,
            "aggregate-init",
            "/organizationId",
            batchSize,
            concurrentBatchCount,
            durableTaskOptions: null,
            cosmosRetryOptions: retryOptions,
            InMemoryQueryExecutor.Default,
            factory);
    }

    private static Mock<DurableTaskClient> CreateClientReturning(OrchestrationRuntimeStatus status)
    {
        var client = new Mock<DurableTaskClient>("test");
        client.Setup(candidate => candidate.GetInstanceAsync(
                "aggregate-init", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadata(status));
        return client;
    }

    private static OrchestrationMetadata CreateMetadata(OrchestrationRuntimeStatus status)
    {
        var metadata = new OrchestrationMetadata("CurrentStateOrchestrator", "aggregate-init");
        typeof(OrchestrationMetadata)
            .GetProperty(nameof(OrchestrationMetadata.RuntimeStatus))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(metadata, new object[] { status });
        return metadata;
    }

    private static HttpRequestData CreateRequestWithUrl()
    {
        var services = new ServiceCollection();
        services.AddFunctionsWorkerDefaults();
        var context = new Mock<FunctionContext>();
        context.SetupProperty(candidate => candidate.InstanceServices, services.BuildServiceProvider());
        var request = new Mock<HttpRequestData>(context.Object);
        request.Setup(candidate => candidate.Body).Returns(new MemoryStream());
        request.Setup(candidate => candidate.Url).Returns(new Uri("http://localhost/api/current-state"));
        request.Setup(candidate => candidate.Headers).Returns(new HttpHeadersCollection());
        request.Setup(candidate => candidate.CreateResponse()).Returns(new MockHttpResponseData(context.Object));
        return request.Object;
    }

    private static Event CreateEvent(Guid id, string name, DateTime timestamp)
        => new(CurrentStateEventType.Instance, id, new { id, name }) { timestamp = timestamp };

    private static Guid CreateDeterministicGuid(int value)
        => new(value, 0, 0, new byte[8]);

    private static void VerifyLogCount(Mock<ILogger> logger, int count)
    {
        logger.Verify(candidate => candidate.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Exactly(count));
    }

    /// <summary>Event type used to exercise real aggregate event application.</summary>
    public sealed class CurrentStateEventType : EventType
    {
        public static CurrentStateEventType Instance { get; } = new();

        public CurrentStateEventType()
            : base("CurrentStateEvent", false, false)
        {
        }
    }

    private sealed class DeleteTrackingInitializer : DurableCurrentStateInitializer<TestAggregate>
    {
        public DeleteTrackingInitializer(INostify nostify, Container expectedContainer, IQueryExecutor queryExecutor)
            : base(
                nostify,
                "aggregate-init",
                "/organizationId",
                2,
                2,
                null,
                null,
                queryExecutor,
                (_, _) => Mock.Of<IRetryableContainer>())
        {
            ExpectedContainer = expectedContainer;
        }

        private Container ExpectedContainer { get; }
        public int DeleteCallCount { get; private set; }
        public Container? DeletedContainer { get; private set; }
        public List<TestAggregate>? DeletedAggregates { get; private set; }

        internal override Task<int> DeleteCurrentStateAsync(
            Container container,
            List<TestAggregate> aggregates)
        {
            Assert.Same(ExpectedContainer, container);
            DeleteCallCount++;
            DeletedContainer = container;
            DeletedAggregates = aggregates;
            return Task.FromResult(aggregates.Count);
        }
    }
}
