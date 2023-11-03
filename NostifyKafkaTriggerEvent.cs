
using System;
using Newtonsoft.Json;
using nostify;

///<summary>
///Type for helping to convert KafkaTrigger input values to PersistedEvent
///</summary>
public class NostifyKafkaTriggerEvent
{
    ///<summary>
    ///Constructor for class for deserialization
    ///</summary>
    public NostifyKafkaTriggerEvent()
    {

    }

    public int Offset { get; set; }
    public int Partition { get; set; }
    public string Topic { get; set; }
    public string Value { get; set; }
    public string Key { get; set; }
    public string[] Headers { get; set; }

    ///<summary>
    ///Converts string value of the Value to a PersistedEvent
    ///</summary>
    public PersistedEvent? GetPersistedEvent()
    {
        return JsonConvert.DeserializeObject<PersistedEvent>(Value);
    }
}