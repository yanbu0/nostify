using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace nostify
{
    /// <summary>
    /// JSON converter for serializing and deserializing lists of <see cref="ISagaStep"/>.
    /// </summary>
    public class ListISagaStepJsonConverter : JsonConverter<List<ISagaStep>>
    {
        public override List<ISagaStep> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Deserialize as List<SagaStep>
            var sagaSteps = JsonSerializer.Deserialize<List<SagaStep>>(ref reader, options);
            // Return as List<ISagaStep>
            return sagaSteps != null ? new List<ISagaStep>(sagaSteps) : new List<ISagaStep>();
        }

        public override void Write(Utf8JsonWriter writer, List<ISagaStep> value, JsonSerializerOptions options)
        {
            // Serialize as List<SagaStep>
            var concreteList = new List<SagaStep>();
            foreach (var step in value)
            {
                if (step is SagaStep sagaStep)
                    concreteList.Add(sagaStep);
                else
                    throw new JsonException("All ISagaStep items must be of type SagaStep for serialization.");
            }
            JsonSerializer.Serialize(writer, concreteList, options);
        }
    }
}
