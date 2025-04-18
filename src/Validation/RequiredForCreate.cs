namespace nostify;

using System;


/// <summary>
/// Attribute to specify that a property is required for an Event with a NostifyCommand where the isNew flag set to true.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class RequiredForCreate : Attribute
{

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredForCreate"/> class.
    /// </summary>
    public RequiredForCreate(bool notEmptyGuid = false)
    {
        NotEmptyGuid = notEmptyGuid;
    }

    /// <summary>
    /// Property indicating required field may not be an Empty Guid
    /// </summary>
    public bool NotEmptyGuid { get; set; } = false;

}
