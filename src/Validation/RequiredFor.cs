namespace nostify;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Xunit.Sdk;


/// <summary>
/// Attribute to specify that a property is required for an Event with a specified NostifyCommand.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class RequiredForAttribute : RequiredAttribute, INostifyValidation
{

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class.
    /// </summary>
    /// <param name="command">The command for which this property is required.</param>
    public RequiredForAttribute(string command) : base()
    {
        Commands = new List<string> { command };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class with multiple commands.
    /// </summary>
    /// <param name="commands">The commands for which this property is required.</param>
    public RequiredForAttribute(string[] commands) : base()
    {
        Commands = [.. commands];
    }


    /// <summary>
    /// Gets the list of commands for which this property requires validation.
    /// </summary>
    public List<string> Commands { get; }

    // Override the isValid(object, validationcontext) method such that the Commands property is passed in validation context
    // and the validation logic checks if the current command is in the Commands list.
    /// <summary>
    /// Determines whether the specified value of the object is valid for the given validation context,
    /// considering whether the current command is in the list of required commands.
    /// </summary>
    /// <param name="value">The value of the object to validate.</param>
    /// <param name="validationContext">The context information about the validation operation.</param>
    /// <returns>
    /// A <see cref="ValidationResult"/> indicating whether the value is valid for the specified context.
    /// </returns>
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        NostifyCommand? command = validationContext.Items.ContainsKey("command") ? validationContext.Items["command"] as NostifyCommand : null;
        // If command is null return ValidationResult
        if (command is null)
        {
            return new ValidationResult($"The property '{validationContext.MemberName}' requires a command to be specified in the validation context.");
        }

        if (Commands.Contains(command.name))
        {
            bool baseResult = base.IsValid(value);
            // If baseResult is null return ValidationResult
            if (!baseResult)
            {
                return new ValidationResult(ErrorMessage ?? $"The property '{validationContext.MemberName}' is required for the command '{command.name}'.");
            }
        }

        // If the command is not in the list of required commands, return success
        return ValidationResult.Success!;
    }
}
