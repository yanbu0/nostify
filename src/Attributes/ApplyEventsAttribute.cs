using System;
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
        /// Initializes a new instance of the <see cref="ApplyEventsAttribute"/> class.
        /// Uses <see cref="Type"/> parameters to remain compatible with C# attribute
        /// constructor rules while still allowing resolution to concrete <see cref="EventType"/>
        /// instances at runtime.
        /// </summary>
        /// <param name="eventTypeTypes">
        /// One or more CLR types deriving from <see cref="EventType"/> that the target
        /// method can handle.
        /// </param>
        public ApplyEventsAttribute(params Type[] eventTypeTypes)
        {
            if (eventTypeTypes == null || eventTypeTypes.Length == 0)
            {
                throw new ArgumentException("At least one EventType type must be provided.", nameof(eventTypeTypes));
            }

            EventTypeTypes = eventTypeTypes;
        }
    }
}
