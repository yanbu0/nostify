using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
        internal sealed class HandlerLookup
        {
            public HandlerLookup(
                Dictionary<EventType, Action<NostifyObject, IEvent>> typedHandlers,
                Dictionary<string, Action<NostifyObject, IEvent>> nameHandlers)
            {
                TypedHandlers = typedHandlers;
                NameHandlers = nameHandlers;
            }

            public Dictionary<EventType, Action<NostifyObject, IEvent>> TypedHandlers { get; }

            public Dictionary<string, Action<NostifyObject, IEvent>> NameHandlers { get; }
        }

        /// <summary>
        /// Cache of handler lookups per concrete NostifyObject type.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, HandlerLookup> _handlerLookups
            = new ConcurrentDictionary<Type, HandlerLookup>();

        /// <summary>
        /// Gets or builds the handler lookup for the specified type.
        /// </summary>
        /// <param name="targetType">Concrete aggregate or projection type deriving from <see cref="NostifyObject"/>.</param>
        /// <returns>
        /// A lookup containing both typed and name-based handler maps for attribute-based dispatch.
        /// Both maps may be empty if the type defines no <see cref="ApplyEventsAttribute"/> handlers.
        /// </returns>
        public static HandlerLookup GetOrBuildHandlerLookup(Type targetType)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            return _handlerLookups.GetOrAdd(targetType, BuildHandlerLookup);
        }

        /// <summary>
        /// Builds typed and name-based handler maps for the given type by scanning for methods decorated
        /// with <see cref="ApplyEventsAttribute"/>.
        /// </summary>
        /// <param name="targetType">Concrete aggregate or projection type.</param>
        /// <returns>A new handler lookup for the type.</returns>
        private static HandlerLookup BuildHandlerLookup(Type targetType)
        {
            var typedMap = new Dictionary<EventType, Action<NostifyObject, IEvent>>();
            var nameMap = new Dictionary<string, Action<NostifyObject, IEvent>>(StringComparer.Ordinal);

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

                // Create a compiled delegate that invokes the method on a NostifyObject target.
                var targetParameter = Expression.Parameter(typeof(NostifyObject), "target");
                var eventParameter = Expression.Parameter(typeof(IEvent), "evt");
                var methodCall = Expression.Call(
                    Expression.Convert(targetParameter, targetType),
                    method,
                    eventParameter);
                var handler = Expression
                    .Lambda<Action<NostifyObject, IEvent>>(methodCall, targetParameter, eventParameter)
                    .Compile();

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

                            var eventTypeInstance = ResolveEventTypeInstance(etType, targetType, method);

                            if (typedMap.ContainsKey(eventTypeInstance))
                            {
                                // Conflict: same EventType mapped to more than one method on this type.
                                throw new InvalidOperationException(
                                    $"Multiple ApplyEventsAttribute handlers found for event type '{eventTypeInstance}' on type '{targetType.FullName}'. " +
                                    "Each event type must map to exactly one method.");
                            }

                            typedMap[eventTypeInstance] = handler;
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

                            if (nameMap.ContainsKey(eventName))
                            {
                                throw new InvalidOperationException(
                                    $"Multiple ApplyEventsAttribute handlers found for event name '{eventName}' on type '{targetType.FullName}'. " +
                                    "Each event name must map to exactly one method.");
                            }

                            nameMap[eventName] = handler;
                        }
                    }
                }
            }

            return new HandlerLookup(typedMap, nameMap);
        }

        /// <summary>
        /// Resolves the canonical EventType instance for the given CLR type.
        /// </summary>
        private static EventType ResolveEventTypeInstance(Type etType, Type targetType, MemberInfo? member)
        {
            try
            {
                return EventType.GetRequiredInstance(etType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve the canonical EventType instance for type '{etType.FullName}' used in ApplyEventsAttribute on '{targetType.FullName}{(member != null ? "." + member.Name : string.Empty)}'. {ex.Message}",
                    ex);
            }
        }
    }
}
