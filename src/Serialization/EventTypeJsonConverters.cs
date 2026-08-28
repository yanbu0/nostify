using System;
using System.Linq;
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

        var jObject = JObject.FromObject(value, serializer);
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
        using var subReader = jObject.CreateReader();
        return serializer.Deserialize(subReader, resolvedType) as EventType;
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
        string? typeName = null;
        if (document.RootElement.TryGetProperty(EventTypeResolver.TypeDiscriminatorPropertyName, out var typeProperty))
        {
            typeName = typeProperty.GetString();
        }

        var resolvedType = EventTypeResolver.Resolve(typeName);
        return STJ.JsonSerializer.Deserialize(document.RootElement.GetRawText(), resolvedType, CreateNestedOptions(options)) as EventType;
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
