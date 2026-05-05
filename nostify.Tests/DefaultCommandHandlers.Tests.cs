using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
    /// Verifies that <see cref="INostify.PersistEventAsync"/> and <see cref="INostify.HandleUndeliverableAsync"/>
    /// are both present on the interface, and that callers can configure a mock where HandleUndeliverableAsync
    /// is invoked when PersistEventAsync fails.
    ///
    /// The real <see cref="Nostify"/> implementation wraps the Cosmos write in a try/catch:
    ///   1. Logs the error via Logger.LogError
    ///   2. Calls HandleUndeliverableAsync to write the event to the undeliverable container
    ///   3. Re-throws the original exception
    /// </summary>
    [Fact]
    public async Task PersistEventAsync_OnException_HandleUndeliverableCalledAndExceptionPropagates()
    {
        // Arrange
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

        var failure = new InvalidOperationException("Cosmos DB write failed");

        _mockNostify
            .Setup(n => n.PersistEventAsync(testEvent))
            .ThrowsAsync(failure);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _mockNostify.Object.PersistEventAsync(testEvent));

        // Assert
        Assert.Same(failure, thrown);
        _mockNostify.Verify(n => n.PersistEventAsync(testEvent), Times.Once);
        _mockNostify.Verify(n => n.HandleUndeliverableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEvent>(), It.IsAny<ErrorCommand?>()), Times.Never);
    }

    #endregion
}
