using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using nostify;
using STJ = System.Text.Json;
using STJS = System.Text.Json.Serialization;

namespace nostify.Tests;

/// <summary>
/// Covers serializer edge behavior that serializer-level null short-circuiting can otherwise bypass.
/// </summary>
public class SerializationEdgeCaseTests
{
    private sealed class ResolverCanonicalEventType : EventType
    {
        public ResolverCanonicalEventType()
            : base("Resolver_Canonical_Event", isNew: true, allowNullPayload: true)
        {
        }
    }

    private static Type[] CreateDuplicateEventTypes(string eventTypeName)
    {
        var assemblyName = new AssemblyName($"ResolverDuplicateEventTypes_{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);

        return new[]
        {
            CreateEventType(moduleBuilder, "DuplicateResolverEventTypeA", eventTypeName),
            CreateEventType(moduleBuilder, "DuplicateResolverEventTypeB", eventTypeName)
        };
    }

    private static Type CreateEventType(ModuleBuilder moduleBuilder, string typeName, string eventTypeName)
    {
        var typeBuilder = moduleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(EventType));
        var constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        var generator = constructor.GetILGenerator();
        var baseConstructor = typeof(EventType).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(string), typeof(bool), typeof(bool) },
            modifiers: null)!;

        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldstr, eventTypeName);
        generator.Emit(OpCodes.Ldc_I4_0);
        generator.Emit(OpCodes.Ldc_I4_0);
        generator.Emit(OpCodes.Call, baseConstructor);
        generator.Emit(OpCodes.Ret);

        return typeBuilder.CreateType()!;
    }

    [Fact]
    public void WithRetry_LoggerOverload_EnablesLogging()
    {
        var container = new Mock<Container>();
        var logger = new Mock<ILogger>();

        IRetryableContainer result = container.Object.WithRetry(logger.Object);

        Assert.Same(container.Object, result.Container);
        Assert.Same(logger.Object, result.Options.Logger);
        Assert.True(result.Options.LogRetries);
        Assert.False(result.Options.RetryWhenNotFound);
    }

    [Fact]
    public void WithRetry_NotFoundAndLoggerOverload_ConfiguresBothBehaviors()
    {
        var container = new Mock<Container>();
        var logger = new Mock<ILogger>();

        IRetryableContainer result =
            container.Object.WithRetry(true, logger.Object);

        Assert.Same(container.Object, result.Container);
        Assert.Same(logger.Object, result.Options.Logger);
        Assert.True(result.Options.LogRetries);
        Assert.True(result.Options.RetryWhenNotFound);
    }

    [Fact]
    public void NostifyDefault_InterfaceConverter_WriteJson_UsesConcreteType()
    {
        JsonConverter converter = Assert.Single(
            SerializationSettings.NostifyDefault.Converters,
            candidate => candidate.CanConvert(typeof(IEvent)));
        IEvent value = new Event(
            new NostifyCommand("Direct_Interface_Write"),
            Guid.NewGuid(),
            new { name = "payload" });
        var output = new StringWriter();
        using var writer = new JsonTextWriter(output);

        converter.WriteJson(
            writer,
            value,
            JsonSerializer.CreateDefault(
                SerializationSettings.NostifyDefault));
        writer.Flush();

        Assert.Contains(
            "\"aggregateRootId\"",
            output.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemTextInterfaceConverter_WriteNull_WritesJsonNull()
    {
        STJ.JsonSerializerOptions options =
            WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions();
        var converter = Assert.IsAssignableFrom<STJS.JsonConverter<IEvent>>(
            options.GetConverter(typeof(IEvent)));
        using var output = new MemoryStream();
        using (var writer = new STJ.Utf8JsonWriter(output))
        {
            // Invoke directly because serializer-level null handling bypasses converters.
            converter.Write(writer, null!, options);
        }

        Assert.Equal("null", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void EventType_BlankName_ThrowsDescriptiveException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new LegacyNostifyCommandEventType(" "));

        Assert.Equal("name", exception.ParamName);
        Assert.StartsWith(
            "Event type name cannot be null or empty",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EventType_CompareTo_UsesOrdinalNameOrder()
    {
        var first = new LegacyNostifyCommandEventType("Alpha");
        var second = new LegacyNostifyCommandEventType("Bravo");

        Assert.True(first.CompareTo(second) < 0);
        Assert.True(second.CompareTo(first) > 0);
    }

    [Fact]
    public void GetRequiredInstance_NonEventType_ThrowsDescriptiveException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => EventType.GetRequiredInstance(typeof(string)));

        Assert.Contains(
            "must derive from EventType",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GetRequiredInstance_AbstractEventType_ThrowsDescriptiveException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => EventType.GetRequiredInstance(typeof(EventType)));

        Assert.Contains(
            "must be a concrete EventType type",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyEventsAttribute_EmptyTypeArray_ThrowsDescriptiveException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ApplyEventsAttribute(Array.Empty<Type>()));

        Assert.Equal("eventTypeTypes", exception.ParamName);
    }

    [Fact]
    public void ApplyEventsAttribute_EmptyNameArray_ThrowsDescriptiveException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ApplyEventsAttribute(Array.Empty<string>()));

        Assert.Equal("eventTypeNames", exception.ParamName);
    }

    [Fact]
    public void ApplyEventsAttribute_BlankName_ThrowsDescriptiveException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ApplyEventsAttribute("valid", " "));

        Assert.Equal("eventTypeNames", exception.ParamName);
        Assert.StartsWith(
            "EventType names cannot be null, empty, or whitespace.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NewtonsoftEventTypeConverter_WriteNull_WritesJsonNull()
    {
        var converter = new NewtonsoftEventTypeJsonConverter();
        var output = new StringWriter();
        using var writer = new JsonTextWriter(output);

        // Invoke the converter directly because Newtonsoft may short-circuit null root values.
        converter.WriteJson(writer, null, JsonSerializer.CreateDefault());
        writer.Flush();

        Assert.Equal("null", output.ToString());
    }

    [Fact]
    public void NewtonsoftEventTypeConverter_Write_UsesNameOnlyWireMetadata()
    {
        var converter = new NewtonsoftEventTypeJsonConverter();
        var output = new StringWriter();
        using var writer = new JsonTextWriter(output);

        converter.WriteJson(
            writer,
            new ResolverCanonicalEventType(),
            JsonSerializer.CreateDefault());
        writer.Flush();

        string json = output.ToString();
        var metadata = Newtonsoft.Json.Linq.JObject.Parse(json);
        Assert.Equal("Resolver_Canonical_Event", metadata.Value<string>("name"));
        Assert.Equal(3, metadata.Properties().Count());
    }

    [Fact]
    public void NewtonsoftEventTypeConverter_ReadNull_ReturnsNull()
    {
        var converter = new NewtonsoftEventTypeJsonConverter();
        using var reader = new JsonTextReader(new StringReader("null"));
        Assert.True(reader.Read());

        EventType? result = converter.ReadJson(
            reader,
            typeof(EventType),
            existingValue: null,
            hasExistingValue: false,
            JsonSerializer.CreateDefault());

        Assert.Null(result);
    }

    [Fact]
    public void NewtonsoftEventTypeConverter_UnknownName_PreservesLogicalMetadata()
    {
        const string json = """
            {
              "name": "Unknown_Command",
              "isNew": true,
              "allowNullPayload": true
            }
            """;
        var converter = new NewtonsoftEventTypeJsonConverter();
        using var reader = new JsonTextReader(new StringReader(json));
        Assert.True(reader.Read());

        EventType? result = converter.ReadJson(
            reader,
            typeof(EventType),
            existingValue: null,
            hasExistingValue: false,
            JsonSerializer.CreateDefault());

        Assert.NotNull(result);
        Assert.Equal("Unknown_Command", result.name);
        Assert.True(result.isNew);
        Assert.True(result.allowNullPayload);
    }

    [Fact]
    public void NewtonsoftEventTypeConverter_KnownName_UsesCanonicalMetadata()
    {
        const string json = """
            {
              "name": "Resolver_Canonical_Event",
              "isNew": false,
              "allowNullPayload": false
            }
            """;

        EventType? result = JsonConvert.DeserializeObject<EventType>(
            json,
            new NewtonsoftEventTypeJsonConverter());

        var canonical = Assert.IsType<ResolverCanonicalEventType>(result);
        Assert.True(canonical.isNew);
        Assert.True(canonical.allowNullPayload);
    }

    [Fact]
    public void EventTypeResolver_DifferentlyCasedName_UsesLegacyAdapter()
    {
        Type resolvedType = EventTypeResolver.Resolve("resolver_canonical_event");
        EventType result = EventTypeResolver.CreateInstance(
            resolvedType,
            "resolver_canonical_event",
            isNew: false,
            allowNullPayload: true);

        var legacy = Assert.IsType<LegacyNostifyCommandEventType>(result);
        Assert.Equal("resolver_canonical_event", legacy.name);
        Assert.True(legacy.allowNullPayload);
    }

    [Fact]
    public void EventTypeResolver_DuplicateName_ListsConflictingTypesDeterministically()
    {
        string eventTypeName = $"Resolver_Duplicate_Event_{Guid.NewGuid():N}";
        Type[] duplicateTypes = CreateDuplicateEventTypes(eventTypeName);

        var exception = Assert.Throws<InvalidOperationException>(
            () => EventTypeResolver.Resolve(eventTypeName));

        string firstType = duplicateTypes[0].AssemblyQualifiedName!;
        string secondType = duplicateTypes[1].AssemblyQualifiedName!;
        Assert.Contains(firstType, exception.Message, StringComparison.Ordinal);
        Assert.Contains(secondType, exception.Message, StringComparison.Ordinal);
        Assert.True(
            exception.Message.IndexOf(firstType, StringComparison.Ordinal) <
            exception.Message.IndexOf(secondType, StringComparison.Ordinal));
    }

    [Fact]
    public void SystemTextEventTypeConverter_ReadNull_ReturnsNull()
    {
        var converter = new SystemTextEventTypeJsonConverter();
        var reader = new STJ.Utf8JsonReader(Encoding.UTF8.GetBytes("null"));
        Assert.True(reader.Read());

        EventType? result = converter.Read(
            ref reader,
            typeof(EventType),
            new STJ.JsonSerializerOptions());

        Assert.Null(result);
    }

    [Fact]
    public void SystemTextEventTypeConverter_WriteNull_WritesJsonNull()
    {
        var converter = new SystemTextEventTypeJsonConverter();
        using var output = new MemoryStream();
        using (var writer = new STJ.Utf8JsonWriter(output))
        {
            // Invoke directly because System.Text.Json does not normally pass null to converters.
            converter.Write(
                writer,
                null!,
                new STJ.JsonSerializerOptions());
        }

        Assert.Equal("null", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void SystemTextEventTypeConverter_Write_UsesNameOnlyWireMetadata()
    {
        var converter = new SystemTextEventTypeJsonConverter();
        using var output = new MemoryStream();
        using (var writer = new STJ.Utf8JsonWriter(output))
        {
            converter.Write(
                writer,
                new ResolverCanonicalEventType(),
                new STJ.JsonSerializerOptions());
        }

        string json = Encoding.UTF8.GetString(output.ToArray());
        using var metadata = STJ.JsonDocument.Parse(json);
        Assert.Equal("Resolver_Canonical_Event", metadata.RootElement.GetProperty("name").GetString());
        Assert.Equal(3, metadata.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void SystemTextEventTypeConverter_UnknownName_PreservesLogicalMetadata()
    {
        const string json = """
            {
              "name": "Unknown_Command",
              "isNew": true,
              "allowNullPayload": true
            }
            """;
        var converter = new SystemTextEventTypeJsonConverter();
        var reader = new STJ.Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read());

        EventType? result = converter.Read(
            ref reader,
            typeof(EventType),
            new STJ.JsonSerializerOptions());

        Assert.NotNull(result);
        Assert.Equal("Unknown_Command", result.name);
        Assert.True(result.isNew);
        Assert.True(result.allowNullPayload);
    }

    [Fact]
    public void NostifyDefault_SerializesIEventThroughConcreteEventConverter()
    {
        IEvent value = new Event(
            new NostifyCommand("Serialize_Interface"),
            Guid.NewGuid(),
            new { name = "payload" });

        string json = JsonConvert.SerializeObject(
            value,
            typeof(IEvent),
            SerializationSettings.NostifyDefault);

        Assert.Contains("\"eventType\"", json, StringComparison.Ordinal);
        Assert.Contains("\"aggregateRootId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"payload\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemTextOptions_SerializingNonConcreteIEvent_ThrowsDescriptiveException()
    {
        IEvent incompatibleValue = new Mock<IEvent>().Object;
        STJ.JsonSerializerOptions options =
            WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions();

        var exception = Assert.Throws<STJ.JsonException>(() =>
            STJ.JsonSerializer.Serialize<IEvent>(incompatibleValue, options));

        Assert.Equal(
            "Expected value assignable to Event when serializing IEvent.",
            exception.Message);
    }
}
