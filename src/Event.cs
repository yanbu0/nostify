
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json.Linq;
using Confluent.Kafka;
using Newtonsoft.Json;

namespace nostify;

/// <summary>
/// Represents events in event store
/// </summary>
public class Event
{
    /// <summary>
    /// Constructor for Event, use when creating object to save to event store
    /// </summary>
    /// <param name="command">Command to persist</param>
    /// <param name="aggregateRootId">Id of the root aggregate to perform the command on.</param>
    /// <param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    /// <param name="userId">ID of User responsible for Event.</param>
    /// <param name="partitionKey">ID of partition that the Aggregate to apply Event to is in.</param>
    public Event(NostifyCommand command, Guid aggregateRootId, object payload, Guid userId = default, Guid partitionKey = default)
    {
        SetUp(command, aggregateRootId, payload, userId, partitionKey);
    }

    /// <summary>
    /// Constructor for Event, use when creating object to save to event store, will parse aggregateRootId from payload
    /// </summary>
    /// <param name="command">Command to persist</param>
    /// <param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    /// <param name="userId">ID of User responsible for Event.</param>
    /// <param name="partitionKey">ID of partition that the Aggregate to apply Event to is in</param>
    public Event(NostifyCommand command, object payload, Guid userId = default, Guid partitionKey = default)
    {
        Guid aggregateRootId = default;
        //Check payload is not null
        if (payload is null || !payload.GetType().GetProperties().Any())
        {
            throw new ArgumentNullException("Payload cannot be null if you do not specify an aggregate root ID");
        }
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

    /// <summary>
    /// Constructor for Event, use when creating object to save to event store, parses Id values to Guids, recommend using Guids instead of strings instead of this constructor
    /// </summary>
    /// <param name="command">Command to persist</param>
    /// <param name="aggregateRootId">Id of the root aggregate to perform the command on.  Must be a Guid string</param>
    /// <param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    /// <param name="userId">ID of User responsible for Event.</param>
    /// <param name="partitionKey">ID of partition that the Aggregate to apply Event to is in.</param>
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

    /// <summary>
    /// Empty constructor for Event, used when querying from db
    /// </summary>
    public Event() { }

    /// <summary>
    /// Timestamp of event
    /// </summary>
    public DateTime timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Partition key to apply event to
    /// </summary>
    public Guid partitionKey { get; set; }

    /// <summary>
    /// Id of user
    /// </summary>
    public Guid userId { get; set; }

    /// <summary>
    /// Id of event
    /// </summary>
    public Guid id { get; set; }

    /// <summary>
    /// Command to perform, defined in Aggregate implementation
    /// </summary>
    public NostifyCommand command { get; set; }  //This is an object because otherwise newtonsoft.json pukes creating an NostifyCommand

    /// <summary>
    /// Key of the Aggregate to perform the event on
    /// </summary>
    /// <para>
    /// <strong>The series of events for an Aggregate should have the same key.</strong>
    /// </para>
    public Guid aggregateRootId { get; set; }
    
    /// <summary>
    /// Internal use only
    /// </summary>
    protected int schemaVersion = 1; //Update to reflect schema changes in Persisted Event

    /// <summary>
    /// Object containing properties of Aggregate to perform the command on
    /// </summary>
    /// <para>
    /// Properties must be the exact same name to have updates applied.
    /// </para>
    /// <para>
    /// Delete command should contain solelly the id value of the Aggregate to delete.
    /// </para>
    public object payload { get; set; }

    /// <summary>
    /// Checks if the payload of this event has a property
    /// </summary>
    /// <param name="propertyName">Property to check for</param>
    public bool PayloadHasProperty(string propertyName)
    {
        return payload.GetType().GetProperty(propertyName) != null;
    }

    /// <summary>
    /// Returns typed value of payload
    /// </summary>
    public T GetPayload<T>()
    {
        return JObject.FromObject(payload).ToObject<T>() ?? throw new NullReferenceException($"Payload is null for type {typeof(T).Name}");
    }

    /// <summary>
    /// Validates if the payload contains all required properties for performing a command on an aggregate of type T.
    /// </summary>
    /// <param name="throwErrorIfExtraProps">If true, will throw a ValidationException if any properties on payload not existing on T are found.</param>
    /// <returns>Returns the event for chaining.</returns>
    /// <typeparam name="T">The type of the aggregate to validate against.</typeparam>
    public Event ValidatePayload<T>(bool throwErrorIfExtraProps = true) where T : NostifyObject, IAggregate
    {
        // Remove properties that do not exist on the Aggregate, 
        JObject cleanedPayload = RemoveNonExistentPayloadProperties<T>(throwErrorIfExtraProps, out List<ValidationResult> validationMessages) as JObject ?? throw new NullReferenceException("Payload cannot be null after removing non-existent properties.");

        // Covert to type
        var deserializedPayload = cleanedPayload.ToObject<T>() ?? throw new NullReferenceException("Payload cannot be null after deserialization.") ;

        // Create a new validation context and add the command
        ValidationContext validationContext = new ValidationContext(deserializedPayload, new Dictionary<object, object?> { { "command", command } });
        Validator.TryValidateObject(deserializedPayload, validationContext, validationMessages, true);

        // If the property exists on T but does not exist in payload, remove the validation message unless
        // it has an attribute that inheirits from RequiredAttribute
        validationMessages.RemoveAll(vm =>
        {
            int i = 0;
            vm.MemberNames.ToList().ForEach(memberName =>
            {
                var property = typeof(T).GetProperty(memberName);
                if (property != null && !cleanedPayload.ContainsKey(memberName))
                {
                    // If the property does not exist in payload, check if it has a RequiredAttribute
                    // If it does not have a RequiredAttribute, remove the validation message
                    // Otherwise, keep the validation message
                    var requiredAttributes = property.GetCustomAttributes(typeof(RequiredAttribute), false);
                    if (requiredAttributes.Length == 0)
                    {
                        // If no RequiredAttribute, update the count for removal
                        i++;
                    }
                }
            });
            if (i > 0 &&i == vm.MemberNames.Count())
            {
                // If all member names were removed, return true to remove the validation message
                return true;
            }
            return false; // Keep the validation message if it has a RequiredAttribute
        });

        // If there are any validation messages left, throw a ValidationException
        if (validationMessages.Any())
        {
            throw new ValidationException($"Payload validation failed. {validationMessages.Select(vm => vm.ErrorMessage).Aggregate((current, next) => $"{current} {next}")}");
        }

        return this;
    }

    /// <summary>
    /// Removes any properties from the payload that are not valid for the aggregate
    /// </summary>
    /// <param name="errorMessageIfFound">If true, will add an error to output if any non-existent properties are found.</param>
    /// <param name="validationMessages">List of validation messages to populate with any errors found.</param>
    private object RemoveNonExistentPayloadProperties<T>(bool errorMessageIfFound, out List<ValidationResult> validationMessages) where T : NostifyObject, IAggregate
    {
        validationMessages = new List<ValidationResult>();

        var validProperties = typeof(T).GetProperties().Select(p => p.Name).ToHashSet();
        var payloadObject = JObject.FromObject(payload) ?? throw new NullReferenceException("Payload cannot be null when removing non-existent properties.");
        // Remove any properties from the payload that are not valid for the aggregate
        foreach (var prop in payloadObject.Properties().Select(p => p.Name).ToList())
        {
            if (!validProperties.Contains(prop))
            {
                if (errorMessageIfFound)
                {
                    validationMessages.Add(new ValidationResult($"Invalid property '{prop}' found in payload."));
                }
                payloadObject.Remove(prop);
            }
        }
        
        return payloadObject.ToObject<object>() ?? throw new NullReferenceException("Payload cannot be null after removing non-existent properties.");

    }

    
}
