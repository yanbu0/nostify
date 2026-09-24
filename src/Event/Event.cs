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
#pragma warning disable CS0618 // Stored solely to preserve the published legacy command JSON/API contract.
    private NostifyCommand? _legacyCommand;
#pragma warning restore CS0618
    private int? _schemaVersion;

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
    /// Constructor for Event, use when creating object to save to event store with legacy command metadata.
    /// </summary>
    [Obsolete("Use Event(EventType, ...) instead.")]
    public Event(NostifyCommand command, Guid aggregateRootId, object payload, Guid userId = default, Guid partitionKey = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        SetUp(CreateLegacyEventType(command), aggregateRootId, payload, userId, partitionKey);
        _legacyCommand = command;
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
        Guid aggregateRootId = GetRequiredAggregateRootId(payload);
        SetUp(eventType, aggregateRootId, payload, userId, partitionKey);
    }

    /// <summary>
    /// Constructor for Event, use when creating object to save to event store, will parse aggregateRootId from payload.
    /// </summary>
    [Obsolete("Use Event(EventType, ...) instead.")]
    public Event(NostifyCommand command, object payload, Guid userId = default, Guid partitionKey = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid aggregateRootId = GetRequiredAggregateRootId(payload);
        SetUp(CreateLegacyEventType(command), aggregateRootId, payload, userId, partitionKey);
        _legacyCommand = command;
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

    /// <summary>
    /// Constructor for Event, use when creating object to save to event store, parses Id values to Guids.
    /// </summary>
    [Obsolete("Use Event(EventType, ...) instead.")]
    public Event(NostifyCommand command, string aggregateRootId, object payload, string userId, string partitionKey)
        : this(CreateLegacyEventType(command), aggregateRootId, payload, userId, partitionKey)
    {
        _legacyCommand = command;
    }

#pragma warning disable CS0618 // This helper is the compatibility boundary for the obsolete command API.
    private static LegacyNostifyCommandEventType CreateLegacyEventType(NostifyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return new LegacyNostifyCommandEventType(command.name, command.isNew, command.allowNullPayload);
    }
#pragma warning restore CS0618

    private static Guid GetRequiredAggregateRootId(object payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload), "Event Create Error: Payload cannot be null if you do not specify an aggregate root ID");
        }

        if (payload.GetType().GetProperties().Length == 0)
        {
            throw new ArgumentNullException(nameof(payload), "Event Create Error: Payload cannot be empty if you do not specify an aggregate root ID");
        }

        JToken? idToken = JObject.FromObject(payload)[nameof(IEvent.id)];
        if (idToken is null || !Guid.TryParse(idToken.ToString(), out Guid aggregateRootId))
        {
            throw new ArgumentException("Event Create Error: Aggregate Root ID does not exist or is not parsable to a Guid", nameof(payload));
        }

        return aggregateRootId;
    }

    private void SetUp(EventType eventType, Guid aggregateRootId, object payload, Guid userId, Guid partitionKey)
    {
        ArgumentNullException.ThrowIfNull(eventType);
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
        get => _eventType!;
        set
        {
            _eventType = value;
#pragma warning disable CS0618 // Hydrates the legacy command view for schema-version-1 documents.
            _legacyCommand = value is LegacyNostifyCommandEventType
                ? new NostifyCommand(value.name, value.isNew, value.allowNullPayload)
                : null;
#pragma warning restore CS0618
        }
    }

    /// <inheritdoc />
    [Obsolete("Use eventType instead.")]
    public NostifyCommand command
    {
        get
        {
            if (_legacyCommand != null)
            {
                return _legacyCommand;
            }

            if (_eventType == null)
            {
                return null!;
            }

            return _legacyCommand = new NostifyCommand(eventType.name, eventType.isNew, eventType.allowNullPayload);
        }
        set
        {
            _legacyCommand = value;
            if (value == null)
            {
                if (_eventType is LegacyNostifyCommandEventType)
                {
                    _eventType = null;
                }
                return;
            }

            if (_eventType == null || _eventType is LegacyNostifyCommandEventType)
            {
                _eventType = new LegacyNostifyCommandEventType(value.name, value.isNew, value.allowNullPayload);
            }
        }
    }

    /// <inheritdoc />
    public Guid aggregateRootId { get; set; }

    /// <inheritdoc />
    public int schemaVersion
    {
        get
        {
#pragma warning disable CS0618
            if (_schemaVersion.HasValue)
            {
                return _schemaVersion.Value;
            }

            return _eventType is not null && _eventType is not LegacyNostifyCommandEventType ? 2 : 1;
#pragma warning restore CS0618
        }
        init => _schemaVersion = value;
    }

    /// <inheritdoc />
    public object? payload { get; set; }

    /// <inheritdoc />
    public bool PayloadHasProperty(string propertyName)
    {
        return payload?.GetType().GetProperty(propertyName) != null;
    }

    /// <inheritdoc />
    public T GetPayload<T>()
    {
        object requiredPayload = payload ?? throw new InvalidOperationException($"Payload is null for type {typeof(T).Name}");
        return JObject.FromObject(requiredPayload).ToObject<T>() ?? throw new InvalidOperationException($"Payload cannot be converted to type {typeof(T).Name}");
    }

    /// <inheritdoc />
    public IEvent ValidatePayload<T>(bool throwErrorIfExtraProps = true) where T : class
    {
        JObject cleanedPayload = RemoveNonExistentPayloadProperties<T>(throwErrorIfExtraProps, out List<ValidationResult> validationMessages) as JObject ?? throw new InvalidOperationException("Payload cannot be null after removing non-existent properties.");
        var deserializedPayload = cleanedPayload.ToObject<T>() ?? throw new InvalidOperationException("Payload cannot be null after deserialization.");

        ValidationContext validationContext = new ValidationContext(deserializedPayload);
        validationContext.Items["eventType"] = eventType;
#pragma warning disable CS0618
        if (_legacyCommand != null)
        {
            validationContext.Items["command"] = _legacyCommand;
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

        if (validationMessages.Count != 0)
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
        object requiredPayload = payload ?? throw new InvalidOperationException("Payload cannot be null when removing non-existent properties.");
        var payloadObject = JObject.FromObject(requiredPayload);
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

        return payloadObject.ToObject<object>() ?? throw new InvalidOperationException("Payload cannot be null after removing non-existent properties.");

    }
}
