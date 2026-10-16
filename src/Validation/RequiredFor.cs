namespace nostify;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;


/// <summary>
/// Attribute to specify that a property is required for an Event with a specified event type name.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class RequiredForAttribute : RequiredAttribute, INostifyValidation
{

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class.
    /// </summary>
    /// <param name="eventName">The event type name for which this property is required.</param>
    public RequiredForAttribute(string eventName) : base()
    {
        Events = new List<string> { eventName };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class with multiple event type names.
    /// </summary>
    /// <param name="eventNames">The event type names for which this property is required.</param>
    public RequiredForAttribute(params string[] eventNames) : base()
    {
        Events = [.. eventNames];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForAttribute"/> class from concrete event type definitions.
    /// </summary>
    /// <param name="eventTypes">
    /// Concrete <see cref="EventType"/> types whose parameterless constructors provide the event type names.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A supplied type does not derive from <see cref="EventType"/>, is not concrete, or has no parameterless constructor.
    /// </exception>
    public RequiredForAttribute(params Type[] eventTypes) : base()
    {
        Events = new List<string>(eventTypes.Length);
        foreach (Type eventType in eventTypes)
        {
            Events.Add(EventType.GetRequiredInstance(eventType).name);
        }
    }

    /// <summary>
    /// Gets the event type names for which this property requires validation.
    /// </summary>
    public List<string> Events { get; }

    /// <summary>
    /// Gets the event type names for which this property requires validation.
    /// </summary>
    /// <remarks>
    /// This compatibility alias refers to the same list as <see cref="Events"/> and will be removed in the next major version.
    /// </remarks>
    [Obsolete("Commands is retained for backwards compatibility and will be removed in the next major version. Use Events instead.")]
    public List<string> Commands => Events;

    // Apply required validation only when the current event type is included in Events.
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
        EventType? eventType = validationContext.Items.ContainsKey("eventType") ? validationContext.Items["eventType"] as EventType : null;
        string? eventTypeName = eventType?.name;

        if (string.IsNullOrWhiteSpace(eventTypeName) && validationContext.Items.ContainsKey("command"))
        {
#pragma warning disable CS0618 // Continue accepting legacy validation contexts created with NostifyCommand.
            eventTypeName = validationContext.Items["command"] switch
            {
                NostifyCommand command => command.name,
                EventType legacyEventType => legacyEventType.name,
                _ => null
            };
#pragma warning restore CS0618
        }

        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            return new ValidationResult($"The property '{validationContext.MemberName}' requires an event type to be specified in the validation context.");
        }

        if (Events.Contains(eventTypeName))
        {
            bool baseResult = base.IsValid(value);
            if (!baseResult)
            {
                // Preserve the member name so structured validation responses do not discard this error.
                return new ValidationResult(
                    ErrorMessage ?? $"The property '{validationContext.MemberName}' is required for the event type '{eventTypeName}'.",
                    [validationContext.MemberName ?? "Unknown"]);
            }
        }

        // If the event type is not in the list of required event types, return success.
        return ValidationResult.Success!;
    }
}
