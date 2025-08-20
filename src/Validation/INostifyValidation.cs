
using System.Collections.Generic;

namespace nostify;

/// <summary>
/// Interface for validation attributes for Nostify commands.
/// </summary>
public interface INostifyValidation
{
    /// <summary>
    /// Gets the list of commands for which this property requires validation.
    /// </summary>
    public List<string> Commands { get; }
}
