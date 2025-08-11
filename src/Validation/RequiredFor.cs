namespace nostify;

using System;


/// <summary>
/// Attribute to specify that a property is required for an Event with a specified NostifyCommand.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class RequiredFor : Attribute
{

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredFor"/> class.
    /// </summary>
    public RequiredFor(NostifyCommand command, bool notDefault = false)
    {
        Command = command;
        NotDefault = notDefault;
    }

    /// <summary>
    /// Property indicating required field may not be an Empty Guid
    /// </summary>
    public bool NotDefault { get; set; }

    /// <summary>
    /// Gets the command for which this property is required.
    /// </summary>
    public NostifyCommand Command { get; set; }

}
