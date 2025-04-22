using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace nostify;

/// <summary>
/// Validates an aggregate instance.
/// </summary>
public interface IAggregateValidator
{
    /// <summary>
    /// Validates the aggregate and returns any ValidationErrors.
    /// </summary>
    /// <typeparam name="T">The type of the aggregate/projection.</typeparam>
    /// <param name="aggregate">The aggregate to validate.</param>
    /// <returns>A list of ValidationErrors.</returns>
    IReadOnlyList<ValidationError> Validate<T>(T aggregate);
}

/// <summary>
/// Validates aggregates by checking max string‑length attributes (or config defaults).
/// </summary>
public sealed class AggregateValidator : IAggregateValidator
{
    /// <summary>
    /// The configuration key for the default maximum string length; defaults to "Nostify:Validation:DefaultMaxStringLength".
    /// </summary>
    public string DefaultMaxStringLengthConfigKey { get; set; } = "Nostify:Validation:DefaultMaxStringLength";
    
    /// <summary>
    /// The default maximum string length; defaults to 1024
    /// </summary>
    public int? DefaultMaxStringLengthValue { get; set; } = 1024;

    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<Type, object> _cache = new();

    /// <summary>
    /// Creates a new AggregateValidator with the specified configuration.
    /// </summary>
    /// <param name="config">The configuration.</param>
    public AggregateValidator(IConfiguration config)
    {
        _config = config;
        DefaultMaxStringLengthValue = config.GetValue<int?>(DefaultMaxStringLengthConfigKey, DefaultMaxStringLengthValue);
    }

    /// <inheritdoc />
    public IReadOnlyList<ValidationError> Validate<T>(T aggregate)
    {
        if (aggregate is null) throw new ArgumentNullException(nameof(aggregate));

        // get/build the cached array of ValidationRule<T>
        var rules = (ValidationRule<T>[])_cache.GetOrAdd(
            typeof(T),
            _ => BuildRules<T>());

        var errors = new List<ValidationError>();
        foreach (var rule in rules)
        {
            var err = rule(aggregate);
            if (err != null)
            {
                errors.Add(err);
            }
        }

        return errors;
    }

    private ValidationRule<T>[] BuildRules<T>()
    {
        var rules = new List<ValidationRule<T>>();
        var allProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var handledProperties = new HashSet<PropertyInfo>();

        // Process attributes first
        foreach (var prop in allProps)
        {
            var validationAttributes = prop.GetCustomAttributes().OfType<IValidationAttribute>().ToList();
            if (validationAttributes.Any())
            {
                // Mark property as handled so the global default isn't applied later
                foreach (var attr in validationAttributes)
                {
                    // Pass the non-nullable fallback int to CreateRule
                    // MaxStringLengthAttribute uses this if it doesn't define its own Length or ConfigKey
                    var rule = attr.CreateRule<T>(prop, _config);
                    if (rule != null)
                    {
                        rules.Add(rule);
                        handledProperties.Add(prop);
                    }
                }
            }
        }

        // Now process string properties *without* attributes, if the global default length is set
        if (DefaultMaxStringLengthValue.HasValue) // Check if a global default was configured
        {
            var maxLength = DefaultMaxStringLengthValue.Value;
            var stringProps = allProps
                .Where(p => p.PropertyType == typeof(string) && !handledProperties.Contains(p));

            foreach (var prop in stringProps)
            {
                // Create a default MaxStringLength rule for unattributed string properties
                rules.Add(instance =>
                {
                    if (instance != null)
                    {
                        var str = prop.GetValue(instance) as string;
                        if (!string.IsNullOrEmpty(str) && str.Length > maxLength)
                        {
                            return new ValidationError(
                                prop.Name,
                                $"Length {str.Length} exceeds max {maxLength}.");
                        }
                    }
                    return null;
                });
            }
        }

        return rules.Where(rule => rule != null).ToArray();
    }
}