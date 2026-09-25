using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace nostify;

/// <summary>
/// Resolves and caches the finite set of logical event names a projection can apply.
/// </summary>
internal static class HandledEventTypeResolver
{
    private static readonly ConcurrentDictionary<Type, Resolution> _cache = new();

    /// <summary>
    /// Describes the handled-event names discovered for one applyable type.
    /// </summary>
    internal sealed class Resolution
    {
        internal Resolution(IReadOnlySet<string> eventTypeNames, bool isDeterminate, string? indeterminateReason = null)
        {
            EventTypeNames = eventTypeNames;
            IsDeterminate = isDeterminate;
            IndeterminateReason = indeterminateReason;
        }

        /// <summary>
        /// Gets the case-sensitive logical event names that were discovered.
        /// </summary>
        internal IReadOnlySet<string> EventTypeNames { get; }

        /// <summary>
        /// Gets whether reflection produced a complete finite handled-event set.
        /// </summary>
        internal bool IsDeterminate { get; }

        /// <summary>
        /// Gets the reason a complete finite set could not be inferred.
        /// </summary>
        internal string? IndeterminateReason { get; }
    }

    /// <summary>
    /// Gets the cached handled-event resolution for a projection or aggregate type.
    /// </summary>
    internal static Resolution GetOrBuild(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        return _cache.GetOrAdd(targetType, Build);
    }

    private static Resolution Build(Type targetType)
    {
        if (!typeof(NostifyObject).IsAssignableFrom(targetType))
        {
            return Indeterminate($"'{targetType.FullName}' implements IApplyable without deriving from NostifyObject.");
        }

        var eventTypeNames = new HashSet<string>(
            ApplyEventsHandlerCache.GetOrBuildHandlerLookup(targetType).EventTypeNames,
            StringComparer.Ordinal);

        const BindingFlags methodFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var method in targetType.GetMethods(methodFlags))
        {
            if (!string.Equals(method.Name, "Apply", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2 ||
                parameters[1].ParameterType != typeof(IEvent) ||
                parameters[0].ParameterType == typeof(EventType) ||
                !typeof(EventType).IsAssignableFrom(parameters[0].ParameterType))
            {
                continue;
            }

            // A concrete EventType parameter is the legacy dynamic-dispatch declaration.
            EventType eventType = EventType.GetRequiredInstance(parameters[0].ParameterType);
            eventTypeNames.Add(eventType.name);
        }

        MethodInfo? catchAll = targetType.GetMethod(
            "Apply",
            methodFlags,
            binder: null,
            types: new[] { typeof(EventType), typeof(IEvent) },
            modifiers: null);
        if (catchAll?.DeclaringType != typeof(NostifyObject))
        {
            return Indeterminate(
                $"'{targetType.FullName}' overrides the catch-all Apply(EventType, IEvent) method.",
                eventTypeNames);
        }

        if (eventTypeNames.Count == 0)
        {
            return Indeterminate($"No event-specific handlers were discovered for '{targetType.FullName}'.");
        }

        return new Resolution(eventTypeNames, isDeterminate: true);
    }

    private static Resolution Indeterminate(string reason, IReadOnlySet<string>? discoveredNames = null)
    {
        return new Resolution(
            discoveredNames ?? new HashSet<string>(StringComparer.Ordinal),
            isDeterminate: false,
            reason);
    }
}
