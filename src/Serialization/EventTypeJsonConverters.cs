using System;
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

    internal static Type Resolve(string? typeName)
    {
#pragma warning disable CS0618
        if (string.IsNullOrWhiteSpace(typeName)) return typeof(NostifyCommand);
#pragma warning restore CS0618

        var resolved = Type.GetType(typeName, throwOnError: false);
        if (resolved != null && typeof(EventType).IsAssignableFrom(resolved)) return resolved;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            resolved = assembly.GetType(typeName, throwOnError: false);
            if (resolved != null && typeof(EventType).IsAssignableFrom(resolved)) return resolved;
        }

#pragma warning disable CS0618
        return typeof(NostifyCommand);
#pragma warning restore CS0618
    }

    internal static EventType CreateInstance(Type resolvedType, string? name, bool isNew, bool allowNullPayload)
    {
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

        throw new JsonSerializationException($"Unable to construct event type '{resolvedType.FullName}'. Ensure it exposes a supported constructor.");
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
        var resolvedType = EventTypeResolver.Resolve(typeName);
        return EventTypeResolver.CreateInstance(
            resolvedType,
            jObject["name"]?.Value<string>(),
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

        return EventTypeResolver.CreateInstance(EventTypeResolver.Resolve(typeName), name, isNew, allowNullPayload);
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
