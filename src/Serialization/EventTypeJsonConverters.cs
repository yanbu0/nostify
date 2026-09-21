using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using STJ = System.Text.Json;
using STJS = System.Text.Json.Serialization;

namespace nostify;

internal static class EventTypeResolver
{
    internal const string TypeDiscriminatorPropertyName = "$eventTypeClrType";
    private static readonly Type MissingEventTypeSentinel = typeof(void);
    private static readonly ConcurrentDictionary<string, Type> _eventTypeByNameCache = new(StringComparer.OrdinalIgnoreCase);

    internal static Type Resolve(string? typeName, string? eventTypeName = null)
    {
#pragma warning disable CS0618
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return ResolveByName(eventTypeName) ?? typeof(NostifyCommand);
        }
#pragma warning restore CS0618

        var resolved = Type.GetType(typeName, throwOnError: false);
        if (resolved != null && typeof(EventType).IsAssignableFrom(resolved))
        {
            if (IsLegacyNostifyCommandType(resolved))
            {
                return ResolveByName(eventTypeName) ?? resolved;
            }
            return resolved;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            resolved = assembly.GetType(typeName, throwOnError: false);
            if (resolved != null && typeof(EventType).IsAssignableFrom(resolved))
            {
                if (IsLegacyNostifyCommandType(resolved))
                {
                    return ResolveByName(eventTypeName) ?? resolved;
                }
                return resolved;
            }
        }

#pragma warning disable CS0618
        return ResolveByName(eventTypeName) ?? typeof(NostifyCommand);
#pragma warning restore CS0618
    }

    internal static EventType CreateInstance(Type resolvedType, string? name, bool isNew, bool allowNullPayload)
    {
        if (!IsLegacyNostifyCommandType(resolvedType))
        {
            return EventType.GetRequiredInstance(resolvedType);
        }

        var constructorArgs = new object?[] { name ?? "Unknown", isNew, allowNullPayload };
        var signatures = new[]
        {
            new[] { typeof(string), typeof(bool), typeof(bool) },
            new[] { typeof(string), typeof(bool) },
            new[] { typeof(string) },
            Type.EmptyTypes
        };

        foreach (var signature in signatures)
        {
            ConstructorInfo? constructor = resolvedType.GetConstructor(signature);
            if (constructor == null)
            {
                continue;
            }

            object?[] args = signature.Length switch
            {
                3 => constructorArgs,
                2 => constructorArgs.Take(2).ToArray(),
                1 => constructorArgs.Take(1).ToArray(),
                _ => Array.Empty<object?>()
            };

            if (constructor.Invoke(args) is EventType eventType)
            {
                return eventType;
            }
        }

        throw new JsonSerializationException(
            $"Unable to construct legacy command event type '{resolvedType.FullName}'. Ensure it exposes a supported constructor.");
    }

    private static Type? ResolveByName(string? eventTypeName)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            return null;
        }

        if (_eventTypeByNameCache.TryGetValue(eventTypeName, out var cachedType))
        {
            return cachedType == MissingEventTypeSentinel ? null : cachedType;
        }

        var resolvedType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(t => t != null)!;
                }
            })
            .Where(t => t != null
                && !t.IsAbstract
                && typeof(EventType).IsAssignableFrom(t))
            .Where(t => !IsLegacyNostifyCommandType(t))
            .Select(t => new { Type = t, EventType = TryGetRequiredInstance(t) })
            .Where(x => x.EventType != null && string.Equals(x.EventType.name, eventTypeName, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Type)
            .OrderBy(t => t.AssemblyQualifiedName, StringComparer.Ordinal)
            .FirstOrDefault();

        _eventTypeByNameCache[eventTypeName] = resolvedType ?? MissingEventTypeSentinel;
        return resolvedType;
    }

    private static bool IsLegacyNostifyCommandType(Type resolvedType)
    {
#pragma warning disable CS0618
        return typeof(NostifyCommand).IsAssignableFrom(resolvedType);
#pragma warning restore CS0618
    }

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

        var jObject = JObject.FromObject(value, JsonSerializer.CreateDefault());
        jObject[EventTypeResolver.TypeDiscriminatorPropertyName] = value.GetType().AssemblyQualifiedName;
        jObject.WriteTo(writer);
    }

    public override EventType? ReadJson(JsonReader reader, Type objectType, EventType? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var jObject = JObject.Load(reader);
        var typeName = jObject[EventTypeResolver.TypeDiscriminatorPropertyName]?.Value<string>();
        var eventTypeName = jObject["name"]?.Value<string>();
        var resolvedType = EventTypeResolver.Resolve(typeName, eventTypeName);
#pragma warning disable CS0618
        if (typeof(NostifyCommand).IsAssignableFrom(objectType) &&
            !typeof(NostifyCommand).IsAssignableFrom(resolvedType))
        {
            resolvedType = typeof(NostifyCommand);
        }
#pragma warning restore CS0618
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

        string? typeName = null;
        if (root.TryGetProperty(EventTypeResolver.TypeDiscriminatorPropertyName, out var typeProperty))
        {
            typeName = typeProperty.GetString();
        }

        string? name = root.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
        bool isNew = root.TryGetProperty("isNew", out var isNewProperty) && isNewProperty.GetBoolean();
        bool allowNullPayload = root.TryGetProperty("allowNullPayload", out var allowNullPayloadProperty) && allowNullPayloadProperty.GetBoolean();

        var resolvedType = EventTypeResolver.Resolve(typeName, name);
#pragma warning disable CS0618
        if (typeof(NostifyCommand).IsAssignableFrom(typeToConvert) &&
            !typeof(NostifyCommand).IsAssignableFrom(resolvedType))
        {
            resolvedType = typeof(NostifyCommand);
        }
#pragma warning restore CS0618

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
        writer.WriteString(EventTypeResolver.TypeDiscriminatorPropertyName, value.GetType().AssemblyQualifiedName);
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
