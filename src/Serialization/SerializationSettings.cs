
using System;
using Newtonsoft.Json;
using Azure.Core.Serialization;
using Newtonsoft.Json.Serialization;

namespace nostify;

/// <summary>
/// Provides default serialization settings and converters for the nostify framework.
/// </summary>
public static class SerializationSettings
{
    private class InterfaceConverter<TInterface, TConcrete> : JsonConverter where TConcrete : TInterface
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(TInterface);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            return serializer.Deserialize<TConcrete>(reader);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value, typeof(TConcrete));
        }
    }


    /// <summary>
    /// Gets the default <see cref="JsonSerializerSettings"/> used by the nostify framework for serialization.
    /// </summary>
        public static JsonSerializerSettings NostifyDefault
        {
            get
            {
                var settings = NewtonsoftJsonObjectSerializer.CreateJsonSerializerSettings();
                settings.Converters.Add(new InterfaceConverter<IEvent, Event>());
                settings.Converters.Add(new InterfaceConverter<ISaga, Saga>());
                settings.Converters.Add(new InterfaceConverter<ISagaStep, SagaStep>());
                settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                settings.NullValueHandling = NullValueHandling.Ignore;
                return settings;
            }
        }
    
}