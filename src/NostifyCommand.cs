using System;

namespace nostify;

/// <summary>
/// Defines command metadata being delivered to the event store.
/// </summary>
[Obsolete("NostifyCommand is deprecated; use EventType for new events. NostifyCommand remains for legacy compatibility.")]
public class NostifyCommand
{
    /// <summary>
    /// Name of command metadata, also used as Kafka topic name.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Signifies if this command metadata results in the creation of a new aggregate.
    /// </summary>
    public bool isNew { get; }

    /// <summary>
    /// Null payload throws an exception by default, but can be overridden by setting this property to true.
    /// </summary>
    public bool allowNullPayload { get; }

    /// <summary>
    /// Base constructor.
    /// </summary>
    /// <param name="name">Human readable friendly name of command. MUST BE UNIQUE - should follow convention "{Action}_{Entity Name}", ie - "Create_User". This will also become the name of the related Kafka topic.</param>
    /// <param name="isNew">Signifies if this command results in the creation of a new aggregate.</param>
    /// <param name="allowNullPayload">Allows null payloads to be sent with this command.</param>
    public NostifyCommand(string name, bool isNew = false, bool allowNullPayload = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event type name cannot be null or empty", nameof(name));

        this.name = name;
        this.isNew = isNew;
        this.allowNullPayload = allowNullPayload;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj == null || obj.GetType() != GetType())
            return false;

        var otherValue = obj as NostifyCommand;
        return otherValue != null && name.Equals(otherValue.name, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + GetType().GetHashCode();
            hash = hash * 23 + name.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    /// Overrides default ToString to return Name property.
    /// </summary>
    public override string ToString() => name;

    /// <summary>
    /// Allows sorting by name.
    /// </summary>
    public int CompareTo(object other)
    {
        var otherCommand = (NostifyCommand)other;
        int nameComparison = string.Compare(name, otherCommand.name, StringComparison.Ordinal);
        if (nameComparison != 0)
        {
            return nameComparison;
        }

        return string.Compare(GetType().FullName, otherCommand.GetType().FullName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts legacy command metadata to the internal legacy <see cref="EventType"/> adapter.
    /// </summary>
    public static implicit operator EventType(NostifyCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new LegacyNostifyCommandEventType(command.name, command.isNew, command.allowNullPayload);
    }

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
