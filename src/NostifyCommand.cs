
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace nostify;

///<summary>
///Defines command being delivered to the event store
///</summary>
public class NostifyCommand
{

    ///<summary>
    ///Name of command, MUST BE UNIQUE - should follow convention "{Action}_{Entity Name}", ie - "Create_User".  This will also become the name of the related Kafka topic.
    ///</summary>
    public string name { get; }

    /// <summary>
    /// Signifies if this command results in the creation of a new aggregate.  Used to key multiple downstream processes with Projections.
    /// </summary>
    public bool isNew { get; }

    /// <summary>
    /// Null payload throws an exception by default, but can be overridden by setting this property to true.
    /// </summary>
    public bool allowNullPayload { get; }

    ///<summary>
    ///Base Constructor
    ///</summary>
    ///<param name="name">Human readable friendly name of command</param>
    ///<param name="isNew">Signifies if this command results in the creation of a new aggregate</param>
    /// <param name="allowNullPayload">Allows null payloads to be sent with this command</param>
    public NostifyCommand(string name, bool isNew = false, bool allowNullPayload = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name cannot be null or empty", nameof(name));

        this.name = name;
        this.isNew = isNew;
        this.allowNullPayload = allowNullPayload;
    }

    ///<summary>
    ///Overrides default ToString to return Name property
    ///</summary>
    public override string ToString() => name;

    ///<summary>
    ///Defines equality
    ///</summary>
    public override bool Equals(object obj)
    {
        var otherValue = obj as NostifyCommand;

        // if (otherValue == null)
        //     return false;

        var t = obj.GetType();
        var t2 = GetType();
        var typeMatches = typeof(NostifyCommand).IsAssignableFrom(obj.GetType());
        var valueMatches = name.Equals(otherValue.name);

        return typeMatches && valueMatches;
    }

    // Josh Bloch hashing implementation
    ///<summary>
    ///Overrides default hash code
    ///</summary>
    public override int GetHashCode()
    {
        unchecked // Overflow is fine, just wrap
        {
            //Prime numbers make better hash
            int hash = 17;
            // Suitable nullity checks etc, of course :)
            hash = hash * 23 + name.GetHashCode();
            return hash;
        }
    }

    ///<summary>
    ///Allows sorting by name
    ///</summary>
    public int CompareTo(object other) => name.CompareTo(((NostifyCommand)other).name);

    ///<summary>
    ///Tests if NostifyCommand equals another NostifyCommand
    ///</summary>
    public static bool operator ==(NostifyCommand a, NostifyCommand b)
    {
        return a.Equals(b);
    }

    ///<summary>
    ///Tests if NostifyCommand does not equal another NostifyCommand
    ///</summary>
    public static bool operator !=(NostifyCommand a, NostifyCommand b)
    {
        return !a.Equals(b);
    }
}
