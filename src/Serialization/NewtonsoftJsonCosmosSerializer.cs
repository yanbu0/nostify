
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using System;
using System.IO;

namespace nostify;

/// <summary>
/// A Cosmos DB serializer implementation using Newtonsoft.Json for object serialization and deserialization.
/// </summary>
public class NewtonsoftJsonCosmosSerializer : CosmosSerializer
{

    /// <summary>
    /// Deserializes an object of type <typeparamref name="T"/> from the provided <see cref="Stream"/> using Newtonsoft.Json.
    /// </summary>
    /// <typeparam name="T">The type of the object to deserialize.</typeparam>
    /// <param name="stream">The stream containing the JSON data.</param>
    /// <returns>The deserialized object of type <typeparamref name="T"/>.</returns>
    public override T FromStream<T>(Stream stream)
    {
        if (stream == null)
        {
            throw new System.ArgumentNullException(nameof(stream));
        }

        // If stream is empty return default(T) (Cosmos may pass an empty stream for null payloads)
        if (stream.CanSeek && stream.Length == 0)
        {
            return default!;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var serializer = JsonSerializer.Create(SerializationSettings.NostifyDefault);

        using var streamReader = new StreamReader(stream);
        using var jsonTextReader = new JsonTextReader(streamReader);
        var deserialized = serializer.Deserialize<T>(jsonTextReader);
        return deserialized!;
    }

    /// <summary>
    /// Serializes the specified object of type <typeparamref name="T"/> to a <see cref="Stream"/> using Newtonsoft.Json.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="input">The object to serialize.</param>
    /// <returns>A <see cref="Stream"/> containing the serialized JSON representation of the object.</returns>
    public override Stream ToStream<T>(T input)
    {
        // Create a memory stream to hold the JSON
        var stream = new MemoryStream();

        // Use UTF8 without BOM which is the expected encoding for Cosmos DB
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var streamWriter = new StreamWriter(stream, utf8NoBom, leaveOpen: true);
        using var jsonTextWriter = new JsonTextWriter(streamWriter) { CloseOutput = false };

        var serializer = JsonSerializer.Create(SerializationSettings.NostifyDefault);
        serializer.Serialize(jsonTextWriter, input);
        jsonTextWriter.Flush();

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return stream;
    }
}
