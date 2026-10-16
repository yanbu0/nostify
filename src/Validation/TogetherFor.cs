namespace nostify;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

/// <summary>
/// Requires every property in a named group to be supplied together for the configured event types.
/// </summary>
/// <remarks>
/// A group remains optional unless another validation attribute, such as <see cref="RequiredForAttribute"/>,
/// requires one of its properties. Once any active group member is present in a payload, every active member
/// with the same group name must also be present. An explicitly supplied null or default value counts as present.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class TogetherForAttribute : ValidationAttribute, INostifyValidation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TogetherForAttribute"/> class.
    /// </summary>
    /// <param name="uniqueGroupName">The name that associates properties which must be supplied together.</param>
    /// <param name="eventNames">The event type names for which the group constraint applies.</param>
    public TogetherForAttribute(string uniqueGroupName, params string[] eventNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueGroupName);
        UniqueGroupName = uniqueGroupName;
        Events = [.. eventNames];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TogetherForAttribute"/> class from concrete event type definitions.
    /// </summary>
    /// <param name="uniqueGroupName">The name that associates properties which must be supplied together.</param>
    /// <param name="eventType">The first concrete <see cref="EventType"/> definition for which the constraint applies.</param>
    /// <param name="additionalEventTypes">Additional concrete <see cref="EventType"/> definitions for the group.</param>
    /// <exception cref="InvalidOperationException">
    /// A supplied type does not derive from <see cref="EventType"/>, is not concrete, or has no parameterless constructor.
    /// </exception>
    public TogetherForAttribute(string uniqueGroupName, Type eventType, params Type[] additionalEventTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueGroupName);
        UniqueGroupName = uniqueGroupName;
        Events = new List<string>(additionalEventTypes.Length + 1)
        {
            EventType.GetRequiredInstance(eventType).name
        };

        foreach (Type additionalEventType in additionalEventTypes)
        {
            Events.Add(EventType.GetRequiredInstance(additionalEventType).name);
        }
    }

    /// <summary>
    /// Gets the unique name shared by properties in this group.
    /// </summary>
    public string UniqueGroupName { get; }

    /// <summary>
    /// Gets the event type names for which the group constraint applies.
    /// </summary>
    public List<string> Events { get; }

    /// <summary>
    /// Gets the event type names for which the group constraint applies.
    /// </summary>
    /// <remarks>
    /// This compatibility alias refers to the same list as <see cref="Events"/> and will be removed in the next major version.
    /// </remarks>
    [Obsolete("Commands is retained for backwards compatibility and will be removed in the next major version. Use Events instead.")]
    public List<string> Commands => Events;

    /// <summary>
    /// Validates that the decorated property is present when another active member of its group is supplied.
    /// </summary>
    /// <param name="value">The deserialized property value. Payload membership, rather than this value, determines presence.</param>
    /// <param name="validationContext">The context for the current payload object and event.</param>
    /// <returns>A validation error for an omitted group member, or success when the group is complete or unused.</returns>
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        string? eventTypeName = GetEventTypeName(validationContext);
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            return new ValidationResult(
                $"The property '{validationContext.MemberName}' requires an event type to be specified in the validation context.",
                [validationContext.MemberName ?? "Unknown"]);
        }

        if (!Events.Contains(eventTypeName, StringComparer.Ordinal))
        {
            return ValidationResult.Success!;
        }

        if (!validationContext.Items.TryGetValue("payloadProperties", out object? payloadPropertiesValue) ||
            payloadPropertiesValue is not ISet<string> suppliedProperties ||
            validationContext.MemberName is null ||
            suppliedProperties.Contains(validationContext.MemberName))
        {
            return ValidationResult.Success!;
        }

        PropertyInfo[] groupProperties = validationContext.ObjectType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttributes<TogetherForAttribute>(inherit: false).Any(attribute =>
                string.Equals(attribute.UniqueGroupName, UniqueGroupName, StringComparison.Ordinal) &&
                attribute.Events.Contains(eventTypeName, StringComparer.Ordinal)))
            .ToArray();
        bool groupWasActivated = groupProperties.Any(property => suppliedProperties.Contains(property.Name)) ||
            groupProperties.Any(property => property.GetCustomAttributes<RequiredForAttribute>(inherit: false)
                .Any(attribute => attribute.Events.Contains(eventTypeName, StringComparer.Ordinal)));

        // A matching RequiredFor declaration activates the optional group, making all of its members required.
        if (!groupWasActivated)
        {
            return ValidationResult.Success!;
        }

        return new ValidationResult(
            ErrorMessage ?? $"The property '{validationContext.MemberName}' must be supplied together with group '{UniqueGroupName}' for event type '{eventTypeName}'.",
            [validationContext.MemberName]);
    }

    /// <summary>
    /// Resolves the current event type name from modern or legacy validation context items.
    /// </summary>
    private static string? GetEventTypeName(ValidationContext validationContext)
    {
        EventType? eventType = validationContext.Items.TryGetValue("eventType", out object? eventTypeValue)
            ? eventTypeValue as EventType
            : null;
        if (!string.IsNullOrWhiteSpace(eventType?.name))
        {
            return eventType.name;
        }

        if (!validationContext.Items.TryGetValue("command", out object? commandValue))
        {
            return null;
        }

#pragma warning disable CS0618 // Continue accepting legacy validation contexts created with NostifyCommand.
        return commandValue switch
        {
            NostifyCommand command => command.name,
            EventType legacyEventType => legacyEventType.name,
            _ => null
        };
#pragma warning restore CS0618
    }
}
