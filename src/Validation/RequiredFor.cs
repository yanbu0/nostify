namespace nostify;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;


/// <summary>
/// Attribute to specify that a property is required for specific event types.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class RequiredForAttribute : RequiredAttribute, INostifyValidation
{

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class.
    /// </summary>
    /// <param name="command">The event type name for which this property is required.</param>
    public RequiredForAttribute(string command) : base()
    {
        Commands = new List<string> { command };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class with multiple commands.
    /// </summary>
    /// <param name="commands">The event type names for which this property is required.</param>
    public RequiredForAttribute(string[] commands) : base()
    {
        Commands = [.. commands];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class for a typed event type.
    /// </summary>
    /// <param name="eventType">The event type for which this property is required.</param>
    public RequiredForAttribute(Type eventType) : base()
    {
        Commands = [ResolveEventTypeName(eventType)];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class for multiple typed event types.
    /// </summary>
    /// <param name="eventTypes">The event types for which this property is required.</param>
    public RequiredForAttribute(Type[] eventTypes) : base()
    {
        Commands = [.. eventTypes.Select(ResolveEventTypeName)];
    }


    /// <summary>
    /// Gets the list of event type names for which this property requires validation.
    /// </summary>
    public List<string> Commands { get; }

    private static string ResolveEventTypeName(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (!typeof(EventType).IsAssignableFrom(eventType))
        {
            throw new ArgumentException($"Type '{eventType.FullName}' must inherit from {nameof(EventType)}.", nameof(eventType));
        }

        if (eventType.IsAbstract)
        {
            throw new ArgumentException($"Type '{eventType.FullName}' must be a concrete {nameof(EventType)}.", nameof(eventType));
        }

        try
        {
            if (Activator.CreateInstance(eventType, nonPublic: true) is EventType eventTypeInstance)
            {
                return eventTypeInstance.name;
            }
        }
        catch (MissingMethodException)
        {
        }

        return eventType.Name;
    }

    // Override the isValid(object, validationcontext) method such that the Commands property is passed in validation context
    // and the validation logic checks if the current event type is in the Commands list.
    /// <summary>
    /// Determines whether the specified value of the object is valid for the given validation context,
    /// considering whether the current event type is in the list of required event types.
    /// </summary>
    /// <param name="value">The value of the object to validate.</param>
    /// <param name="validationContext">The context information about the validation operation.</param>
    /// <returns>
    /// A <see cref="ValidationResult"/> indicating whether the value is valid for the specified context.
    /// </returns>
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        EventType? eventType = validationContext.Items.ContainsKey("eventType") ? validationContext.Items["eventType"] as EventType : validationContext.Items.ContainsKey("command") ? validationContext.Items["command"] as EventType : null;
        if (eventType is null)
        {
            return new ValidationResult($"The property '{validationContext.MemberName}' requires an event type to be specified in the validation context.");
        }

        if (Commands.Contains(eventType.name))
        {
            bool baseResult = base.IsValid(value);
            // If baseResult is null return ValidationResult
            if (!baseResult)
            {
                return new ValidationResult(ErrorMessage ?? $"The property '{validationContext.MemberName}' is required for the event type '{eventType.name}'.");
            }
        }

        // If the event type is not in the list of required event types, return success
        return ValidationResult.Success!;
    }
}
