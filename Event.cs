
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.ComponentModel.DataAnnotations;

namespace nostify;

///<summary>
///Represents events in event store
///</summary>
public class Event
{

    ///<summary>
    ///Constructor for PeristedEvent, use when creating object to save to event store
    ///</summary>
    ///<param name="command">Command to persist</param>
    ///<param name="aggregateRootId">Id of the root aggregate to perform the command on.  Must be a Guid string</param>
    ///<param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    ///<param name="partitionKey">Partition key to apply Event to.</param>
    public Event(NostifyCommand command, string aggregateRootId, object payload, string partitionKey)
    {
        if (!Guid.TryParse(aggregateRootId, out _)){
            throw new ArgumentException("String is not parsable to a Guid");
        }

        SetUp(command,aggregateRootId,payload, partitionKey);
    }

    ///<summary>
    ///Constructor for PeristedEvent, use when creating object to save to event store
    ///</summary>
    ///<param name="command">Command to persist</param>
    ///<param name="aggregateRootId">Id of the root aggregate to perform the command on.</param>
    ///<param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    ///<param name="partitionKey">Tenant ID to apply Event to.</param>
    public Event(NostifyCommand command, Guid aggregateRootId, object payload, Guid partitionKey = default)
    {
        SetUp(command,aggregateRootId.ToString(),payload, partitionKey.ToString());
    }
    
    private void SetUp(NostifyCommand command, string aggregateRootId, object payload, string partitionKey)
    {
        this.aggregateRootId = aggregateRootId;
        this.id = Guid.NewGuid();
        this.command = command;
        this.timestamp = DateTime.UtcNow;
        this.payload = payload;
        this.partitionKey = partitionKey;
    }

    ///<summary>
    ///Empty constructor for PeristedEvent, used when querying from db
    ///</summary>
    public Event() { }

    ///<summary>
    ///Timestamp of event
    ///</summary>
    public DateTime timestamp { get; set; }

    ///<summary>
    ///Partition key to apply event to
    ///</summary>
    public string partitionKey { get; set; }


    ///<summary>
    ///Id of event
    ///</summary>
    public Guid id { get; set; }

    ///<summary>
    ///Command to perform, defined in Aggregate implementation
    ///</summary>
    public NostifyCommand command { get; set; }  //This is an object because otherwise newtonsoft.json pukes creating an NostifyCommand

    ///<summary>
    ///Key of the Aggregate to perform the event on
    ///</summary>
    ///<para>
    ///<strong>The series of events for an Aggregate should have the same key.</strong>
    ///</para>
    public string aggregateRootId { get; set; }
    
    ///<summary>
    ///Internal use only
    ///</summary>
    protected int schemaVersion = 1; //Update to reflect schema changes in Persisted Event

    ///<summary>
    ///Object containing properties of Aggregate to perform the command on
    ///</summary>
    ///<para>
    ///Properties must be the exact same name to have updates applied.
    ///</para>
    ///<para>
    ///Delete command should contain solelly the id value of the Aggregate to delete.
    ///</para>
    public object payload { get; set; }

    ///<summary>
    ///Checks if the payload of this event has a property
    ///</summary>
    ///<param name="propertyName">Property to check for</param>
    public bool PayloadHasProperty(string propertyName)
    {
        return payload.GetType().GetProperty(propertyName) != null;
    }

    
}
