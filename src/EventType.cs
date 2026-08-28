using System;

namespace nostify;

/// <summary>
/// Defines the event type being delivered to the event store.
/// </summary>
public abstract class EventType
{
    /// <summary>
    /// Name of event type, MUST BE UNIQUE - should follow convention "{Action}_{Entity Name}", ie - "Create_User". This will also become the name of the related Kafka topic.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Signifies if this event type results in the creation of a new aggregate. Used to key multiple downstream processes with projections.
    /// </summary>
    public bool isNew { get; }

    /// <summary>
    /// Null payload throws an exception by default, but can be overridden by setting this property to true.
    /// </summary>
    public bool allowNullPayload { get; }

    /// <summary>
    /// Base constructor.
    /// </summary>
    /// <param name="name">Human readable friendly name of event type. MUST BE UNIQUE - should follow convention "{Action}_{Entity Name}", ie - "Create_User". This will also become the name of the related Kafka topic.</param>
    /// <param name="isNew">Signifies if this event type results in the creation of a new aggregate.</param>
    /// <param name="allowNullPayload">Allows null payloads to be sent with this event type.</param>
    protected EventType(string name, bool isNew = false, bool allowNullPayload = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event type name cannot be null or empty", nameof(name));

        this.name = name;
        this.isNew = isNew;
        this.allowNullPayload = allowNullPayload;
    }

    /// <summary>
    /// Overrides default ToString to return Name property.
    /// </summary>
    public override string ToString() => name;

    /// <summary>
    /// Defines equality.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj == null || !typeof(EventType).IsAssignableFrom(obj.GetType()))
            return false;

        var otherValue = obj as EventType;
        return otherValue != null && name.Equals(otherValue.name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Overrides default hash code.
    /// </summary>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + name.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    /// Allows sorting by name.
    /// </summary>
    public int CompareTo(object other) => name.CompareTo(((EventType)other).name);

    /// <summary>
    /// Tests if EventType equals another EventType.
    /// </summary>
    public static bool operator ==(EventType? a, EventType? b)
    {
        if (a is null) return b is null;
        return a.Equals(b);
    }

    /// <summary>
    /// Tests if EventType does not equal another EventType.
    /// </summary>
    public static bool operator !=(EventType? a, EventType? b)
    {
        if (a is null) return b is not null;
        return !a.Equals(b);
    }
}
