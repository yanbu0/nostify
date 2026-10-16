namespace nostify;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Specifies that a property may only be included in payloads for the configured event types.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class OnlyForAttribute : ValidationAttribute, INostifyValidation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OnlyForAttribute"/> class.
    /// </summary>
    /// <param name="eventName">The event type name for which this property is valid.</param>
    public OnlyForAttribute(string eventName)
    {
        Events = new List<string> { eventName };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OnlyForAttribute"/> class with multiple event type names.
    /// </summary>
    /// <param name="eventNames">The event type names for which this property is valid.</param>
    public OnlyForAttribute(params string[] eventNames)
    {
        Events = [.. eventNames];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OnlyForAttribute"/> class from concrete event type definitions.
    /// </summary>
    /// <param name="eventTypes">
    /// Concrete <see cref="EventType"/> types whose parameterless constructors provide the event type names.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A supplied type does not derive from <see cref="EventType"/>, is not concrete, or has no parameterless constructor.
    /// </exception>
    public OnlyForAttribute(params Type[] eventTypes)
    {
        Events = new List<string>(eventTypes.Length);
        foreach (Type eventType in eventTypes)
        {
            Events.Add(EventType.GetRequiredInstance(eventType).name);
        }
    }

    /// <summary>
    /// Gets the event type names for which this property is valid.
    /// </summary>
    public List<string> Events { get; }

    /// <summary>
    /// Gets the event type names for which this property is valid.
    /// </summary>
    /// <remarks>
    /// This compatibility alias refers to the same list as <see cref="Events"/> and will be removed in the next major version.
    /// </remarks>
    [Obsolete("Commands is retained for backwards compatibility and will be removed in the next major version. Use Events instead.")]
    public List<string> Commands => Events;

    /// <summary>
    /// Determines whether the decorated property is valid for the event type in the validation context.
    /// </summary>
    /// <param name="value">The value of the property being validated.</param>
    /// <param name="validationContext">The context information about the validation operation.</param>
    /// <returns>A validation error when the current event type is not included in <see cref="Events"/>.</returns>
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        EventType? eventType = validationContext.Items.ContainsKey("eventType")
            ? validationContext.Items["eventType"] as EventType
            : null;
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
            return new ValidationResult(
                $"The property '{validationContext.MemberName}' requires an event type to be specified in the validation context.",
                [validationContext.MemberName ?? "Unknown"]);
        }

        if (!Events.Contains(eventTypeName))
        {
            // During payload validation, omitted properties deserialize to null/default values. Check the
            // original payload members so OnlyFor rejects supplied properties without making them required.
            if (validationContext.Items.TryGetValue("payloadProperties", out object? payloadPropertiesValue) &&
                payloadPropertiesValue is ISet<string> payloadProperties &&
                (validationContext.MemberName is null || !payloadProperties.Contains(validationContext.MemberName)))
            {
                return ValidationResult.Success!;
            }

            return new ValidationResult(
                ErrorMessage ?? $"The property '{validationContext.MemberName}' is only valid for event types: {string.Join(", ", Events)}.",
                [validationContext.MemberName ?? "Unknown"]);
        }

        return ValidationResult.Success!;
    }
}
