using System;
using System.Collections;
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
        JObject cleanedPayload = RemoveNonExistentPayloadProperties<T>(
            throwErrorIfExtraProps,
            out List<ValidationResult> validationMessages,
            out HashSet<string> ignoredClientProperties);
        var deserializedPayload = cleanedPayload.ToObject<T>() ?? throw new InvalidOperationException("Payload cannot be null after deserialization.");

        var validationItems = new Dictionary<object, object?>
        {
            ["eventType"] = eventType,
            // Identity validators compare payload identifiers against the immutable event target.
            ["aggregateRootId"] = aggregateRootId
        };
#pragma warning disable CS0618
        if (_legacyCommand != null)
        {
            validationItems["command"] = _legacyCommand;
        }
#pragma warning restore CS0618

        // Validate every reachable payload model and collection element. The active-object set
        // prevents reference cycles while still validating shared objects at each distinct path.
        ValidateObjectGraph(
            deserializedPayload,
            cleanedPayload,
            string.Empty,
            validationItems,
            validationMessages,
            new HashSet<object>(ReferenceEqualityComparer.Instance));

        validationMessages.RemoveAll(vm =>
            // Silently rejected top-level server-managed properties are excluded from validation.
            vm.MemberNames.Any() && vm.MemberNames.All(ignoredClientProperties.Contains));

        if (validationMessages.Count != 0)
        {
            throw new NostifyValidationException(validationMessages);
        }

        return this;
    }

    /// <summary>
    /// Recursively validates an object graph and qualifies validation member names with their payload paths.
    /// </summary>
    /// <param name="instance">The object or collection at the current graph node.</param>
    /// <param name="payloadToken">The JSON token corresponding to <paramref name="instance"/>.</param>
    /// <param name="path">The path from the payload root to the current node.</param>
    /// <param name="validationItems">Event-scoped values exposed to validation attributes.</param>
    /// <param name="validationMessages">The collection that receives validation failures.</param>
    /// <param name="activeObjects">Reference objects currently being traversed, used to break cycles.</param>
    private static void ValidateObjectGraph(
        object? instance,
        JToken? payloadToken,
        string path,
        IReadOnlyDictionary<object, object?> validationItems,
        List<ValidationResult> validationMessages,
        HashSet<object> activeObjects)
    {
        if (instance is null || IsSimpleType(instance.GetType()))
        {
            return;
        }

        bool tracksReference = !instance.GetType().IsValueType;
        if (tracksReference && !activeObjects.Add(instance))
        {
            return;
        }

        try
        {
            if (instance is IEnumerable enumerable)
            {
                int index = 0;
                foreach (object? item in enumerable)
                {
                    JToken? itemToken = payloadToken is JArray payloadArray && index < payloadArray.Count
                        ? payloadArray[index]
                        : null;
                    ValidateObjectGraph(
                        item,
                        itemToken,
                        $"{path}[{index}]",
                        validationItems,
                        validationMessages,
                        activeObjects);
                    index++;
                }

                return;
            }

            var suppliedProperties = payloadToken is JObject payloadObject
                ? payloadObject.Properties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var context = new ValidationContext(instance);
            foreach ((object key, object? value) in validationItems)
            {
                context.Items[key] = value;
            }

            // Event-aware validators must distinguish omitted properties from null/default values
            // independently at each nesting level.
            context.Items["payloadProperties"] = suppliedProperties;

            var currentResults = new List<ValidationResult>();
            Validator.TryValidateObject(instance, context, currentResults, validateAllProperties: true);
            foreach (ValidationResult result in currentResults)
            {
                List<string> memberNames = result.MemberNames.ToList();
                bool suppressOmittedOptionalResult = memberNames.Count > 0 && memberNames.All(memberName =>
                {
                    PropertyInfo? property = instance.GetType().GetProperty(memberName);
                    return property != null &&
                        !suppliedProperties.Contains(memberName) &&
                        property.GetCustomAttributes(typeof(RequiredAttribute), inherit: false).Length == 0;
                });
                if (suppressOmittedOptionalResult)
                {
                    continue;
                }

                IEnumerable<string> qualifiedMembers = memberNames.Count == 0
                    ? (string.IsNullOrEmpty(path) ? [] : [path])
                    : memberNames.Select(memberName => QualifyMemberPath(path, memberName));
                validationMessages.Add(new ValidationResult(result.ErrorMessage, qualifiedMembers));
            }

            foreach (PropertyInfo property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // An omitted nested object is not part of a partial-update payload, even when its
                // model property has a non-null default initializer.
                if (!property.CanRead ||
                    property.GetIndexParameters().Length != 0 ||
                    !suppliedProperties.Contains(property.Name))
                {
                    continue;
                }

                object? child = property.GetValue(instance);
                if (child is null || IsSimpleType(child.GetType()))
                {
                    continue;
                }

                JToken? childToken = payloadToken is JObject currentPayload
                    ? currentPayload.GetValue(property.Name, StringComparison.Ordinal)
                    : null;
                ValidateObjectGraph(
                    child,
                    childToken,
                    QualifyMemberPath(path, property.Name),
                    validationItems,
                    validationMessages,
                    activeObjects);
            }
        }
        finally
        {
            if (tracksReference)
            {
                activeObjects.Remove(instance);
            }
        }
    }

    /// <summary>
    /// Determines whether a value is a leaf that cannot contain payload validation targets.
    /// </summary>
    private static bool IsSimpleType(Type type)
    {
        Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType.IsPrimitive ||
            effectiveType.IsEnum ||
            effectiveType == typeof(string) ||
            effectiveType == typeof(decimal) ||
            effectiveType == typeof(DateTime) ||
            effectiveType == typeof(DateTimeOffset) ||
            effectiveType == typeof(DateOnly) ||
            effectiveType == typeof(TimeOnly) ||
            effectiveType == typeof(TimeSpan) ||
            effectiveType == typeof(Guid) ||
            effectiveType == typeof(Uri);
    }

    /// <summary>
    /// Combines a parent payload path with a validation member name.
    /// </summary>
    private static string QualifyMemberPath(string path, string memberName)
        => string.IsNullOrEmpty(path) ? memberName : $"{path}.{memberName}";

    /// <summary>
    /// Removes payload properties that do not exist on the target or are configured to be silently rejected.
    /// </summary>
    /// <param name="errorMessageIfFound">If true, adds an error for properties that do not exist on the target.</param>
    /// <param name="validationMessages">The validation messages produced while inspecting payload properties.</param>
    /// <param name="ignoredClientProperties">The server-managed properties removed without producing validation errors.</param>
    /// <returns>A cleaned payload containing only client-writable target properties.</returns>
    private JObject RemoveNonExistentPayloadProperties<T>(
        bool errorMessageIfFound,
        out List<ValidationResult> validationMessages,
        out HashSet<string> ignoredClientProperties) where T : class
    {
        validationMessages = new List<ValidationResult>();

        Dictionary<string, PropertyInfo> validProperties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, property => property, StringComparer.Ordinal);
        ignoredClientProperties = validProperties.Values
            .Where(property => property.GetCustomAttribute<RejectFromClientAttribute>(inherit: false)?.throwsError == false)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        object requiredPayload = payload ?? throw new InvalidOperationException("Payload cannot be null when removing non-existent properties.");
        var payloadObject = JObject.FromObject(requiredPayload);
        bool removedRejectedProperty = false;

        foreach (string propertyName in payloadObject.Properties().Select(property => property.Name).ToList())
        {
            if (!validProperties.TryGetValue(propertyName, out PropertyInfo? property))
            {
                if (errorMessageIfFound)
                {
                    validationMessages.Add(new ValidationResult($"Invalid property '{propertyName}' found in payload."));
                }

                payloadObject.Remove(propertyName);
                continue;
            }

            RejectFromClientAttribute? rejectAttribute = property.GetCustomAttribute<RejectFromClientAttribute>(inherit: false);
            if (rejectAttribute is null)
            {
                continue;
            }

            if (rejectAttribute.throwsError)
            {
                validationMessages.Add(new ValidationResult(
                    $"The property '{propertyName}' is server-managed and cannot be supplied by a client.",
                    [propertyName]));
                continue;
            }

            payloadObject.Remove(propertyName);
            removedRejectedProperty = true;
            Console.Error.WriteLine($"[nostify:validation] Ignored server-managed client payload property '{propertyName}' for '{typeof(T).Name}'.");
        }

        if (removedRejectedProperty)
        {
            // Persist and apply only the cleaned payload so an ignored client value cannot update server state later.
            payload = payloadObject;
        }

        return payloadObject;
    }
}
