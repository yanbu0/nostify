using Microsoft.Azure.Functions.Worker;
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
    private readonly ILogger<NostifyValidationExceptionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NostifyValidationExceptionMiddleware"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public NostifyValidationExceptionMiddleware(ILogger<NostifyValidationExceptionMiddleware> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        _logger.LogWarning("Validation failed: {ValidationErrors}", validationEx.GetAllErrorMessages());

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

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        // Try to set the HTTP response for the function context
        try
        {
            var httpResponseData = context.GetHttpResponseData();
            if (httpResponseData != null)
            {
                httpResponseData.StatusCode = HttpStatusCode.BadRequest;
                httpResponseData.Headers.Add("Content-Type", "application/json");
                
                using var writer = new StreamWriter(httpResponseData.Body);
                await writer.WriteAsync(jsonResponse);
            }
            else
            {
                // For non-HTTP triggered functions, just log the validation error
                _logger.LogError("Validation error in non-HTTP function: {ValidationErrors}", validationEx.GetAllErrorMessages());
            }
        }
        catch (Exception ex)
        {
            // Fallback: log the validation error if we can't set HTTP response
            _logger.LogError(ex, "Failed to set HTTP response for validation error. Original validation errors: {ValidationErrors}", 
                validationEx.GetAllErrorMessages());
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
