using System;

namespace nostify;

/// <summary>
/// Defines command metadata being delivered to the event store.
/// </summary>
[Obsolete("NostifyCommand is deprecated; inherit from EventType instead.")]
public class NostifyCommand : EventType
{
    /// <summary>
    /// Base constructor.
    /// </summary>
    /// <param name="name">Human readable friendly name of command. MUST BE UNIQUE - should follow convention "{Action}_{Entity Name}", ie - "Create_User". This will also become the name of the related Kafka topic.</param>
    /// <param name="isNew">Signifies if this command results in the creation of a new aggregate.</param>
    /// <param name="allowNullPayload">Allows null payloads to be sent with this command.</param>
    public NostifyCommand(string name, bool isNew = false, bool allowNullPayload = false)
        : base(name, isNew, allowNullPayload, "Command")
    {
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => base.Equals(obj);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>
    /// Tests if NostifyCommand equals another NostifyCommand.
    /// </summary>
    public static bool operator ==(NostifyCommand? a, NostifyCommand? b)
    {
        if (a is null) return b is null;
        return a.Equals(b);
    }

    /// <summary>
    /// Tests if NostifyCommand does not equal another NostifyCommand.
    /// </summary>
    public static bool operator !=(NostifyCommand? a, NostifyCommand? b)
    {
        if (a is null) return b is not null;
        return !a.Equals(b);
    }
}
