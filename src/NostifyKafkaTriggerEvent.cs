using System;
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
    ///Converts string value of the Value to an Event.
    ///</summary>
    /// <param name="eventTypeFilter">Optional filter to only return the event if it matches the specified event type.</param>
    /// <returns>The deserialized Event object from the Kafka message Value.</returns>
    public Event? GetEvent(string? eventTypeFilter)
    {
        Event? evt = JsonConvert.DeserializeObject<Event>(Value, SerializationSettings.NostifyDefault);
        if (evt != null && (string.IsNullOrEmpty(eventTypeFilter) || evt.command.name != eventTypeFilter))
        {
            evt = null;
        }
        return evt;
    }

    ///<summary>
    ///Converts string value of the Value to an IEvent.
    ///</summary>
    /// <param name="eventTypeFilter">Optional filter to only return the event if it matches the specified event type.</param>
    /// <returns>The deserialized IEvent object from the Kafka message Value.</returns>
    public IEvent? GetIEvent(string? eventTypeFilter)
    {
        IEvent? evt = JsonConvert.DeserializeObject<IEvent>(Value, SerializationSettings.NostifyDefault);
        if (evt != null && (string.IsNullOrEmpty(eventTypeFilter) || evt.command.name != eventTypeFilter))
        {
            evt = null;
        }
        return evt;
    }
}