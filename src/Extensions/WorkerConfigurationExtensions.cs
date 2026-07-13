using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace nostify;

/// <summary>
/// Extension methods for configuring Azure Functions worker JSON serialization with nostify defaults.
/// </summary>
public static class WorkerConfigurationExtensions
{
    /// <summary>
    /// Configures the Functions worker to use Newtonsoft.Json with <see cref="SerializationSettings.NostifyDefault"/>.
    /// </summary>
    /// <param name="builder">The Functions worker application builder.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static IFunctionsWorkerApplicationBuilder UseNostifyDefaultJson(this IFunctionsWorkerApplicationBuilder builder)
    {
        builder.Services.Configure<WorkerOptions>(workerOptions =>
        {
            workerOptions.Serializer = new NewtonsoftJsonObjectSerializer(SerializationSettings.NostifyDefault);
        });

        return builder;
    }

    /// <summary>
    /// Configures the Functions worker to use Newtonsoft.Json with <see cref="SerializationSettings.NostifyDefault"/>.
    /// </summary>
    /// <param name="builder">The Functions worker application builder.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static IFunctionsWorkerApplicationBuilder UseNostifyDefaultConfiguredNewtonsoftJson(this IFunctionsWorkerApplicationBuilder builder) => builder.UseNostifyDefaultJson();

    /// <summary>
    /// Configures the Functions worker to use System.Text.Json with nostify-compatible defaults.
    /// </summary>
    /// <param name="builder">The Functions worker application builder.</param>
    /// <returns>The same builder instance for chaining.</returns>
    [Experimental("NOSTIFY001")]
    public static IFunctionsWorkerApplicationBuilder UseNostifySystemTextJson(this IFunctionsWorkerApplicationBuilder builder)
    {
        builder.Services.Configure<WorkerOptions>(workerOptions =>
        {
            workerOptions.Serializer = new JsonObjectSerializer(CreateNostifyDefaultSystemTextJsonOptions());
        });

        return builder;
    }

    internal static JsonSerializerOptions CreateNostifyDefaultSystemTextJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new InterfaceConverter<IEvent, Event>());
        options.Converters.Add(new InterfaceConverter<ISaga, Saga>());
        options.Converters.Add(new InterfaceConverter<ISagaStep, SagaStep>());
        options.Converters.Add(new ObjectToInferredTypesConverter());

        return options;
    }

    private sealed class InterfaceConverter<TInterface, TConcrete> : JsonConverter<TInterface> where TConcrete : class, TInterface
    {
        public override TInterface? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonSerializer.Deserialize<TConcrete>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, TInterface value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            if (value is not TConcrete concreteValue)
            {
                throw new JsonException($"Expected value assignable to {typeof(TConcrete).Name} when serializing {typeof(TInterface).Name}.");
            }

            JsonSerializer.Serialize(writer, concreteValue, options);
        }
    }

    private sealed class ObjectToInferredTypesConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long longValue))
                    {
                        return longValue;
                    }

                    if (reader.TryGetDecimal(out decimal decimalValue))
                    {
                        return decimalValue;
                    }

                    return reader.GetDouble();
                case JsonTokenType.String:
                    if (reader.TryGetDateTime(out DateTime dateTimeValue))
                    {
                        return dateTimeValue;
                    }

                    return reader.GetString();
                case JsonTokenType.StartObject:
                    return JsonSerializer.Deserialize<Dictionary<string, object?>>(ref reader, options) ?? new Dictionary<string, object?>();
                case JsonTokenType.StartArray:
                    return JsonSerializer.Deserialize<List<object?>>(ref reader, options) ?? new List<object?>();
                case JsonTokenType.Null:
                    return null;
                default:
                    using (var document = JsonDocument.ParseValue(ref reader))
                    {
                        return document.RootElement.Clone();
                    }
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value is JsonElement jsonElement)
            {
                jsonElement.WriteTo(writer);
                return;
            }

            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
