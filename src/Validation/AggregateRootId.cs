namespace nostify;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Requires a supplied property value to exactly match the aggregate root identifier of the event being validated.
/// </summary>
/// <remarks>
/// Apply this attribute to an aggregate identifier property to prevent an event payload from changing the
/// aggregate's identity. Omitted properties remain valid because payload validation only retains errors for
/// properties that were supplied, unless another attribute makes the property required.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class AggregateRootIdAttribute : ValidationAttribute
{
    /// <summary>
    /// Determines whether the decorated property exactly matches the event's aggregate root identifier.
    /// </summary>
    /// <param name="value">The value of the decorated property.</param>
    /// <param name="validationContext">The context for the current event payload validation.</param>
    /// <returns>A validation error when the values differ in an event payload validation context.</returns>
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        if (!validationContext.Items.TryGetValue("aggregateRootId", out object? aggregateRootId))
        {
            return ValidationResult.Success!;
        }

        if (!Equals(value, aggregateRootId))
        {
            return new ValidationResult(
                ErrorMessage ?? $"The property '{validationContext.MemberName}' must exactly match the event aggregate root ID '{aggregateRootId}'.",
                [validationContext.MemberName ?? "Unknown"]);
        }

        return ValidationResult.Success!;
    }
}
