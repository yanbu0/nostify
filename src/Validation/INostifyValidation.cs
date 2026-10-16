
using System.Collections.Generic;

namespace nostify;

/// <summary>
/// Defines an event-type-aware validation attribute.
/// </summary>
public interface INostifyValidation
{
    /// <summary>
    /// Gets the command names for which the decorated property requires validation.
    /// </summary>
    public List<string> Commands { get; }

    /// <summary>
    /// Gets the event type names for which the decorated property requires validation.
    /// </summary>
    /// <remarks>
    /// Defaults to the published <see cref="Commands"/> contract for compatibility with existing implementations.
    /// </remarks>
    public List<string> Events => Commands;
}
