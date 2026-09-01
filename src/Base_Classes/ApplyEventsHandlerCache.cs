using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nostify.Attributes;

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

                // Create a delegate that invokes the method on a NostifyObject target.
                Action<NostifyObject, IEvent> handler = (nostifyObject, evt) =>
                {
                    method.Invoke(nostifyObject, new object[] { evt });
                };

                foreach (var attr in attributes)
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
                        EventType eventTypeInstance;
                        var instanceField = etType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                        if (instanceField != null && typeof(EventType).IsAssignableFrom(instanceField.FieldType))
                        {
                            eventTypeInstance = (EventType)instanceField.GetValue(null);
                        }
                        else
                        {
                            eventTypeInstance = (EventType)Activator.CreateInstance(etType);
                        }

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
            }

            return map;
        }
    }
}
