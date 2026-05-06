using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Tests for DefaultCommandHandler covering:
/// - HandleBulkUpdateAsync throws NostifyException when the request body deserializes to null
/// - HandleBulkDeleteAsync (HttpRequestData overloads) throws NostifyException when the request body deserializes to null
/// - PersistEventAsync contract: HandleUndeliverableAsync is called and exception is re-thrown on failure
/// </summary>
public class DefaultCommandHandlersTests
{
    private readonly Mock<INostify> _mockNostify;

    public DefaultCommandHandlersTests()
    {
        _mockNostify = new Mock<INostify>();
        _mockNostify
            .Setup(n => n.HandleUndeliverableAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()))
            .Returns(Task.CompletedTask);
        _mockNostify
            .Setup(n => n.BulkPersistEventAsync(
                It.IsAny<List<IEvent>>(), It.IsAny<int?>(), It.IsAny<RetryOptions?>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        _mockNostify
            .Setup(n => n.DefaultRetryOptions)
            .Returns(new RetryOptions());
    }

    #region Helpers

    /// <summary>
    /// Creates an HttpRequestData whose body contains the given raw JSON string.
    /// </summary>
    private static HttpRequestData CreateRequestWithRawBody(string rawJson)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddFunctionsWorkerDefaults();

        var bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(rawJson));
        var context = new Mock<Microsoft.Azure.Functions.Worker.FunctionContext>();
        context.SetupProperty(c => c.InstanceServices, serviceCollection.BuildServiceProvider());

        var request = new Mock<HttpRequestData>(context.Object);
        request.Setup(r => r.Body).Returns(bodyStream);
        request.Setup(r => r.CreateResponse()).Returns(new MockHttpResponseData(context.Object));

        return request.Object;
    }

    #endregion

    #region HandleBulkUpdateAsync – null body

    [Fact]
    public async Task HandleBulkUpdateAsync_RetryOptionsOverload_NullBody_ThrowsNostifyException()
    {
        // "null" is valid JSON for null, so DeserializeObject returns null
        var req = CreateRequestWithRawBody("null");
        var command = new NostifyCommand("BulkUpdate");

        var ex = await Assert.ThrowsAsync<NostifyException>(() =>
            DefaultCommandHandler.HandleBulkUpdateAsync<TestAggregate>(
                _mockNostify.Object, command, req,
                default, default, 100, (RetryOptions?)null));

        Assert.Contains("deserialize", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleBulkUpdateAsync_BoolOverload_NullBody_ThrowsNostifyException()
    {
        var req = CreateRequestWithRawBody("null");
        var command = new NostifyCommand("BulkUpdate");

        await Assert.ThrowsAsync<NostifyException>(() =>
            DefaultCommandHandler.HandleBulkUpdateAsync<TestAggregate>(
                _mockNostify.Object, command, req,
                batchSize: 100, allowRetry: false));
    }

    [Fact]
    public async Task HandleBulkUpdateAsync_ValidBody_DoesNotThrow()
    {
        var id = Guid.NewGuid();
        var body = JsonConvert.SerializeObject(new[] { new { id = id.ToString(), name = "Test" } });
        var req = CreateRequestWithRawBody(body);
        var command = new NostifyCommand("BulkUpdate");

        // Should not throw; returns count of objects
        var count = await DefaultCommandHandler.HandleBulkUpdateAsync<TestAggregate>(
            _mockNostify.Object, command, req,
            default, default, 100, (RetryOptions?)null);

        Assert.Equal(1, count);
    }

    #endregion

    #region HandleBulkDeleteAsync (HttpRequestData) – null body

    [Fact]
    public async Task HandleBulkDeleteAsync_HttpRequestData_RetryOptionsOverload_NullBody_ThrowsNostifyException()
    {
        var req = CreateRequestWithRawBody("null");
        var command = new NostifyCommand("BulkDelete");

        var ex = await Assert.ThrowsAsync<NostifyException>(() =>
            DefaultCommandHandler.HandleBulkDeleteAsync<TestAggregate>(
                _mockNostify.Object, command, req,
                default, default, 100, (RetryOptions?)null));

        Assert.Contains("deserialize", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleBulkDeleteAsync_HttpRequestData_BoolOverload_NullBody_ThrowsNostifyException()
    {
        var req = CreateRequestWithRawBody("null");
        var command = new NostifyCommand("BulkDelete");

        await Assert.ThrowsAsync<NostifyException>(() =>
            DefaultCommandHandler.HandleBulkDeleteAsync<TestAggregate>(
                _mockNostify.Object, command, req,
                allowRetry: false));
    }

    [Fact]
    public async Task HandleBulkDeleteAsync_HttpRequestData_ValidBody_DoesNotThrow()
    {
        var ids = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        var body = JsonConvert.SerializeObject(ids);
        var req = CreateRequestWithRawBody(body);
        var command = new NostifyCommand("BulkDelete");

        var count = await DefaultCommandHandler.HandleBulkDeleteAsync<TestAggregate>(
            _mockNostify.Object, command, req,
            default, default, 100, (RetryOptions?)null);

        Assert.Equal(2, count);
    }

    #endregion

    #region PersistEventAsync – undeliverable contract

    /// <summary>
    /// A test-only subclass of <see cref="Nostify"/> that overrides the two virtual I/O boundary
    /// methods so the real <see cref="Nostify.PersistEventAsync"/> implementation can run under test:
    ///   - <see cref="GetEventStoreContainerAsync"/> returns a caller-supplied fake container or throws
    ///     a caller-supplied exception, depending on which constructor was used.
    ///   - <see cref="HandleUndeliverableAsync"/> records arguments without touching Cosmos.
    /// </summary>
    private sealed class TestableNostify : Nostify
    {
        private readonly Container? _fakeEventContainer;
        private readonly Exception? _containerException;

        public List<(string FunctionName, string ErrorMessage, IEvent Event, ErrorCommand? Command)> UndeliverableCalls { get; } = new();

        /// <summary>Simulates a successful container resolution that returns the supplied fake.</summary>
        public TestableNostify(Container fakeEventContainer)
            : base(
                new NostifyCosmosClient(),
                "/tenantId",
                Guid.Empty,
                "localhost:9092",
                new Mock<IProducer<string, string>>().Object,
                new Mock<System.Net.Http.IHttpClientFactory>().Object)
        {
            _fakeEventContainer = fakeEventContainer;
        }

        /// <summary>Simulates a container-resolution failure (GetEventStoreContainerAsync throws).</summary>
        public TestableNostify(Exception containerException)
            : base(
                new NostifyCosmosClient(),
                "/tenantId",
                Guid.Empty,
                "localhost:9092",
                new Mock<IProducer<string, string>>().Object,
                new Mock<System.Net.Http.IHttpClientFactory>().Object)
        {
            _containerException = containerException;
        }

        public override Task<Container> GetEventStoreContainerAsync(bool allowBulk = false)
        {
            if (_containerException != null)
                return Task.FromException<Container>(_containerException);
            return Task.FromResult(_fakeEventContainer!);
        }

        public override Task HandleUndeliverableAsync(
            string functionName, string errorMessage, IEvent eventToHandle, ErrorCommand? errorCommand = null)
        {
            UndeliverableCalls.Add((functionName, errorMessage, eventToHandle, errorCommand));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Exercises the real <see cref="Nostify.PersistEventAsync"/> implementation: when the
    /// underlying event-store write throws, the production catch block must call
    /// <see cref="Nostify.HandleUndeliverableAsync"/> and then re-throw the original exception.
    ///
    /// Uses <see cref="TestableNostify"/> (a concrete subclass) so the production code runs end-to-end
    /// without touching real Cosmos — only the two virtual I/O boundaries are swapped out.
    /// </summary>
    [Fact]
    public async Task PersistEventAsync_OnException_HandleUndeliverableCalledAndExceptionPropagates()
    {
        // Arrange: a mock Container whose CreateItemAsync always throws
        var failure = new InvalidOperationException("Cosmos DB write failed");
        var mockContainer = new Mock<Container>();
        mockContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<IEvent>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var nostify = new TestableNostify(mockContainer.Object);

        var testEvent = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = Guid.NewGuid(),
            command = new NostifyCommand("TestCommand"),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = Guid.NewGuid(),
            payload = new { name = "Test" }
        };

        // Act: the REAL PersistEventAsync runs; it must re-throw the original exception
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nostify.PersistEventAsync(testEvent));

        // Assert: the original exception identity is preserved
        Assert.Same(failure, thrown);

        // Assert: HandleUndeliverableAsync was called exactly once with the right context
        Assert.Single(nostify.UndeliverableCalls);
        var call = nostify.UndeliverableCalls[0];
        Assert.Equal(nameof(Nostify.PersistEventAsync), call.FunctionName);
        Assert.Equal(failure.Message, call.ErrorMessage);
        Assert.Same(testEvent, call.Event);
        Assert.Null(call.Command);
    }

    /// <summary>
    /// Exercises the real <see cref="Nostify.PersistEventAsync"/> implementation: when
    /// <see cref="Nostify.GetEventStoreContainerAsync"/> throws (container resolution failure),
    /// the production catch block must call <see cref="Nostify.HandleUndeliverableAsync"/> and
    /// re-throw the original exception.  This verifies that the try/catch covers the full method
    /// body, including the container-resolution step.
    /// </summary>
    [Fact]
    public async Task PersistEventAsync_ContainerResolutionFails_HandleUndeliverableCalledAndExceptionPropagates()
    {
        // Arrange: TestableNostify whose GetEventStoreContainerAsync throws
        var failure = new InvalidOperationException("Cosmos container not found");
        var nostify = new TestableNostify(failure);

        var testEvent = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = Guid.NewGuid(),
            command = new NostifyCommand("TestCommand"),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = Guid.NewGuid(),
            payload = new { name = "Test" }
        };

        // Act: the REAL PersistEventAsync runs; it must re-throw the original exception
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nostify.PersistEventAsync(testEvent));

        // Assert: the original exception identity is preserved
        Assert.Same(failure, thrown);

        // Assert: HandleUndeliverableAsync was called exactly once with the right context
        Assert.Single(nostify.UndeliverableCalls);
        var call = nostify.UndeliverableCalls[0];
        Assert.Equal(nameof(Nostify.PersistEventAsync), call.FunctionName);
        Assert.Equal(failure.Message, call.ErrorMessage);
        Assert.Same(testEvent, call.Event);
        Assert.Null(call.Command);
    }

    #endregion
}
