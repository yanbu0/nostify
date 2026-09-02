using System;
using System.Linq;
using nostify;

namespace nostify.Attributes
{
    /// <summary>
    /// Attribute used to declare that a method handles one or more <see cref="EventType"/> values.
    /// Can be applied multiple times to the same method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class ApplyEventsAttribute : Attribute
    {
        /// <summary>
        /// Gets the concrete <see cref="EventType"/> CLR types that this method handles.
        /// These must be types deriving from <see cref="EventType"/> such as command/event
        /// classes following the nostify templates (e.g. <c>Create__ReplaceMe_</c>).
        /// </summary>
        public Type[] EventTypeTypes { get; }

        /// <summary>
        /// Gets the logical <see cref="EventType.name"/> values that this method handles.
        /// These must match the <see cref="EventType.name"/> of a concrete event type that can be
        /// resolved for the containing aggregate or projection.
        /// </summary>
        public string[] EventTypeNames { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplyEventsAttribute"/> class using CLR types.
        /// Uses <see cref="Type"/> parameters to remain compatible with C# attribute constructor rules
        /// while still allowing resolution to concrete <see cref="EventType"/> instances at runtime.
        /// </summary>
        /// <param name="eventTypeTypes">
        /// One or more CLR types deriving from <see cref="EventType"/> that the target method can handle.
        /// </param>
        public ApplyEventsAttribute(params Type[] eventTypeTypes)
        {
            if (eventTypeTypes == null || eventTypeTypes.Length == 0)
            {
                throw new ArgumentException("At least one EventType type must be provided.", nameof(eventTypeTypes));
            }

            EventTypeTypes = eventTypeTypes;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplyEventsAttribute"/> class using logical
        /// <see cref="EventType.name"/> values instead of CLR types.
        /// </summary>
        /// <param name="eventTypeNames">
        /// One or more logical event type names that the target method can handle. These must match the
        /// <see cref="EventType.name"/> of a concrete event type that can be resolved for the containing
        /// aggregate or projection.
        /// </param>
        public ApplyEventsAttribute(params string[] eventTypeNames)
        {
            if (eventTypeNames == null || eventTypeNames.Length == 0)
            {
                throw new ArgumentException("At least one EventType name must be provided.", nameof(eventTypeNames));
            }

            if (eventTypeNames.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("EventType names cannot be null, empty, or whitespace.", nameof(eventTypeNames));
            }

            EventTypeNames = eventTypeNames;
        }
    }
}
