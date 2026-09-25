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
    /// The cache is keyed by concrete target type and maps each logical event name to a
    /// compiled delegate that accepts (NostifyObject target, IEvent eventToApply).
    /// </summary>
    internal static class ApplyEventsHandlerCache
    {
        internal sealed class HandlerLookup
        {
            public HandlerLookup(Dictionary<string, Action<NostifyObject, IEvent>> handlers)
            {
                Handlers = handlers;
            }

            /// <summary>
            /// Gets handlers keyed by the case-sensitive logical <see cref="EventType.name"/>.
            /// The keys also provide the canonical event names used by handled-event discovery.
            /// </summary>
            public Dictionary<string, Action<NostifyObject, IEvent>> Handlers { get; }

            /// <summary>
            /// Gets the case-sensitive logical event names declared by
            /// <see cref="ApplyEventsAttribute"/> handlers.
            /// </summary>
            public IReadOnlyCollection<string> EventTypeNames => Handlers.Keys;
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
        /// A logical-name lookup for attribute-based dispatch. The map may be empty if the type
        /// defines no <see cref="ApplyEventsAttribute"/> handlers.
        /// </returns>
        public static HandlerLookup GetOrBuildHandlerLookup(Type targetType)
        {
            ArgumentNullException.ThrowIfNull(targetType);

            return _handlerLookups.GetOrAdd(targetType, BuildHandlerLookup);
        }

        /// <summary>
        /// Builds a logical-name handler map for the given type by scanning for methods decorated
        /// with <see cref="ApplyEventsAttribute"/>.
        /// </summary>
        /// <param name="targetType">Concrete aggregate or projection type.</param>
        /// <returns>A new handler lookup for the type.</returns>
        private static HandlerLookup BuildHandlerLookup(Type targetType)
        {
            var handlerMap = new Dictionary<string, Action<NostifyObject, IEvent>>(StringComparer.Ordinal);
            var declaringMethods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

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
                    // Type declarations are validation and authoring conveniences. Resolve their
                    // canonical metadata once, then retain only the stable logical name.
                    foreach (var eventTypeType in attr.EventTypeTypes)
                    {
                        if (eventTypeType == null)
                        {
                            continue;
                        }

                        if (!typeof(EventType).IsAssignableFrom(eventTypeType))
                        {
                            throw new InvalidOperationException(
                                $"Type '{eventTypeType.FullName}' used in ApplyEventsAttribute on '{targetType.FullName}.{method.Name}' " +
                                "does not derive from EventType.");
                        }

                        var eventTypeInstance = ResolveEventTypeInstance(eventTypeType, targetType, method);
                        AddHandler(eventTypeInstance.name, handler, method, targetType, handlerMap, declaringMethods);
                    }

                    // String declarations already provide the stable logical identity directly.
                    foreach (var eventName in attr.EventTypeNames)
                    {
                        if (string.IsNullOrWhiteSpace(eventName))
                        {
                            throw new InvalidOperationException(
                                $"EventType name used in ApplyEventsAttribute on '{targetType.FullName}.{method.Name}' " +
                                "cannot be null, empty, or whitespace.");
                        }

                        AddHandler(eventName, handler, method, targetType, handlerMap, declaringMethods);
                    }
                }
            }

            return new HandlerLookup(handlerMap);
        }

        /// <summary>
        /// Adds one logical handler mapping and rejects collisions across all attribute forms.
        /// </summary>
        private static void AddHandler(
            string eventName,
            Action<NostifyObject, IEvent> handler,
            MethodInfo method,
            Type targetType,
            Dictionary<string, Action<NostifyObject, IEvent>> handlerMap,
            Dictionary<string, MethodInfo> declaringMethods)
        {
            if (declaringMethods.TryGetValue(eventName, out var existingMethod))
            {
                throw new InvalidOperationException(
                    $"Multiple ApplyEventsAttribute handlers found for event name '{eventName}' on type '{targetType.FullName}': " +
                    $"'{existingMethod.Name}' and '{method.Name}'. Each event name must map to exactly one method.");
            }

            declaringMethods[eventName] = method;
            handlerMap[eventName] = handler;
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
