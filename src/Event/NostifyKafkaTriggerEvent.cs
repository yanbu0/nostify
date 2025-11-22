using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using nostify;

///<summary>
///Type for helping to convert KafkaTrigger input values to Event
///</summary>
public class NostifyKafkaTriggerEvent
{
    ///<summary>
    ///Constructor for class for deserialization
    ///</summary>
    public NostifyKafkaTriggerEvent()
    {

    }

    ///<summary>
    ///Gets or sets the offset of the Kafka message.
    ///</summary>
    public int Offset { get; set; }

    ///<summary>
    ///Gets or sets the partition of the Kafka message.
    ///</summary>
    public int Partition { get; set; }

    ///<summary>
    ///Gets or sets the topic of the Kafka message.
    ///</summary>
    public string Topic { get; set; }

    ///<summary>
    ///Gets or sets the value of the Kafka message.
    ///</summary>
    public string Value { get; set; }

    ///<summary>
    ///Gets or sets the key of the Kafka message.
    ///</summary>
    public string Key { get; set; }

    ///<summary>
    ///Gets or sets the headers of the Kafka message.
    ///</summary>
    public string[] Headers { get; set; }

    ///<summary>
    ///Converts the Kafka message Value to an Event applying an optional single event type filter.
    ///</summary>
    ///<param name="eventTypeFilter">Optional event type name; if provided and it does not match the event's command name, null is returned.</param>
    ///<returns>The deserialized Event or null if the filter does not match.</returns>
    public Event? GetEvent(string? eventTypeFilter = null)
    {
        return GetEvent(eventTypeFilter is null ? new List<string>() : new List<string> { eventTypeFilter });
    }

    ///<summary>
    ///Converts string value of the Value to an Event.
    ///</summary>
    /// <param name="eventTypeFilters">Optional filter to only return the event if it matches one of the specified event types.</param>
    /// <returns>The deserialized Event object from the Kafka message Value.</returns>
    public Event? GetEvent(IEnumerable<string> eventTypeFilters)
    {
        Event? evt = JsonConvert.DeserializeObject<Event>(Value, SerializationSettings.NostifyDefault);
        if (evt != null && eventTypeFilters.Count() > 0 && !eventTypeFilters.Contains(evt.command.name))
        {
            evt = null;
        }
        return evt;
    }

    ///<summary>
    ///Converts string value of the Value to an IEvent using a single event type filter.
    ///</summary>
    ///<param name="eventTypeFilter">Optional event type name used to filter the returned event.</param>
    ///<returns>The deserialized Event object from the Kafka message Value or null if the filter does not match.</returns>
    public IEvent? GetIEvent(string? eventTypeFilter = null)
    {
        return GetIEvent(eventTypeFilter is null ? new List<string>() : new List<string> { eventTypeFilter });
    }

    ///<summary>
    ///Converts string value of the Value to an IEvent.
    ///</summary>
    /// <param name="eventTypeFilters">Optional filter to only return the event if it matches one of the specified event types.</param>
    /// <returns>The deserialized Event object from the Kafka message Value.</returns>
    public IEvent? GetIEvent(IEnumerable<string> eventTypeFilters)
    {
        IEvent? evt = JsonConvert.DeserializeObject<IEvent>(Value, SerializationSettings.NostifyDefault);
        if (evt != null && eventTypeFilters.Count() > 0 && !eventTypeFilters.Contains(evt.command.name))
        {
            evt = null;
        }
        return evt;
    }
}