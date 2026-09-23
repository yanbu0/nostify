using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace nostify;

/// <summary>
/// Middleware that catches NostifyValidationException and converts it to appropriate HTTP responses.
/// This middleware provides consistent error handling for validation failures across all Azure Functions.
/// </summary>
public class NostifyValidationExceptionMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly Action<ILogger, string, Exception?> LogValidationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogValidationFailure)),
            "Validation failed: {ValidationErrors}");

    private static readonly Action<ILogger, string, Exception?> LogNonHttpValidationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(LogNonHttpValidationFailure)),
            "Validation error in non-HTTP function: {ValidationErrors}");

    private static readonly Action<ILogger, string, Exception?> LogResponseWriteFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(LogResponseWriteFailure)),
            "Failed to set HTTP response for validation error. Original validation errors: {ValidationErrors}");

    private readonly ILogger<NostifyValidationExceptionMiddleware> _logger;
    private readonly Func<FunctionContext, HttpResponseData?> _responseResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="NostifyValidationExceptionMiddleware"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public NostifyValidationExceptionMiddleware(ILogger<NostifyValidationExceptionMiddleware> logger)
        : this(logger, static context => context.GetHttpResponseData())
    {
    }

    /// <summary>
    /// Initializes middleware with an injectable HTTP response resolver for deterministic testing.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="responseResolver">Resolves the HTTP response for the current invocation.</param>
    internal NostifyValidationExceptionMiddleware(
        ILogger<NostifyValidationExceptionMiddleware> logger,
        Func<FunctionContext, HttpResponseData?> responseResolver)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _responseResolver = responseResolver ?? throw new ArgumentNullException(nameof(responseResolver));
    }

    /// <summary>
    /// Invokes the middleware to handle the function execution and catch validation exceptions.
    /// </summary>
    /// <param name="context">The function context.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (NostifyValidationException validationEx)
        {
            await HandleValidationException(context, validationEx);
        }
        catch (Exception ex) when (ex.InnerException is NostifyValidationException innerValidationEx)
        {
            await HandleValidationException(context, innerValidationEx);
        }
    }

    /// <summary>
    /// Handles NostifyValidationException by creating an appropriate HTTP response.
    /// </summary>
    /// <param name="context">The function context.</param>
    /// <param name="validationEx">The validation exception to handle.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task HandleValidationException(FunctionContext context, NostifyValidationException validationEx)
    {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            LogValidationFailure(_logger, validationEx.GetAllErrorMessages(), null);
        }

        var response = new ValidationErrorResponse
        {
            Message = "Validation failed",
            Errors = validationEx.GetErrorsByMember(),
            Details = validationEx.ValidationMessages?.Select(vm => new ValidationErrorDetail
            {
                ErrorMessage = vm.ErrorMessage,
                MemberNames = vm.MemberNames?.ToArray()
            }).ToArray()
        };

        var jsonResponse = JsonSerializer.Serialize(response, IndentedJsonOptions);

        // Try to set the HTTP response for the function context.
        try
        {
            var httpResponseData = _responseResolver(context);
            if (httpResponseData != null)
            {
                httpResponseData.StatusCode = HttpStatusCode.BadRequest;
                httpResponseData.Headers.Add("Content-Type", "application/json");

                // The Functions host owns the response stream; disposing this writer must not
                // close the body before the host serializes the completed invocation response.
                using var writer = new StreamWriter(
                    httpResponseData.Body,
                    System.Text.Encoding.UTF8,
                    bufferSize: 1024,
                    leaveOpen: true);
                await writer.WriteAsync(jsonResponse);
            }
            else if (_logger.IsEnabled(LogLevel.Error))
            {
                // Non-HTTP functions cannot return a validation response, so retain the failure in their logs.
                LogNonHttpValidationFailure(_logger, validationEx.GetAllErrorMessages(), null);
            }
        }
        catch (Exception ex)
        {
            // Preserve both the response-writing failure and the original validation details.
            if (_logger.IsEnabled(LogLevel.Error))
            {
                LogResponseWriteFailure(_logger, validationEx.GetAllErrorMessages(), ex);
            }
        }
    }
}

/// <summary>
/// Represents the structure of a validation error response.
/// </summary>
public class ValidationErrorResponse
{
    /// <summary>
    /// Gets or sets the main error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the validation errors grouped by member name.
    /// </summary>
    public Dictionary<string, List<string>> Errors { get; set; } = new();

    /// <summary>
    /// Gets or sets the detailed validation error information.
    /// </summary>
    public ValidationErrorDetail[]? Details { get; set; }
}

/// <summary>
/// Represents detailed information about a validation error.
/// </summary>
public class ValidationErrorDetail
{
    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the names of the members that failed validation.
    /// </summary>
    public string[]? MemberNames { get; set; }
}
