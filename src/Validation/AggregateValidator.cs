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
    IReadOnlyList<ValidationError> Validate<T>(T? aggregate);
}

/// <summary>
/// Validates aggregates by checking max string‑length attributes (or config defaults).
/// </summary>
public sealed class AggregateValidator : IAggregateValidator
{
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<Type, IReadOnlyList<ValidationRule<object>>> _ruleCache = new();
    private readonly IReadOnlyList<IValidationAttribute> _defaultValidationAttributes;

    /// <summary>
    /// Creates a new AggregateValidator with the specified configuration.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="defaultValidationAttributes">Default validation attributes, if any</param>
    public AggregateValidator(IConfiguration config, params IValidationAttribute[] defaultValidationAttributes)
    {
        _config = config;
        _defaultValidationAttributes = defaultValidationAttributes.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<ValidationError> Validate<T>(T? aggregate)
    {
        var errors = new List<ValidationError>();

        if (aggregate is null)
        {
            // nothing to validate
            return errors.AsReadOnly();
        }

        // get/build the cached array of ValidationRule<T> - this is only done once per type T
        var rules = _ruleCache.GetOrAdd(
            typeof(T),
            type => BuildRules<T>()
                      .Select(rule => (ValidationRule<object>)(obj => rule((T)obj))) 
                      .ToList()
                      .AsReadOnly()
            );

        foreach (var rule in rules)
        {
            var err = rule(aggregate);
            if (err != null)
            {
                errors.Add(err);
            }
        }

        return errors.AsReadOnly();
    }

    private List<ValidationRule<T>> BuildRules<T>()
    {
        var rules = new List<ValidationRule<T>>();
        var allProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // process IValidationAttributes
        foreach (var prop in allProps)
        {
            var validationAttributes = prop.GetCustomAttributes().OfType<IValidationAttribute>().ToList();
            var handledValidationAttributeTypes = new HashSet<Type>();

            foreach (var attr in validationAttributes.Where(v => v.PropertyType == prop.PropertyType))
            {
                try
                {
                    var rule = attr.CreateRule<T>(prop, _config);
                    if (rule != null)
                    {
                        rules.Add(rule);

                        // mark the attribute type as handled so it isn't included in the default attributes
                        handledValidationAttributeTypes.Add(attr.GetType());
                    }
                }
                catch(Exception ex)
                {
                    // don't allow broken CreateRule<T> implementations to crash the app (TODO: is this what we want?)
                    Console.WriteLine($"Error creating rule for {typeof(T).Name}.{prop.Name}: {ex.Message}");
                }
            }

            // get the default attributes that have not been handled (i.e. added to handledValidationAttributeTypes)
            var defaultValidationAttributes = _defaultValidationAttributes.Where(a => !handledValidationAttributeTypes.Contains(a.GetType())).ToList();

            // process default attributes
            foreach (var defaultValidationAttribute in defaultValidationAttributes.Where(a => a.PropertyType == prop.PropertyType))
            {
                try
                {
                    var rule = defaultValidationAttribute.CreateRule<T>(prop, _config);
                    if (rule != null)
                    {
                        rules.Add(rule);
                    }
                }
                catch(Exception ex)
                {
                    // don't allow broken CreateRule<T> implementations to crash the app (TODO: is this what we want?)
                    Console.WriteLine($"Error creating rule for {typeof(T).Name}.{prop.Name}: {ex.Message}");
                }
            }
        }

        return rules;
    }
}