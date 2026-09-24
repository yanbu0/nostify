using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;

namespace nostify.Tests;

/// <summary>
/// Verifies validation middleware behavior at the Azure Functions worker boundary.
/// </summary>
public class NostifyValidationExceptionMiddlewareTests
{
    [Fact]
    public void Constructor_WithNullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NostifyValidationExceptionMiddleware(null!));
    }

    [Fact]
    public async Task Invoke_WhenNextSucceeds_InvokesNextWithoutChangingResponse()
    {
        var logger = CreateEnabledLogger();
        var response = CreateResponse();
        FunctionContext context = CreateContext(response);
        bool invoked = false;
        var middleware = CreateMiddleware(logger, response);

        await middleware.Invoke(context, _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        Assert.True(invoked);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, response.Body.Length);
        VerifyLogCount(logger, LogLevel.Warning, 0);
    }

    [Fact]
    public async Task Invoke_WithValidationException_WritesStructuredBadRequestAndLogsWarning()
    {
        var logger = CreateEnabledLogger();
        var response = CreateResponse();
        FunctionContext context = CreateContext(response);
        var middleware = CreateMiddleware(logger, response);
        var exception = new NostifyValidationException(
        [
            new ValidationResult("Name is required", ["Name"]),
            new ValidationResult("Object is invalid")
        ]);

        await middleware.Invoke(context, _ => Task.FromException(exception));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            response.Headers,
            header => header.Key == "Content-Type" && header.Value.Contains("application/json"));

        using JsonDocument document = await ReadBodyAsync(response);
        JsonElement root = document.RootElement;
        Assert.Equal("Validation failed", root.GetProperty("message").GetString());
        Assert.Equal(
            "Name is required",
            root.GetProperty("errors").GetProperty("Name")[0].GetString());
        Assert.Equal(2, root.GetProperty("details").GetArrayLength());
        Assert.Equal(
            "Object is invalid",
            root.GetProperty("details")[1].GetProperty("errorMessage").GetString());
        VerifyLogCount(logger, LogLevel.Warning, 1, eventId: 1);
    }

    [Fact]
    public async Task Invoke_WithWrappedValidationException_UsesInnerValidationDetails()
    {
        var logger = CreateEnabledLogger();
        var response = CreateResponse();
        FunctionContext context = CreateContext(response);
        var middleware = CreateMiddleware(logger, response);
        var validationException = new NostifyValidationException(
            [new ValidationResult("Code is invalid", ["Code"])]);
        var wrapper = new InvalidOperationException("Invocation failed", validationException);

        await middleware.Invoke(context, _ => Task.FromException(wrapper));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument document = await ReadBodyAsync(response);
        Assert.Equal(
            "Code is invalid",
            document.RootElement.GetProperty("errors").GetProperty("Code")[0].GetString());
        VerifyLogCount(logger, LogLevel.Warning, 1, eventId: 1);
    }

    [Fact]
    public async Task Invoke_ForNonHttpFunction_LogsValidationAtWarningAndError()
    {
        var logger = CreateEnabledLogger();
        FunctionContext context = CreateContext(response: null);
        var middleware = CreateMiddleware(logger, response: null);
        var exception = new NostifyValidationException("Invalid message");

        await middleware.Invoke(context, _ => Task.FromException(exception));

        VerifyLogCount(logger, LogLevel.Warning, 1, eventId: 1);
        VerifyLogCount(logger, LogLevel.Error, 1, eventId: 2);
    }

    [Fact]
    public async Task Invoke_WhenResponseBodyCannotBeWritten_LogsFailureWithoutMaskingValidation()
    {
        var logger = CreateEnabledLogger();
        var response = CreateResponse();
        response.Body.Dispose();
        FunctionContext context = CreateContext(response);
        var middleware = CreateMiddleware(logger, response);
        var exception = new NostifyValidationException("Invalid request");

        // Response-writing failures are logged and contained so the original validation failure
        // does not become an unrelated host exception.
        await middleware.Invoke(context, _ => Task.FromException(exception));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        VerifyLogCount(logger, LogLevel.Warning, 1, eventId: 1);
        VerifyLogCount(logger, LogLevel.Error, 1, eventId: 3);
    }

    [Fact]
    public async Task Invoke_WithUnrelatedException_PropagatesException()
    {
        var logger = CreateEnabledLogger();
        FunctionContext context = CreateContext(CreateResponse());
        var middleware = CreateMiddleware(logger, CreateResponse());
        var exception = new InvalidOperationException("Unexpected failure");

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.Invoke(context, _ => Task.FromException(exception)));

        Assert.Same(exception, actual);
        VerifyLogCount(logger, LogLevel.Warning, 0);
    }

    private static MockHttpResponseData CreateResponse()
    {
        var context = new Mock<FunctionContext>();
        return new MockHttpResponseData(context.Object);
    }

    private static FunctionContext CreateContext(HttpResponseData? response)
    {
        // The injected resolver owns response selection, so no inaccessible Worker host features
        // are required on this lightweight invocation context.
        return new Mock<FunctionContext>().Object;
    }

    private static NostifyValidationExceptionMiddleware CreateMiddleware(
        Mock<ILogger<NostifyValidationExceptionMiddleware>> logger,
        HttpResponseData? response)
    {
        return new NostifyValidationExceptionMiddleware(logger.Object, _ => response);
    }

    private static Mock<ILogger<NostifyValidationExceptionMiddleware>> CreateEnabledLogger()
    {
        var logger = new Mock<ILogger<NostifyValidationExceptionMiddleware>>();
        logger.Setup(candidate => candidate.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return logger;
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseData response)
    {
        response.Body.Position = 0;
        return await JsonDocument.ParseAsync(response.Body);
    }

    private static void VerifyLogCount(
        Mock<ILogger<NostifyValidationExceptionMiddleware>> logger,
        LogLevel level,
        int count,
        int? eventId = null)
    {
        logger.Verify(
            candidate => candidate.Log(
                level,
                It.Is<EventId>(id => eventId == null || id.Id == eventId),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(count));
    }
}
