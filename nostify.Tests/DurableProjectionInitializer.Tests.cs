using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using nostify;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Unit tests for <see cref="DurableProjectionInitializer{TProjection,TAggregate}"/>.
///
/// IMPORTANT MOQ BEHAVIOR NOTE:
/// The abstract class <see cref="TaskOrchestrationContext"/> has both abstract and non-abstract
/// virtual overloads of CallActivityAsync. All non-abstract overloads delegate to the abstract
/// 3-param overload: CallActivityAsync(TaskName, object?, TaskOptions?). Moq calls the base
/// implementation for non-abstract virtual methods when there is no matching setup, which means
/// all activity calls reach the abstract 3-param overload. Therefore, all setups in these tests
/// target the 3-param abstract overload and use It.Is&lt;TaskName&gt;(n => n.Name == "...") to
/// distinguish between different activities.
///
/// Similarly, DurableTaskClient.GetInstanceAsync(string, CancellationToken) delegates to the
/// abstract GetInstanceAsync(string, bool, CancellationToken) overload, so the 3-param signature
/// is used in all setups.
/// </summary>
public class DurableProjectionInitializerTests
{
    private readonly Mock<INostify> _nostifyMock;
    private readonly HttpClient _httpClient;

    public DurableProjectionInitializerTests()
    {
        _nostifyMock = new Mock<INostify>();
        _httpClient = new HttpClient();
    }

    #region Helpers

    private DurableProjectionInitializer<TestProjection, TestAggregate> CreateInitializer(
        string? instanceId = "test-instance",
        int batchSize = 10,
        int concurrentBatchCount = 2)
    {
        return new DurableProjectionInitializer<TestProjection, TestAggregate>(
            _httpClient, _nostifyMock.Object, instanceId!, batchSize, concurrentBatchCount);
    }

    /// <summary>
    /// Creates an OrchestrationMetadata with the given status using reflection,
    /// because the RuntimeStatus property only has a private setter.
    /// </summary>
    private static OrchestrationMetadata CreateMetadataWithStatus(OrchestrationRuntimeStatus status)
    {
        var meta = new OrchestrationMetadata("TestOrchestrator", "test-instance");
        var prop = typeof(OrchestrationMetadata).GetProperty("RuntimeStatus");
        prop!.GetSetMethod(nonPublic: true)!.Invoke(meta, new object[] { status });
        return meta;
    }

    /// <summary>
    /// Creates a Mock HttpRequestData with Url and Headers properly set up so that
    /// DurableTaskClientExtensions.CreateCheckStatusResponseAsync can build management URLs
    /// without throwing NullReferenceException.
    /// </summary>
    private static HttpRequestData CreateMockRequestWithUrl()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddFunctionsWorkerDefaults();

        var context = new Mock<FunctionContext>();
        context.SetupProperty(c => c.InstanceServices, serviceCollection.BuildServiceProvider());

        var request = new Mock<HttpRequestData>(context.Object);
        request.Setup(r => r.Body).Returns(new MemoryStream());
        request.Setup(r => r.Url).Returns(new Uri("http://localhost:7071/api/test"));
        request.Setup(r => r.Headers).Returns(new HttpHeadersCollection());
        request.Setup(r => r.CreateResponse()).Returns(new MockHttpResponseData(context.Object));

        return request.Object;
    }

    /// <summary>
    /// Sets up all activity overloads needed for OrchestrateInitAsync tests.
    /// All CallActivityAsync overloads delegate to the abstract 3-param version,
    /// so setups use It.Is&lt;TaskName&gt;(n => n.Name == ...) for precise matching.
    /// </summary>
    private static void SetupOrchestratorActivities(
        Mock<TaskOrchestrationContext> contextMock,
        string deleteActivityName,
        string getTenantIdsActivityName,
        IEnumerable<Guid> tenantIds,
        string getIdsActivityName,
        List<Guid> idsToReturn,
        string processBatchActivityName)
    {
        // Delete: untyped void activity (all variants delegate to abstract 3-param)
        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == deleteActivityName),
                It.IsAny<object?>(),
                It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        // GetTenantIds: typed activity returning a tenant list
        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == getTenantIdsActivityName),
                It.IsAny<object?>(),
                It.IsAny<TaskOptions?>()))
            .ReturnsAsync(tenantIds.ToList());

        // GetIds: typed activity returning aggregate IDs for a given page
        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == getIdsActivityName),
                It.IsAny<object?>(),
                It.IsAny<TaskOptions?>()))
            .ReturnsAsync(idsToReturn);

        // ProcessBatch: untyped void activity with batch input
        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == processBatchActivityName),
                It.IsAny<object?>(),
                It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);
    }

    #endregion

    #region DurableInitPageInfo Tests

    [Fact]
    public void DurableInitPageInfo_Constructor_SetsTenantId()
    {
        var tenantId = Guid.NewGuid();
        var pageInfo = new DurableInitPageInfo(tenantId, 3);

        Assert.Equal(tenantId, pageInfo.TenantId);
    }

    [Fact]
    public void DurableInitPageInfo_Constructor_SetsPageNumber()
    {
        var pageInfo = new DurableInitPageInfo(Guid.NewGuid(), 5);

        Assert.Equal(5, pageInfo.PageNumber);
    }

    [Fact]
    public void DurableInitPageInfo_PageNumberZero_IsValid()
    {
        var pageInfo = new DurableInitPageInfo(Guid.NewGuid(), 0);

        Assert.Equal(0, pageInfo.PageNumber);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public async Task Constructor_WithNullInstanceId_UsesGenericParameterNameNotConcreteTypeName()
    {
        // nameof(TProjection) inside a generic class yields "TProjection" (the type parameter
        // identifier), NOT the concrete type name such as "TestProjection".
        // Verifiable by observing the instance ID passed to GetInstanceAsync.
        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock
            .Setup(c => c.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running));

        // null is passed explicitly; constructor uses: _instanceId = instanceId ?? "TProjection_Init"
        var initializer = new DurableProjectionInitializer<TestProjection, TestAggregate>(
            _httpClient, _nostifyMock.Object, null!);

        var req = MockHttpRequestData.Create();
        // Running instance → returns 409 without calling CreateCheckStatusResponseAsync
        await initializer.StartOrchestration(req, clientMock.Object, "Orchestrator");

        // The abstract 3-param overload is what's actually called
        clientMock.Verify(
            c => c.GetInstanceAsync("TProjection_Init", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Constructor_WithCustomInstanceId_UsesProvidedId()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock
            .Setup(c => c.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running));

        var initializer = new DurableProjectionInitializer<TestProjection, TestAggregate>(
            _httpClient, _nostifyMock.Object, "my-custom-id");

        var req = MockHttpRequestData.Create();
        await initializer.StartOrchestration(req, clientMock.Object, "Orchestrator");

        clientMock.Verify(
            c => c.GetInstanceAsync("my-custom-id", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithExplicitInstanceId_CreatesSuccessfully()
    {
        var initializer = new DurableProjectionInitializer<TestProjection, TestAggregate>(
            _httpClient, _nostifyMock.Object, "test", batchSize: 5, concurrentBatchCount: 3);
        Assert.NotNull(initializer);
    }

    #endregion

    #region StartOrchestration Tests

    [Fact]
    public async Task StartOrchestration_WhenExistingInstanceIsRunning_Returns409Conflict()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        // GetInstanceAsync(string, CancellationToken) delegates to the abstract
        // GetInstanceAsync(string, bool, CancellationToken), so set up the 3-param version.
        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running));

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        var response = await initializer.StartOrchestration(req, clientMock.Object, "OrchestratorName");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StartOrchestration_WhenExistingInstanceIsRunning_DoesNotScheduleNewOrchestration()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running));

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        await initializer.StartOrchestration(req, clientMock.Object, "OrchestratorName");

        clientMock.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<StartOrchestrationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StartOrchestration_WhenExistingInstanceIsRunning_UsesCorrectInstanceId()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock
            .Setup(c => c.GetInstanceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running));

        var initializer = CreateInitializer("my-id");
        var req = MockHttpRequestData.Create();

        await initializer.StartOrchestration(req, clientMock.Object, "OrchestratorName");

        clientMock.Verify(
            c => c.GetInstanceAsync("my-id", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartOrchestration_WhenNoExistingInstance_SchedulesOrchestration()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null);

        clientMock
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<StartOrchestrationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-instance");

        var initializer = CreateInitializer("test-instance");
        // A request with Url + Headers is required so CreateCheckStatusResponseAsync can build management URLs
        var req = CreateMockRequestWithUrl();

        await initializer.StartOrchestration(req, clientMock.Object, "OrchestratorName");

        clientMock.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<StartOrchestrationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartOrchestration_WhenExistingInstanceIsCompleted_SchedulesOrchestration()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadataWithStatus(OrchestrationRuntimeStatus.Completed));

        clientMock
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<StartOrchestrationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-instance");

        var initializer = CreateInitializer("test-instance");
        var req = CreateMockRequestWithUrl();

        await initializer.StartOrchestration(req, clientMock.Object, "OrchestratorName");

        clientMock.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<StartOrchestrationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartOrchestration_WhenNoExistingInstance_SchedulesWithCorrectInstanceId()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        StartOrchestrationOptions? capturedOptions = null;

        clientMock
            .Setup(c => c.GetInstanceAsync("my-instance-id", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null);

        clientMock
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<TaskName>(),
                It.IsAny<StartOrchestrationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<TaskName, StartOrchestrationOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync("my-instance-id");

        var initializer = CreateInitializer("my-instance-id");
        var req = CreateMockRequestWithUrl();

        await initializer.StartOrchestration(req, clientMock.Object, "OrchestratorName");

        Assert.NotNull(capturedOptions);
        Assert.Equal("my-instance-id", capturedOptions!.InstanceId);
    }

    #endregion

    #region CancelOrchestration Tests

    [Fact]
    public async Task CancelOrchestration_WhenNoExistingInstance_ReturnsOk()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null);

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        var response = await initializer.CancelOrchestration(req, clientMock.Object);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrchestration_WhenNoExistingInstance_DoesNotTerminateOrPurge()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null);

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        await initializer.CancelOrchestration(req, clientMock.Object);

        clientMock.Verify(
            c => c.TerminateInstanceAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
        clientMock.Verify(
            c => c.PurgeInstanceAsync(It.IsAny<string>(), It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelOrchestration_WhenRunningInstance_TerminatesBeforePurging()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        var runningMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running);
        var terminatedMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Terminated);
        var callOrder = new List<string>();

        clientMock
            .SetupSequence(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runningMetadata)
            .ReturnsAsync(terminatedMetadata);

        clientMock
            .Setup(c => c.TerminateInstanceAsync("test-instance", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Terminate"))
            .Returns(Task.CompletedTask);

        clientMock
            .Setup(c => c.WaitForInstanceCompletionAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Wait"))
            .ReturnsAsync(terminatedMetadata);

        clientMock
            .Setup(c => c.PurgeInstanceAsync("test-instance", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Purge"))
            .ReturnsAsync(new PurgeResult(1));

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        await initializer.CancelOrchestration(req, clientMock.Object);

        Assert.Equal(new[] { "Terminate", "Wait", "Purge" }, callOrder);
    }

    [Fact]
    public async Task CancelOrchestration_WhenRunningInstance_ReturnsOk()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        var runningMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running);
        var terminatedMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Terminated);

        clientMock
            .SetupSequence(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runningMetadata)
            .ReturnsAsync(terminatedMetadata);

        clientMock
            .Setup(c => c.TerminateInstanceAsync("test-instance", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        clientMock
            .Setup(c => c.WaitForInstanceCompletionAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(terminatedMetadata);

        clientMock
            .Setup(c => c.PurgeInstanceAsync("test-instance", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurgeResult(1));

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        var response = await initializer.CancelOrchestration(req, clientMock.Object);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrchestration_WhenRunningInstance_CallsTerminateWithInstanceIdInReason()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        var runningMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running);
        var terminatedMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Terminated);
        string? capturedReason = null;

        clientMock
            .SetupSequence(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runningMetadata)
            .ReturnsAsync(terminatedMetadata);

        clientMock
            .Setup(c => c.TerminateInstanceAsync("test-instance", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((_, reason, _) => capturedReason = reason?.ToString())
            .Returns(Task.CompletedTask);

        clientMock
            .Setup(c => c.WaitForInstanceCompletionAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(terminatedMetadata);

        clientMock
            .Setup(c => c.PurgeInstanceAsync("test-instance", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurgeResult(1));

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        await initializer.CancelOrchestration(req, clientMock.Object);

        Assert.Contains("test-instance", capturedReason);
    }

    [Fact]
    public async Task CancelOrchestration_WhenNonRunningInstance_PurgesWithoutTerminating()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        var completedMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Completed);

        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedMetadata);

        clientMock
            .Setup(c => c.PurgeInstanceAsync("test-instance", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurgeResult(1));

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        await initializer.CancelOrchestration(req, clientMock.Object);

        clientMock.Verify(
            c => c.TerminateInstanceAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
        clientMock.Verify(
            c => c.PurgeInstanceAsync("test-instance", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelOrchestration_WhenNonRunningInstance_ReturnsOk()
    {
        var clientMock = new Mock<DurableTaskClient>("test");
        var completedMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Completed);

        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedMetadata);

        clientMock
            .Setup(c => c.PurgeInstanceAsync("test-instance", It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurgeResult(1));

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        var response = await initializer.CancelOrchestration(req, clientMock.Object);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrchestration_WhenInstanceGoneAfterTerminate_DoesNotPurge()
    {
        // The second GetInstanceAsync returns null (instance purged externally).
        // PurgeInstanceAsync must not be called in this case.
        var clientMock = new Mock<DurableTaskClient>("test");
        var runningMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running);

        clientMock
            .SetupSequence(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runningMetadata)
            .ReturnsAsync((OrchestrationMetadata?)null);

        clientMock
            .Setup(c => c.TerminateInstanceAsync("test-instance", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        clientMock
            .Setup(c => c.WaitForInstanceCompletionAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null!);

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        await initializer.CancelOrchestration(req, clientMock.Object);

        clientMock.Verify(
            c => c.PurgeInstanceAsync(It.IsAny<string>(), It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelOrchestration_WhenInstanceStillRunningAfterWait_DoesNotPurge()
    {
        // Edge case: if IsRunning is still true after WaitForInstanceCompletionAsync,
        // PurgeInstanceAsync must not be called.
        var clientMock = new Mock<DurableTaskClient>("test");
        var runningMetadata = CreateMetadataWithStatus(OrchestrationRuntimeStatus.Running);

        clientMock
            .Setup(c => c.GetInstanceAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runningMetadata);

        clientMock
            .Setup(c => c.TerminateInstanceAsync("test-instance", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        clientMock
            .Setup(c => c.WaitForInstanceCompletionAsync("test-instance", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runningMetadata);

        var initializer = CreateInitializer("test-instance");
        var req = MockHttpRequestData.Create();

        await initializer.CancelOrchestration(req, clientMock.Object);

        clientMock.Verify(
            c => c.PurgeInstanceAsync(It.IsAny<string>(), It.IsAny<PurgeInstanceOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region OrchestrateInitAsync Tests

    [Fact]
    public async Task OrchestrateInitAsync_AlwaysCallsDeleteActivityFirst()
    {
        var contextMock = new Mock<TaskOrchestrationContext>();
        var callOrder = new List<string>();

        // Track the order of calls using callbacks on name-matched setups
        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback(() => callOrder.Add("delete"))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback(() => callOrder.Add("getTenants"))
            .ReturnsAsync(new List<Guid>());

        // GetIds is not needed since tenant list is empty; ProcessBatch also never called.

        var initializer = CreateInitializer();
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        Assert.NotEmpty(callOrder);
        Assert.Equal("delete", callOrder[0]);
        Assert.Contains("getTenants", callOrder);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithNoTenants_DoesNotCallGetIdsActivity()
    {
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid>());

        var initializer = CreateInitializer();
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        contextMock.Verify(c => c.CallActivityAsync<List<Guid>>(
            It.Is<TaskName>(n => n.Name == "GetIds"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithNoTenants_DoesNotCallProcessBatch()
    {
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid>());

        var initializer = CreateInitializer();
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        contextMock.Verify(c => c.CallActivityAsync(
            It.Is<TaskName>(n => n.Name == "ProcessBatch"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithTenantAndEmptyFirstPage_BreaksWithoutCallingProcessBatch()
    {
        var tenantId = Guid.NewGuid();
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid> { tenantId });

        // Empty first page → loop breaks immediately
        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid>());

        var initializer = CreateInitializer();
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        contextMock.Verify(c => c.CallActivityAsync(
            It.Is<TaskName>(n => n.Name == "ProcessBatch"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithPartialPage_OnlyFetchesOnePage()
    {
        // batchSize=5, concurrentBatchCount=2 → pageSize=10
        // 7 ids (< pageSize) → partial page → loop breaks after one fetch
        var tenantId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToList();
        var contextMock = new Mock<TaskOrchestrationContext>();

        SetupOrchestratorActivities(contextMock,
            "DeleteActivity", "GetTenantIds", new[] { tenantId },
            "GetIds", ids, "ProcessBatch");

        var initializer = CreateInitializer(batchSize: 5, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        // GetIds called exactly once (partial page → no further page fetch)
        contextMock.Verify(c => c.CallActivityAsync<List<Guid>>(
            It.Is<TaskName>(n => n.Name == "GetIds"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithPartialPage_CallsProcessBatchForEachChunk()
    {
        // batchSize=5, concurrentBatchCount=2 → pageSize=10
        // 7 ids chunked by 5 → 2 process-batch calls (chunks of 5 and 2)
        var tenantId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToList();
        var contextMock = new Mock<TaskOrchestrationContext>();

        SetupOrchestratorActivities(contextMock,
            "DeleteActivity", "GetTenantIds", new[] { tenantId },
            "GetIds", ids, "ProcessBatch");

        var initializer = CreateInitializer(batchSize: 5, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        // 7 ids ÷ batchSize 5 = 2 batches
        contextMock.Verify(c => c.CallActivityAsync(
            It.Is<TaskName>(n => n.Name == "ProcessBatch"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithFullPage_FetchesNextPage()
    {
        // batchSize=5, concurrentBatchCount=2 → pageSize=10
        // First page: 10 ids (== pageSize) → continue; Second page: 3 ids (partial) → break
        var tenantId = Guid.NewGuid();
        var fullPage = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var partialPage = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid> { tenantId });

        contextMock.SetupSequence(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(fullPage)
            .ReturnsAsync(partialPage);

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "ProcessBatch"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        var initializer = CreateInitializer(batchSize: 5, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        // GetIds called twice: once for full page, once for partial
        contextMock.Verify(c => c.CallActivityAsync<List<Guid>>(
            It.Is<TaskName>(n => n.Name == "GetIds"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithFullPageFollowedByEmptyPage_StopsAfterEmpty()
    {
        // First page: 10 ids (full) → continue; Second page: empty → break
        var tenantId = Guid.NewGuid();
        var fullPage = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid> { tenantId });

        contextMock.SetupSequence(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(fullPage)
            .ReturnsAsync(new List<Guid>());

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "ProcessBatch"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        var initializer = CreateInitializer(batchSize: 5, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        // GetIds called twice: once for full page (continues), once for empty (breaks)
        contextMock.Verify(c => c.CallActivityAsync<List<Guid>>(
            It.Is<TaskName>(n => n.Name == "GetIds"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task OrchestrateInitAsync_BatchSizeChunks_SingleChunk()
    {
        // batchSize=10, concurrentBatchCount=2 → pageSize=20
        // 5 ids → 1 chunk → ProcessBatch called once
        var tenantId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var contextMock = new Mock<TaskOrchestrationContext>();

        SetupOrchestratorActivities(contextMock,
            "DeleteActivity", "GetTenantIds", new[] { tenantId },
            "GetIds", ids, "ProcessBatch");

        var initializer = CreateInitializer(batchSize: 10, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        contextMock.Verify(c => c.CallActivityAsync(
            It.Is<TaskName>(n => n.Name == "ProcessBatch"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task OrchestrateInitAsync_BatchSizeChunks_MultipleChunks()
    {
        // batchSize=3, concurrentBatchCount=4 → pageSize=12
        // 7 ids (< 12, partial page) → ceil(7/3)=3 batches: [3, 3, 1]
        var tenantId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToList();
        var capturedBatchSizes = new List<int>();
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid> { tenantId });

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(ids);

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "ProcessBatch"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback<TaskName, object?, TaskOptions?>((_, input, _) =>
            {
                if (input is List<Guid> batch)
                    capturedBatchSizes.Add(batch.Count);
            })
            .Returns(Task.CompletedTask);

        var initializer = CreateInitializer(batchSize: 3, concurrentBatchCount: 4);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        Assert.Equal(3, capturedBatchSizes.Count);
        Assert.Equal(3, capturedBatchSizes[0]);
        Assert.Equal(3, capturedBatchSizes[1]);
        Assert.Equal(1, capturedBatchSizes[2]);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithMultipleTenants_ProcessesEachTenant()
    {
        // Two tenants, each with a partial page of IDs
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();
        var ids1 = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var ids2 = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        var contextMock = new Mock<TaskOrchestrationContext>();
        int getIdsCallCount = 0;

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid> { tenantId1, tenantId2 });

        // Return different page data per tenant (both partial → each breaks after one page)
        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns<TaskName, object?, TaskOptions?>((_, _, _) =>
            {
                getIdsCallCount++;
                return Task.FromResult(getIdsCallCount == 1 ? ids1 : ids2);
            });

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "ProcessBatch"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        var initializer = CreateInitializer(batchSize: 10, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        // GetIds called once per tenant (each page is partial)
        contextMock.Verify(c => c.CallActivityAsync<List<Guid>>(
            It.Is<TaskName>(n => n.Name == "GetIds"),
            It.IsAny<object?>(), It.IsAny<TaskOptions?>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task OrchestrateInitAsync_PassesDurableInitPageInfoToGetIds()
    {
        // Verifies that GetIds receives a DurableInitPageInfo with the correct TenantId and page 0.
        var tenantId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        DurableInitPageInfo? capturedPageInfo = null;
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid> { tenantId });

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Callback<TaskName, object?, TaskOptions?>((_, input, _) =>
            {
                if (input is DurableInitPageInfo p)
                    capturedPageInfo = p;
            })
            .ReturnsAsync(ids);

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "ProcessBatch"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        var initializer = CreateInitializer(batchSize: 10, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        Assert.NotNull(capturedPageInfo);
        Assert.Equal(tenantId, capturedPageInfo!.Value.TenantId);
        Assert.Equal(0, capturedPageInfo!.Value.PageNumber);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithMultiplePages_IncrementsPageNumber()
    {
        // batchSize=5, concurrentBatchCount=2 → pageSize=10
        // First page full (10 ids) → page number increments; second page partial (3) → breaks
        var tenantId = Guid.NewGuid();
        var fullPage = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var partialPage = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var capturedPageInfos = new List<DurableInitPageInfo>();
        int callCount = 0;
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid> { tenantId });

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns<TaskName, object?, TaskOptions?>((_, input, _) =>
            {
                if (input is DurableInitPageInfo p)
                    capturedPageInfos.Add(p);
                callCount++;
                return Task.FromResult(callCount == 1 ? fullPage : partialPage);
            });

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "ProcessBatch"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        var initializer = CreateInitializer(batchSize: 5, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        Assert.Equal(2, capturedPageInfos.Count);
        Assert.Equal(0, capturedPageInfos[0].PageNumber);
        Assert.Equal(1, capturedPageInfos[1].PageNumber);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithMultiplePages_PassesSameTenantIdForEachPage()
    {
        // Both page requests for the same tenant should carry the same TenantId.
        var tenantId = Guid.NewGuid();
        var fullPage = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var partialPage = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var capturedTenantIds = new List<Guid>();
        int callCount = 0;
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "DeleteActivity"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenantIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid> { tenantId });

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetIds"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns<TaskName, object?, TaskOptions?>((_, input, _) =>
            {
                if (input is DurableInitPageInfo p)
                    capturedTenantIds.Add(p.TenantId);
                callCount++;
                return Task.FromResult(callCount == 1 ? fullPage : partialPage);
            });

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "ProcessBatch"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        var initializer = CreateInitializer(batchSize: 5, concurrentBatchCount: 2);
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch");

        Assert.Equal(2, capturedTenantIds.Count);
        Assert.All(capturedTenantIds, id => Assert.Equal(tenantId, id));
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithNullLogger_DoesNotThrow()
    {
        var contextMock = new Mock<TaskOrchestrationContext>();

        contextMock.Setup(c => c.CallActivityAsync(
                It.Is<TaskName>(n => n.Name == "Delete"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .Returns(Task.CompletedTask);

        contextMock.Setup(c => c.CallActivityAsync<List<Guid>>(
                It.Is<TaskName>(n => n.Name == "GetTenants"),
                It.IsAny<object?>(), It.IsAny<TaskOptions?>()))
            .ReturnsAsync(new List<Guid>());

        var initializer = CreateInitializer();

        var ex = await Record.ExceptionAsync(() =>
            initializer.OrchestrateInitAsync(contextMock.Object,
                "Delete", "GetTenants", "GetIds", "ProcessBatch", null));

        Assert.Null(ex);
    }

    [Fact]
    public async Task OrchestrateInitAsync_WithLogger_CallsLogMultipleTimes()
    {
        // Logger should be called at least 3 times: on delete, on tenant count, and on completion.
        var tenantId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var contextMock = new Mock<TaskOrchestrationContext>();

        SetupOrchestratorActivities(contextMock,
            "DeleteActivity", "GetTenantIds", new[] { tenantId },
            "GetIds", ids, "ProcessBatch");

        var loggerMock = new Mock<ILogger>();

        var initializer = CreateInitializer("test-instance");
        await initializer.OrchestrateInitAsync(contextMock.Object,
            "DeleteActivity", "GetTenantIds", "GetIds", "ProcessBatch", loggerMock.Object);

        loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(3));
    }

    #endregion

    #region Cosmos-Dependent Methods

    // DeleteAllProjections, GetDistinctTenantIds, GetIdsForTenant, and ProcessBatch
    // all use container.GetItemLinqQueryable<T>().ReadAllAsync(), which internally calls
    // ToFeedIterator<T>() — a Cosmos-specific extension method that cannot be mocked
    // without a live Cosmos DB connection. This is the same limitation documented elsewhere
    // in the codebase (see QueryExtensions.Tests.cs).
    //
    // Integration tests with the Cosmos DB Emulator are required to verify:
    //   - DeleteAllProjections:  all projection documents are removed
    //   - GetDistinctTenantIds: distinct tenant IDs are returned from the aggregate container
    //   - GetIdsForTenant:      correct page-based pagination (Skip/Take) per tenant partition
    //   - ProcessBatch:         events are fetched, applied to projections, and InitAsync is called

    #endregion
}
