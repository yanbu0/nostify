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
    public Event? GetEvent()
    {
        return JsonConvert.DeserializeObject<Event>(Value);
    }

    ///<summary>
    ///Converts string value of the Value to an IEvent.
    ///</summary>
    /// <returns>The deserialized IEvent object from the Kafka message Value.</returns>
    /// <exception cref="InvalidOperationException">Thrown if deserialization fails.</exception>
    public IEvent GetIEvent()
    {
        return JsonConvert.DeserializeObject<IEvent>(Value, SerializationSettings.NostifyDefault) ?? throw new InvalidOperationException("Failed to deserialize Event from Kafka message Value.");
    }
}