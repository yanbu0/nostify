using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;

namespace nostify;

/// <summary>
/// Utility class for handling NostifyValidationException consistently across the application.
/// Provides methods to process validation exceptions and format error responses.
/// </summary>
public static class NostifyValidationExceptionHandler
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Action<ILogger, string, Exception?> LogValidationErrors =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogValidationErrors)),
            "Validation failed: {ValidationErrors}");

    private static readonly Action<ILogger, Exception?> LogNullObject =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(LogNullObject)),
            "Validation failed: Object is null");

    private static readonly Action<ILogger, Exception?> LogMissingEventType =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(LogMissingEventType)),
            "Validation failed: Event type name is null or empty");

    /// <summary>
    /// Processes a NostifyValidationException and returns a structured error response.
    /// </summary>
    /// <param name="validationException">The validation exception to process.</param>
    /// <param name="logger">Optional logger for recording the validation failure.</param>
    /// <returns>A structured validation error response.</returns>
    public static ValidationErrorResponse HandleValidationException(
        NostifyValidationException validationException,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(validationException);

        if (logger != null)
        {
            LogValidationErrors(logger, validationException.GetAllErrorMessages(), null);
        }

        return new ValidationErrorResponse
        {
            Message = "Validation failed",
            Errors = validationException.GetErrorsByMember(),
            Details = validationException.ValidationMessages?.Select(vm => new ValidationErrorDetail
            {
                ErrorMessage = vm.ErrorMessage,
                MemberNames = vm.MemberNames?.ToArray()
            }).ToArray()
        };
    }

    /// <summary>
    /// Processes a list of validation results and returns a structured error response.
    /// </summary>
    /// <param name="validationResults">The validation results to process.</param>
    /// <param name="logger">Optional logger for recording the validation failure.</param>
    /// <returns>A structured validation error response.</returns>
    public static ValidationErrorResponse HandleValidationResults(
        List<ValidationResult> validationResults,
        ILogger? logger = null)
    {
        if (validationResults == null || validationResults.Count == 0)
            return new ValidationErrorResponse { Message = "No validation errors found" };

        var validationException = new NostifyValidationException(validationResults);
        return HandleValidationException(validationException, logger);
    }

    /// <summary>
    /// Converts a validation error response to JSON format.
    /// </summary>
    /// <param name="errorResponse">The error response to serialize.</param>
    /// <param name="indented">Whether to format the JSON with indentation.</param>
    /// <returns>A JSON string representation of the error response.</returns>
    public static string ToJson(ValidationErrorResponse errorResponse, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(errorResponse);

        return JsonSerializer.Serialize(
            errorResponse,
            indented ? IndentedJsonOptions : CompactJsonOptions);
    }

    /// <summary>
    /// Creates a simple error message string from validation results.
    /// </summary>
    /// <param name="validationResults">The validation results to process.</param>
    /// <returns>A concatenated string of all error messages.</returns>
    public static string CreateSimpleErrorMessage(List<ValidationResult> validationResults)
    {
        if (validationResults == null || validationResults.Count == 0)
            return "No validation errors found";

        var errorMessages = validationResults
            .Where(vr => !string.IsNullOrEmpty(vr.ErrorMessage))
            .Select(vr => vr.ErrorMessage);

        return string.Join(" ", errorMessages);
    }

    /// <summary>
    /// Validates an object and returns a validation error response if validation fails.
    /// </summary>
    /// <param name="obj">The object to validate.</param>
    /// <param name="logger">Optional logger for recording validation failures.</param>
    /// <returns>A validation error response if validation fails, null if validation passes.</returns>
    public static ValidationErrorResponse? ValidateObjectAndGetErrorResponse(object obj, ILogger? logger = null)
    {
        if (obj == null)
        {
            var nullError = new ValidationErrorResponse
            {
                Message = "Validation failed",
                Errors = new Dictionary<string, List<string>>
                {
                    { "Object", new List<string> { "Object cannot be null" } }
                }
            };
            if (logger != null) LogNullObject(logger, null);
            return nullError;
        }

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(obj);

        bool isValid = Validator.TryValidateObject(obj, validationContext, validationResults, true);

        if (!isValid && validationResults.Count != 0)
        {
            return HandleValidationResults(validationResults, logger);
        }

        return null; // Validation passed
    }

    /// <summary>
    /// Validates an object for a specific command and returns a validation error response if validation fails.
    /// </summary>
    /// <param name="obj">The object to validate.</param>
    /// <param name="commandName">The command name to validate against.</param>
    /// <param name="logger">Optional logger for recording validation failures.</param>
    /// <returns>A validation error response if validation fails, null if validation passes.</returns>
    public static ValidationErrorResponse? ValidateObjectForCommandAndGetErrorResponse(
        object obj,
        string commandName,
        ILogger? logger = null)
    {
        if (obj == null)
        {
            var nullError = new ValidationErrorResponse
            {
                Message = "Validation failed",
                Errors = new Dictionary<string, List<string>>
                {
                    { "Object", new List<string> { "Object cannot be null" } }
                }
            };
            if (logger != null) LogNullObject(logger, null);
            return nullError;
        }

        if (string.IsNullOrEmpty(commandName))
        {
            var commandError = new ValidationErrorResponse
            {
                Message = "Validation failed",
                Errors = new Dictionary<string, List<string>>
                {
                    { "Event Type", new List<string> { "Event type name cannot be null or empty" } }
                }
            };
            if (logger != null) LogMissingEventType(logger, null);
            return commandError;
        }

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(obj);

        // RequiredFor's published validation context still consumes the legacy command type.
#pragma warning disable CS0618 // Narrow compatibility boundary retained for the published validation contract.
        var command = new NostifyCommand(commandName, true);
#pragma warning restore CS0618
        validationContext.Items["command"] = command;

        bool isValid = Validator.TryValidateObject(obj, validationContext, validationResults, true);

        if (!isValid && validationResults.Count != 0)
        {
            return HandleValidationResults(validationResults, logger);
        }

        return null; // Validation passed
    }
}
