using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace nostify;

/// <summary>
/// A validation error record.
/// </summary>
/// <param name="Property">The name of the property that failed validation.</param>
/// <param name="Message">The error message.</param>
public record ValidationError(string Property, string Message);

/// <summary>
/// A validation rule that returns a validation error.
/// </summary>
/// <typeparam name="T">The type of the instance being validated.</typeparam>
/// <param name="instance">The instance being validated.</param>
/// <returns>The validation error, or null if the instance is valid.</returns>
public delegate ValidationError? ValidationRule<T>(T instance);

/// <summary>
/// An attribute that defines a validation rule.
/// </summary>
public interface IValidationAttribute
{
    /// <summary>
    /// The types of properties that the validation rule applies to.
    /// </summary>
    public Type PropertyType { get; }

    /// <summary>
    /// Creates a validation rule for the specified property.
    /// </summary>
    /// <typeparam name="T">The type of the instance being validated.</typeparam>
    /// <param name="property">The property to validate.</param>
    /// <param name="config">The configuration.</param>
    /// <returns>The validation rule.</returns>
    ValidationRule<T>? CreateRule<T>(
        PropertyInfo property,
        IConfiguration config);
}

/// <summary>
/// Specifies the maximum length of a string property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class MaxStringLengthAttribute : Attribute, IValidationAttribute
{
    /// <summary>
    /// Creates a new instance with the specified maximum length.
    /// </summary>
    /// <param name="length">The maximum length of the string property.</param>
    public MaxStringLengthAttribute(int length) { Length = length; }

    /// <summary>
    /// Creates a new instance with the specified configuration key.
    /// </summary>
    /// <param name="configKey">The configuration key for the maximum length.</param>
    public MaxStringLengthAttribute(string configKey) { ConfigKey = configKey; }

    /// <summary>
    /// The maximum length of the string property.
    /// </summary>
    public int? Length { get; }
    /// <summary>
    /// The configuration key for the maximum length.
    /// </summary>
    public string? ConfigKey { get; }

    /// <inheritdoc/>
    public Type PropertyType => typeof(string);

    /// <inheritdoc/>
    public ValidationRule<T>? CreateRule<T>(
        PropertyInfo property,
        IConfiguration config)
    {
        if (PropertyType != property.PropertyType)
        {
            // don't try to validate a property of the wrong type
            return null;
        }

        int? maxLength;
        if (Length.HasValue)
        {
            maxLength = Length;
        }
        else if (!string.IsNullOrEmpty(ConfigKey) && int.TryParse(config[ConfigKey], out var cfgLen))
        {
            maxLength = cfgLen;
        }
        else
        {
            return null;
        }

        // return a function that takes an Aggregate or Projection instance and validates the property
        return instance =>
        {
            var str = (string?)property.GetValue(instance);
            if (!string.IsNullOrEmpty(str) && str.Length > maxLength)
            {
                return new ValidationError(
                    property.Name,
                    $"Length {str.Length} exceeds max {maxLength}.");
            }
            
            return null;
        };
    }
}
