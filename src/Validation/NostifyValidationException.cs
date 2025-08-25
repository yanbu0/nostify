using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace nostify;

/// <summary>
/// Exception thrown when validation fails for Nostify objects.
/// Contains a list of validation messages detailing the specific failures.
/// </summary>
public class NostifyValidationException : ValidationException
{
    /// <summary>
    /// Gets the list of validation messages that describe the validation failures.
    /// </summary>
    public List<ValidationResult> ValidationMessages { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NostifyValidationException"/> class
    /// with a single validation message.
    /// </summary>
    /// <param name="message">The validation error message.</param>
    public NostifyValidationException(string message) : base(message)
    {
        ValidationMessages = new List<ValidationResult> { new ValidationResult(message) };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NostifyValidationException"/> class
    /// with a list of validation messages.
    /// </summary>
    /// <param name="validationMessages">The list of validation messages.</param>
    public NostifyValidationException(List<ValidationResult> validationMessages) 
        : base(BuildErrorMessage(validationMessages))
    {
        ValidationMessages = validationMessages ?? new List<ValidationResult>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NostifyValidationException"/> class
    /// with a list of validation messages and an inner exception.
    /// </summary>
    /// <param name="validationMessages">The list of validation messages.</param>
    /// <param name="innerException">The inner exception.</param>
    public NostifyValidationException(List<ValidationResult> validationMessages, Exception innerException) 
        : base(BuildErrorMessage(validationMessages), innerException)
    {
        ValidationMessages = validationMessages ?? new List<ValidationResult>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NostifyValidationException"/> class
    /// with a custom message and a list of validation messages.
    /// </summary>
    /// <param name="message">The custom error message.</param>
    /// <param name="validationMessages">The list of validation messages.</param>
    public NostifyValidationException(string message, List<ValidationResult> validationMessages) 
        : base(message)
    {
        ValidationMessages = validationMessages ?? new List<ValidationResult>();
    }

    /// <summary>
    /// Gets all error messages from the validation results as a single formatted string.
    /// </summary>
    /// <returns>A formatted string containing all validation error messages.</returns>
    public string GetAllErrorMessages()
    {
        if (ValidationMessages == null || !ValidationMessages.Any())
            return Message;

        return string.Join(" ", ValidationMessages.Select(vm => vm.ErrorMessage));
    }

    /// <summary>
    /// Gets validation errors grouped by member name.
    /// </summary>
    /// <returns>A dictionary where keys are member names and values are lists of error messages.</returns>
    public Dictionary<string, List<string>> GetErrorsByMember()
    {
        var errorsByMember = new Dictionary<string, List<string>>();

        if (ValidationMessages == null || !ValidationMessages.Any())
            return errorsByMember;

        foreach (var validationResult in ValidationMessages)
        {
            var memberNames = validationResult.MemberNames?.ToList() ?? new List<string> { "Unknown" };
            
            foreach (var memberName in memberNames)
            {
                if (!errorsByMember.ContainsKey(memberName))
                    errorsByMember[memberName] = new List<string>();

                if (!string.IsNullOrEmpty(validationResult.ErrorMessage))
                    errorsByMember[memberName].Add(validationResult.ErrorMessage);
            }
        }

        return errorsByMember;
    }

    /// <summary>
    /// Builds a comprehensive error message from the validation results.
    /// </summary>
    /// <param name="validationMessages">The validation messages to build the error from.</param>
    /// <returns>A formatted error message string.</returns>
    private static string BuildErrorMessage(List<ValidationResult> validationMessages)
    {
        if (validationMessages == null || !validationMessages.Any())
            return "Validation failed with no specific errors.";

        var errorMessages = validationMessages
            .Where(vm => !string.IsNullOrEmpty(vm.ErrorMessage))
            .Select(vm => vm.ErrorMessage);

        return $"Validation failed: {string.Join(" ", errorMessages)}";
    }
}
