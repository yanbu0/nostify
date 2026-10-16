
using System.Collections.Generic;

namespace nostify;

/// <summary>
/// Defines an event-type-aware validation attribute.
/// </summary>
public interface INostifyValidation
{
    /// <summary>
    /// Gets the event type names for which the decorated property requires validation.
    /// </summary>
    public List<string> Events { get; }

    /// <summary>
    /// Gets the event type names for which the decorated property requires validation.
    /// </summary>
    /// <remarks>
    /// Retained as a compatibility alias for <see cref="Events"/> and will be removed in the next major version.
    /// </remarks>
    [System.Obsolete("Commands is retained for backwards compatibility and will be removed in the next major version. Use Events instead.")]
    public List<string> Commands => Events;
}
