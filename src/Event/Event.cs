using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json.Linq;
using Confluent.Kafka;
using Newtonsoft.Json;
using System.Reflection;
using NJson = Newtonsoft.Json;
using STJ = System.Text.Json.Serialization;

namespace nostify;

/// <inheritdoc />
public class Event : IEvent
{
    private EventType? _eventType;

    /// <summary>
    /// Constructor for Event, use when creating object to save to event store.
    /// </summary>
    /// <param name="eventType">Event type to persist.</param>
    /// <param name="aggregateRootId">Id of the root aggregate to perform the event on.</param>
    /// <param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    /// <param name="userId">ID of User responsible for Event.</param>
    /// <param name="partitionKey">ID of partition that the Aggregate to apply Event to is in.</param>
    public Event(EventType eventType, Guid aggregateRootId, object payload, Guid userId = default, Guid partitionKey = default)
    {
        SetUp(eventType, aggregateRootId, payload, userId, partitionKey);
    }

    /// <summary>
    /// Constructor for Event, use when creating object to save to event store, will parse aggregateRootId from payload.
    /// </summary>
    /// <param name="eventType">Event type to persist.</param>
    /// <param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    /// <param name="userId">ID of User responsible for Event.</param>
    /// <param name="partitionKey">ID of partition that the Aggregate to apply Event to is in.</param>
    public Event(EventType eventType, object payload, Guid userId = default, Guid partitionKey = default)
    {
        Guid aggregateRootId = default;
        if (payload is null || !payload.GetType().GetProperties().Any())
        {
            throw new ArgumentNullException("Event Create Error: Payload cannot be null if you do not specify an aggregate root ID");
        }
        var jPayload = JObject.FromObject(payload);
        if (jPayload["id"] == null || (jPayload["id"].Type != JTokenType.Guid && !Guid.TryParse(jPayload["id"].Value<string>(), out aggregateRootId)))
        {
            throw new ArgumentException("Event Create Errpr: Aggregate Root ID does not exist or is not parsable to a Guid");
        }
        else if (aggregateRootId == default)
        {
            aggregateRootId = jPayload["id"].Value<Guid>();
        }
        SetUp(eventType, aggregateRootId, payload, userId, partitionKey);
    }

    /// <summary>
    /// Constructor for Event, use when creating object to save to event store, parses Id values to Guids.
    /// </summary>
    /// <param name="eventType">Event type to persist.</param>
    /// <param name="aggregateRootId">Id of the root aggregate to perform the event on. Must be a Guid string.</param>
    /// <param name="payload">Properties to update or the id of the Aggregate to delete.</param>
    /// <param name="userId">ID of User responsible for Event.</param>
    /// <param name="partitionKey">ID of partition that the Aggregate to apply Event to is in.</param>
    public Event(EventType eventType, string aggregateRootId, object payload, string userId, string partitionKey)
    {
        Guid aggGuid;
        if (!Guid.TryParse(aggregateRootId, out aggGuid))
        {
            throw new ArgumentException("Aggregate Root ID is not parsable to a Guid");
        }

        Guid userGuid;
        if (!Guid.TryParse(userId, out userGuid))
        {
            throw new ArgumentException("User ID is not parsable to a Guid");
        }

        Guid pKey;
        if (!Guid.TryParse(partitionKey, out pKey))
        {
            throw new ArgumentException("Partition Key is not parsable to a Guid");
        }

        SetUp(eventType, aggGuid, payload, userGuid, pKey);
    }

    private void SetUp(EventType eventType, Guid aggregateRootId, object payload, Guid userId, Guid partitionKey)
    {
        if (eventType is null)
        {
            throw new ArgumentNullException("Event type cannot be null");
        }
        this.aggregateRootId = aggregateRootId;
        this.id = Guid.NewGuid();
        this.eventType = eventType;
        this.timestamp = DateTime.UtcNow;
        this.payload = payload;
        this.partitionKey = partitionKey;
        this.userId = userId;
    }

    /// <summary>
    /// Empty constructor for Event, used when querying from db.
    /// </summary>
    public Event() { }

    /// <inheritdoc />
    public DateTime timestamp { get; set; } = DateTime.UtcNow;

    /// <inheritdoc />
    public Guid partitionKey { get; set; }

    /// <inheritdoc />
    public Guid userId { get; set; }

    /// <inheritdoc />
    public Guid id { get; set; }

    /// <inheritdoc />
    [NJson.JsonConverter(typeof(NewtonsoftEventTypeJsonConverter))]
    [STJ.JsonConverter(typeof(SystemTextEventTypeJsonConverter))]
    public EventType eventType
    {
        get => _eventType ?? throw new NullReferenceException("Event type cannot be null");
        set => _eventType = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    [Obsolete("Use eventType instead.")]
    public NostifyCommand command
    {
        get => _eventType as NostifyCommand ?? new NostifyCommand(eventType.name, eventType.isNew, eventType.allowNullPayload);
        set => eventType = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public Guid aggregateRootId { get; set; }

    /// <inheritdoc />
    public int schemaVersion { get; init; } = 1;

    /// <inheritdoc />
    public object payload { get; set; }

    /// <inheritdoc />
    public bool PayloadHasProperty(string propertyName)
    {
        return payload.GetType().GetProperty(propertyName) != null;
    }

    /// <inheritdoc />
    public T GetPayload<T>()
    {
        return JObject.FromObject(payload).ToObject<T>() ?? throw new NullReferenceException($"Payload is null for type {typeof(T).Name}");
    }

    /// <inheritdoc />
    public IEvent ValidatePayload<T>(bool throwErrorIfExtraProps = true) where T : class
    {
        JObject cleanedPayload = RemoveNonExistentPayloadProperties<T>(throwErrorIfExtraProps, out List<ValidationResult> validationMessages) as JObject ?? throw new NullReferenceException("Payload cannot be null after removing non-existent properties.");
        var deserializedPayload = cleanedPayload.ToObject<T>() ?? throw new NullReferenceException("Payload cannot be null after deserialization.");

        ValidationContext validationContext = new ValidationContext(deserializedPayload);
        validationContext.Items["eventType"] = eventType;
#pragma warning disable CS0618
        if (eventType is NostifyCommand legacyCommand)
        {
            validationContext.Items["command"] = legacyCommand;
        }
#pragma warning restore CS0618
        Validator.TryValidateObject(deserializedPayload, validationContext, validationMessages, true);

        validationMessages.RemoveAll(vm =>
        {
            int i = 0;
            vm.MemberNames.ToList().ForEach(memberName =>
            {
                var property = typeof(T).GetProperty(memberName);
                if (property != null && !cleanedPayload.ContainsKey(memberName))
                {
                    var requiredAttributes = property.GetCustomAttributes(typeof(RequiredAttribute), false);
                    if (requiredAttributes.Length == 0)
                    {
                        i++;
                    }
                }
            });
            if (i > 0 && i == vm.MemberNames.Count())
            {
                return true;
            }
            return false;
        });

        if (validationMessages.Any())
        {
            throw new NostifyValidationException(validationMessages);
        }

        return this;
    }

    /// <summary>
    /// Removes any properties from the payload that are not valid for the aggregate.
    /// </summary>
    /// <param name="errorMessageIfFound">If true, will add an error to output if any non-existent properties are found.</param>
    /// <param name="validationMessages">List of validation messages to populate with any errors found.</param>
    private object RemoveNonExistentPayloadProperties<T>(bool errorMessageIfFound, out List<ValidationResult> validationMessages) where T : class
    {
        validationMessages = new List<ValidationResult>();

        var validProperties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
        var payloadObject = JObject.FromObject(payload) ?? throw new NullReferenceException("Payload cannot be null when removing non-existent properties.");
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
