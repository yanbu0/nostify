using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace nostify
{
    /// <summary>
    /// Caches attribute-based ApplyEvents handlers for NostifyObject-derived types.
    /// The cache is keyed by concrete type and maps <see cref="EventType"/> to a compiled
    /// delegate that accepts (NostifyObject target, IEvent eventToApply).
    /// </summary>
    internal static class ApplyEventsHandlerCache
    {
        /// <summary>
        /// Cache of handler maps per concrete NostifyObject type.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, Dictionary<EventType, Action<NostifyObject, IEvent>>> _handlerMaps
            = new ConcurrentDictionary<Type, Dictionary<EventType, Action<NostifyObject, IEvent>>>();

        /// <summary>
        /// Gets or builds the handler map for the specified type.
        /// </summary>
        /// <param name="targetType">Concrete aggregate or projection type deriving from <see cref="NostifyObject"/>.</param>
        /// <returns>
        /// A dictionary mapping <see cref="EventType"/> to an invocation delegate for attribute-based handlers.
        /// May be empty if the type defines no <see cref="ApplyEventsAttribute"/> handlers.
        /// </returns>
        public static Dictionary<EventType, Action<NostifyObject, IEvent>> GetOrBuildHandlerMap(Type targetType)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            return _handlerMaps.GetOrAdd(targetType, BuildHandlerMap);
        }

        /// <summary>
        /// Builds the handler map for the given type by scanning for methods decorated
        /// with <see cref="ApplyEventsAttribute"/>.
        /// </summary>
        /// <param name="targetType">Concrete aggregate or projection type.</param>
        /// <returns>A new handler map for the type.</returns>
        private static Dictionary<EventType, Action<NostifyObject, IEvent>> BuildHandlerMap(Type targetType)
        {
            var map = new Dictionary<EventType, Action<NostifyObject, IEvent>>();

            // Build a lookup of known EventType instances for this target type keyed by EventType.name.
            // This is used to resolve string-based ApplyEventsAttribute mappings.
            var nameToEventType = BuildEventTypeNameLookup(targetType);

            // Scan instance methods (public and non-public) to allow protected Apply methods.
            var methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                // Only consider methods that take a single IEvent parameter.
                var parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(IEvent))
                {
                    continue;
                }

                var attributes = method.GetCustomAttributes(typeof(ApplyEventsAttribute), inherit: true)
                    .Cast<ApplyEventsAttribute>()
                    .ToArray();

                if (attributes.Length == 0)
                {
                    continue; // No ApplyEvents attributes on this method.
                }

                if (method.ReturnType != typeof(void))
                {
                    throw new InvalidOperationException(
                        $"ApplyEventsAttribute handler '{targetType.FullName}.{method.Name}' must return void.");
                }

                // Create a delegate that invokes the method on a NostifyObject target.
                Action<NostifyObject, IEvent> handler = (nostifyObject, evt) =>
                {
                    method.Invoke(nostifyObject, new object[] { evt });
                };

                foreach (var attr in attributes)
                {
                    // Type-based mappings (existing behaviour).
                    if (attr.EventTypeTypes != null)
                    {
                        foreach (var etType in attr.EventTypeTypes)
                        {
                            if (etType == null)
                            {
                                continue;
                            }

                            if (!typeof(EventType).IsAssignableFrom(etType))
                            {
                                throw new InvalidOperationException(
                                    $"Type '{etType.FullName}' used in ApplyEventsAttribute on '{targetType.FullName}.{method.Name}' " +
                                    "does not derive from EventType.");
                            }

                            // Resolve a concrete EventType instance for this CLR type. For nostify command/event types
                            // following the template pattern, prefer a public static 'Instance' field if present; otherwise
                            // fall back to Activator.CreateInstance.
                            var eventTypeInstance = ResolveEventTypeInstance(etType, targetType, method);

                            if (eventTypeInstance == null)
                            {
                                throw new InvalidOperationException(
                                    $"Unable to create or resolve an EventType instance for type '{etType.FullName}' " +
                                    $"used in ApplyEventsAttribute on '{targetType.FullName}.{method.Name}'.");
                            }

                            if (map.ContainsKey(eventTypeInstance))
                            {
                                // Conflict: same EventType mapped to more than one method on this type.
                                throw new InvalidOperationException(
                                    $"Multiple ApplyEventsAttribute handlers found for event type '{eventTypeInstance}' on type '{targetType.FullName}'. " +
                                    "Each event type must map to exactly one method.");
                            }

                            map[eventTypeInstance] = handler;
                        }
                    }

                    // Name-based mappings (new behaviour).
                    if (attr.EventTypeNames != null)
                    {
                        foreach (var eventName in attr.EventTypeNames)
                        {
                            if (string.IsNullOrWhiteSpace(eventName))
                            {
                                throw new InvalidOperationException(
                                    $"EventType name used in ApplyEventsAttribute on '{targetType.FullName}.{method.Name}' " +
                                    "cannot be null, empty, or whitespace.");
                            }

                            if (!nameToEventType.TryGetValue(eventName, out var eventTypeInstance))
                            {
                                // Treat missing name the same way we treat an invalid EventType Type: configuration error.
                                throw new InvalidOperationException(
                                    $"Unable to resolve an EventType instance for name '{eventName}' " +
                                    $"used in ApplyEventsAttribute on '{targetType.FullName}.{method.Name}'.");
                            }

                            if (map.ContainsKey(eventTypeInstance))
                            {
                                throw new InvalidOperationException(
                                    $"Multiple ApplyEventsAttribute handlers found for event type '{eventTypeInstance}' on type '{targetType.FullName}'. " +
                                    "Each event type must map to exactly one method.");
                            }

                            map[eventTypeInstance] = handler;
                        }
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// Builds a lookup of EventType instances for the specified target type, keyed by EventType.name.
        /// This is used to resolve string-based ApplyEventsAttribute mappings.
        /// </summary>
        private static Dictionary<string, EventType> BuildEventTypeNameLookup(Type targetType)
        {
            var result = new Dictionary<string, EventType>(StringComparer.Ordinal);

            // Discover EventType implementations in the target assembly. Generated command/event types are typically
            // top-level (not nested on the aggregate/projection), so nested-type scanning alone is insufficient.
            foreach (var etType in targetType.Assembly.GetTypes().Where(t => typeof(EventType).IsAssignableFrom(t) && !t.IsAbstract))
            {
                var hasPublicInstanceField = etType.GetField("Instance", BindingFlags.Public | BindingFlags.Static) != null;
                var hasPublicParameterlessCtor = etType.GetConstructor(Type.EmptyTypes) != null;
                if (!hasPublicInstanceField && !hasPublicParameterlessCtor)
                {
                    continue;
                }

                var eventTypeInstance = ResolveEventTypeInstance(etType, targetType, member: null);
                if (result.ContainsKey(eventTypeInstance.name))
                {
                    throw new InvalidOperationException(
                        $"Multiple EventType instances with the same name '{eventTypeInstance.name}' were discovered in assembly '{targetType.Assembly.FullName}'. " +
                        "EventType.name must be unique when using string-based ApplyEventsAttribute mappings.");
                }

                result[eventTypeInstance.name] = eventTypeInstance;
            }

            return result;
        }

        /// <summary>
        /// Populates the provided dictionary with EventType instances discovered from
        /// nested types of the specified container type.
        /// </summary>
        private static void PopulateFromNestedTypes(Type container, Dictionary<string, EventType> result)
        {
            var nestedTypes = container.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var nested in nestedTypes)
            {
                if (!typeof(EventType).IsAssignableFrom(nested))
                {
                    continue;
                }

                // Skip abstract EventType types; they cannot be instantiated and are not
                // directly used as concrete event types in the handler map.
                if (nested.IsAbstract)
                {
                    continue;
                }

                var eventTypeInstance = ResolveEventTypeInstance(nested, container, member: null);
                if (eventTypeInstance == null)
                {
                    continue;
                }

                var name = eventTypeInstance.name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (result.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        $"Multiple EventType instances with the same name '{name}' were discovered on type '{container.FullName}'. " +
                        "EventType.name must be unique per aggregate or projection type when using string-based ApplyEventsAttribute mappings.");
                }

                result[name] = eventTypeInstance;
            }
        }

        /// <summary>
        /// Resolves a concrete EventType instance for the given CLR type using the same rules
        /// as the original ApplyEventsHandlerCache implementation.
        /// </summary>
        private static EventType ResolveEventTypeInstance(Type etType, Type targetType, MemberInfo? member)
        {
            // For nostify command/event types following the template pattern, prefer a public static 'Instance'
            // field if present; otherwise fall back to Activator.CreateInstance.
            var instanceField = etType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceField != null && typeof(EventType).IsAssignableFrom(instanceField.FieldType))
            {
                var value = instanceField.GetValue(null);
                if (value is EventType eventType)
                {
                    return eventType;
                }

                throw new InvalidOperationException(
                    $"Static field 'Instance' on type '{etType.FullName}' used in ApplyEventsAttribute on '{targetType.FullName}{(member != null ? "." + member.Name : string.Empty)}' " +
                    "does not contain a valid EventType instance.");
            }

            var created = Activator.CreateInstance(etType) as EventType;
            if (created == null)
            {
                throw new InvalidOperationException(
                    $"Unable to create an EventType instance for type '{etType.FullName}' used in ApplyEventsAttribute on '{targetType.FullName}{(member != null ? "." + member.Name : string.Empty)}'.");
            }

            return created;
        }
    }
}
