using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using STJ = System.Text.Json;
using STJS = System.Text.Json.Serialization;

namespace nostify;

internal static class EventTypeResolver
{
    private static readonly Type MissingEventTypeSentinel = typeof(void);
    private static readonly ConcurrentDictionary<string, Type> _eventTypeByNameCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a logical event name to its unique loaded concrete definition.
    /// </summary>
    internal static Type Resolve(string? eventTypeName)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            return typeof(LegacyNostifyCommandEventType);
        }

        if (_eventTypeByNameCache.TryGetValue(eventTypeName, out var cachedType))
        {
            return cachedType == MissingEventTypeSentinel
                ? typeof(LegacyNostifyCommandEventType)
                : cachedType;
        }

        var matchingTypes = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(t => !t.IsAbstract
                && typeof(EventType).IsAssignableFrom(t)
                && !IsLegacyCompatibilityType(t))
            .Select(t => new { Type = t, EventType = TryGetRequiredInstance(t) })
            .Where(x => x.EventType != null
                && string.Equals(x.EventType.name, eventTypeName, StringComparison.Ordinal))
            .Select(x => x.Type)
            .OrderBy(t => t.AssemblyQualifiedName, StringComparer.Ordinal)
            .ToArray();

        if (matchingTypes.Length > 1)
        {
            var conflicts = string.Join(", ", matchingTypes.Select(t => $"'{t.AssemblyQualifiedName}'"));
            throw new InvalidOperationException(
                $"Multiple concrete EventType definitions declare the logical name '{eventTypeName}': {conflicts}. " +
                "EventType names must be unique using ordinal, case-sensitive comparison.");
        }

        var resolvedType = matchingTypes.SingleOrDefault();
        _eventTypeByNameCache[eventTypeName] = resolvedType ?? MissingEventTypeSentinel;
        return resolvedType ?? typeof(LegacyNostifyCommandEventType);
    }

    internal static EventType CreateInstance(Type resolvedType, string? name, bool isNew, bool allowNullPayload)
    {
        if (!IsLegacyCompatibilityType(resolvedType))
        {
            // A known definition is authoritative for all event metadata.
            return EventType.GetRequiredInstance(resolvedType);
        }

        // Unknown and legacy names preserve metadata carried by the envelope.
        return new LegacyNostifyCommandEventType(name ?? "Unknown", isNew, allowNullPayload);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Keep successfully loaded types when an assembly is only partially loadable.
            return ex.Types.OfType<Type>();
        }
    }

    private static bool IsLegacyCompatibilityType(Type resolvedType) => resolvedType == typeof(LegacyNostifyCommandEventType);

    private static EventType? TryGetRequiredInstance(Type eventTypeType)
    {
        try
        {
            return EventType.GetRequiredInstance(eventTypeType);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class NewtonsoftEventTypeJsonConverter : JsonConverter<EventType>
{
    public override void WriteJson(JsonWriter writer, EventType? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        // Persist only stable logical event metadata.
        JObject.FromObject(value, JsonSerializer.CreateDefault()).WriteTo(writer);
    }

    public override EventType? ReadJson(JsonReader reader, Type objectType, EventType? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var jObject = JObject.Load(reader);
        var eventTypeName = jObject["name"]?.Value<string>();
        var resolvedType = EventTypeResolver.Resolve(eventTypeName);
        return EventTypeResolver.CreateInstance(
            resolvedType,
            eventTypeName,
            jObject["isNew"]?.Value<bool>() ?? false,
            jObject["allowNullPayload"]?.Value<bool>() ?? false);
    }
}

internal sealed class SystemTextEventTypeJsonConverter : STJS.JsonConverter<EventType>
{
    public override EventType? Read(ref STJ.Utf8JsonReader reader, Type typeToConvert, STJ.JsonSerializerOptions options)
    {
        if (reader.TokenType == STJ.JsonTokenType.Null)
        {
            return null;
        }

        using var document = STJ.JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        string? name = root.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
        bool isNew = root.TryGetProperty("isNew", out var isNewProperty) && isNewProperty.GetBoolean();
        bool allowNullPayload = root.TryGetProperty("allowNullPayload", out var allowNullPayloadProperty) && allowNullPayloadProperty.GetBoolean();

        var resolvedType = EventTypeResolver.Resolve(name);

        return EventTypeResolver.CreateInstance(resolvedType, name, isNew, allowNullPayload);
    }

    public override void Write(STJ.Utf8JsonWriter writer, EventType value, STJ.JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        var nestedOptions = CreateNestedOptions(options);
        using var document = STJ.JsonSerializer.SerializeToDocument(value, value.GetType(), nestedOptions);

        writer.WriteStartObject();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static STJ.JsonSerializerOptions CreateNestedOptions(STJ.JsonSerializerOptions options)
    {
        var nestedOptions = new STJ.JsonSerializerOptions(options);
        var converter = nestedOptions.Converters.FirstOrDefault(c => c is SystemTextEventTypeJsonConverter);
        if (converter != null)
        {
            nestedOptions.Converters.Remove(converter);
        }
        return nestedOptions;
    }
}
