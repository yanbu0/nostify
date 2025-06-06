
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json.Linq;
using Confluent.Kafka;

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
    ///<param name="aggregateRootId">Id of the root aggregate to perform the command on.</param>
    ///<param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    ///<param name="userId">ID of User responsible for Event.</param>
    ///<param name="partitionKey">Tenant ID to apply Event to.</param>
    public Event(NostifyCommand command, Guid aggregateRootId, object payload, Guid userId = default, Guid partitionKey = default)
    {
        SetUp(command, aggregateRootId, payload, userId, partitionKey);
    }

    ///<summary>
    ///Constructor for PeristedEvent, use when creating object to save to event store, will parse aggregateRootId from payload
    ///</summary>
    ///<param name="command">Command to persist</param>
    ///<param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    ///<param name="userId">ID of User responsible for Event.</param>
    ///<param name="partitionKey">Tenant ID to apply Event to.</param>
    public Event(NostifyCommand command, object payload, Guid userId = default, Guid partitionKey = default)
    {
        Guid aggregateRootId = default;
        //Check payload is not null
        CheckPayload(payload);
        var jPayload = JObject.FromObject(payload);
        if (jPayload["id"] == null || (jPayload["id"].Type != JTokenType.Guid && !Guid.TryParse(jPayload["id"].Value<string>(), out aggregateRootId)))
        {
            throw new ArgumentException("Aggregate Root ID does not exist or is not parsable to a Guid");
        }
        //Only do this if we didn't parse out the guid value above
        else if (aggregateRootId == default)
        {
            aggregateRootId = jPayload["id"].Value<Guid>();
        }
        SetUp(command, aggregateRootId, payload, userId, partitionKey);
    }

    ///<summary>
    ///Constructor for PeristedEvent, use when creating object to save to event store, parses Id values to Guids, recommend using Guids instead of strings instead of this constructor
    ///</summary>
    ///<param name="command">Command to persist</param>
    ///<param name="aggregateRootId">Id of the root aggregate to perform the command on.  Must be a Guid string</param>
    ///<param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    ///<param name="userId">ID of User responsible for Event.</param>
    ///<param name="partitionKey">Partition key to apply Event to.</param>
    public Event(NostifyCommand command, string aggregateRootId, object payload, string userId, string partitionKey)
    {
        Guid aggGuid;
        if (!Guid.TryParse(aggregateRootId, out aggGuid)){
            throw new ArgumentException("Aggregate Root ID is not parsable to a Guid");
        }

        Guid userGuid;
        if (!Guid.TryParse(userId, out userGuid)){
            throw new ArgumentException("User ID is not parsable to a Guid");
        }

        Guid pKey;
        if (!Guid.TryParse(partitionKey, out pKey)){
            throw new ArgumentException("Partition Key is not parsable to a Guid");
        }

        SetUp(command, aggGuid, payload, userGuid, pKey);
    }    
    
    private void SetUp(NostifyCommand command, Guid aggregateRootId, object payload, Guid userId, Guid partitionKey)
    {
        if (command is null)
        {
            throw new ArgumentNullException("Command cannot be null");
        }
        CheckPayload(payload);
        
        this.aggregateRootId = aggregateRootId;
        this.id = Guid.NewGuid();
        this.command = command;
        this.timestamp = DateTime.UtcNow;
        this.payload = payload;
        this.partitionKey = partitionKey;
        this.userId = userId;
    }

    private void CheckPayload(object payload)
    {
        if (payload == null || !JObject.FromObject(payload).HasValues)
        {
            throw new ArgumentNullException("Payload cannot be null");
        }
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
    public Guid partitionKey { get; set; }

    ///<summary>
    ///Id of user
    ///</summary>
    public Guid userId { get; set; }

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
    public Guid aggregateRootId { get; set; }
    
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

    ///<summary>
    ///Returns typed value of payload
    ///</summary>
    public T GetPayload<T>()
    {
        return JObject.FromObject(payload).ToObject<T>() ?? throw new NullReferenceException($"Payload is null for type {typeof(T).Name}");
    }

    ///<summary>
    ///Validates if the payload contains all required properties for performing a command on an aggregate of type T. Will throw a ValidationException if any required properties are missing or null.
    ///</summary>
    /// <param name="validator">Validator to use for validating the payload. If null, no validation will be performed.</param>
    /// <param name="removeNonExistent">If true, will remove any properties from the payload that are not valid for the aggregate.</param>
    ///<returns>Returns the event for chaining.</returns>
    ///<typeparam name="T">The type of the aggregate to validate against.</typeparam>
    public Event ValidatePayload<T>(IAggregateValidator? validator = null, bool removeNonExistent = true) where T : NostifyObject, IAggregate
    {
        if (removeNonExistent)
        {
            RemoveNonExistentPayloadProperties<T>();
        }
        if (validator != null)
        {
            var validationErrors = validator.Validate<T>(GetPayload<T>());
            if (validationErrors.Count > 0)
            {
                // create a single validation message string of the format $"{property}: {message}" separated by newlines for each error
                var validationMessage = string.Join("\n", validationErrors.Select(e => $"  {e.Property}: {e.Message}"));
                throw new ValidationException($"Validation failed for {typeof(T).Name}:\n{validationMessage}");
            }
        }

        if (this.command.isNew)
        {
            ValidateForCreate<T>();
        }
        return this;
    }

    private void RemoveNonExistentPayloadProperties<T>()
    {
        var validProperties = typeof(T).GetProperties().Select(p => p.Name).ToHashSet();
        var payloadObject = JObject.FromObject(payload) ?? throw new NullReferenceException("Payload cannot be null when removing non-existent properties.");
        // Remove any properties from the payload that are not valid for the aggregate
        foreach (var prop in payloadObject.Properties().Select(p => p.Name).ToList())
        {
            if (!validProperties.Contains(prop))
            {
                payloadObject.Remove(prop);
            }
        }
        // Reassign the modified JObject back to the payload
        payload = payloadObject.ToObject(payload.GetType()) ?? throw new NullReferenceException("Payload cannot be null after removing non-existent properties.");
    }

    private void ValidateForCreate<T>()
    {
        //Get all properties with RequiredForCreate attribute on T
        var requiredProps = typeof(T).GetProperties().Where(p => p.GetCustomAttributes(typeof(RequiredForCreate), false).Any()).ToList();
        //Get all properties with RequiredForCreate attribute on T where NotEmptyGuid is true
        var notEmptyGuidProps = requiredProps.Where(p => p.GetCustomAttributes(typeof(RequiredForCreate), false).Any(a => ((RequiredForCreate)a).NotEmptyGuid)).ToList();
        //Get all properties in payload where the name and type match a property in requiredProps
        var payloadProps = JObject.FromObject(payload).Properties()
                            .Where(p => requiredProps.Any(rp => rp.Name == p.Name)).ToList();

        //Add error for each missing property
        string missingProps = string.Empty;
        foreach (var prop in requiredProps)
        {
            if (!payloadProps.Any(p => p.Name == prop.Name))
            {
                missingProps += prop.Name + ", ";
            }
        }
        //Add error for any null properties
        string nullProps = string.Empty;
        foreach (var prop in payloadProps)
        {
            if (prop.Value.Type == JTokenType.Null)
            {
                nullProps += prop.Name + ", ";
            }
        }
        //Add error for any empty guid properties
        string emptyGuidProps = string.Empty;
        foreach (var prop in notEmptyGuidProps)
        {
            if (payloadProps.Any(p => p.Name == prop.Name && Guid.TryParse(p.Value.ToString(), out Guid guidValue) && guidValue == Guid.Empty))
            {
                emptyGuidProps += prop.Name + ", ";
            }
        }
        //Throw exception if any missing or null properties
        string message = string.Empty;
        if (!string.IsNullOrEmpty(missingProps))
        {
            message += $"Missing properties: {missingProps} ";
        }
        if (!string.IsNullOrEmpty(nullProps))
        {
            message += $"Null properties: {nullProps} ";
        }
        if (!string.IsNullOrEmpty(emptyGuidProps))
        {
            message += $"Empty Guid properties: {emptyGuidProps}";
        }
        if (!string.IsNullOrEmpty(message))
        {
            throw new ValidationException(message);
        }
    }
    
}
