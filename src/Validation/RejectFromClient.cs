namespace nostify;

using System;

/// <summary>
/// Marks a property as server-managed so client event payloads cannot update it.
/// </summary>
/// <remarks>
/// Payload validation rejects a supplied property by default. When <see cref="throwsError"/> is
/// <see langword="false"/>, the property is removed from the payload and the ignored update is logged.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class RejectFromClientAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RejectFromClientAttribute"/> class.
    /// </summary>
    /// <param name="throwsError">
    /// <see langword="true"/> to fail payload validation when the property is supplied;
    /// <see langword="false"/> to remove and log the supplied property.
    /// </param>
    public RejectFromClientAttribute(bool throwsError = true)
    {
        this.throwsError = throwsError;
    }

    /// <summary>
    /// Gets whether supplying the decorated property causes payload validation to fail.
    /// </summary>
    public bool throwsError { get; }
}
