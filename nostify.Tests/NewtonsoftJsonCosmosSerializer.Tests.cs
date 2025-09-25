using System;
using System.IO;
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using nostify;
using Xunit;

namespace nostify.Tests;

public class NewtonsoftJsonCosmosSerializerTests
{
    private readonly NewtonsoftJsonCosmosSerializer _serializer;

    public NewtonsoftJsonCosmosSerializerTests()
    {
        _serializer = new NewtonsoftJsonCosmosSerializer();
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip_Succeeds()
    {
        // Arrange
        var originalObject = new TestSerializableObject
        {
            Id = Guid.NewGuid(),
            Name = "Test Object",
            Value = 42,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var stream = _serializer.ToStream(originalObject);
        var deserializedObject = _serializer.FromStream<TestSerializableObject>(stream);

        // Assert
        Assert.NotNull(deserializedObject);
        Assert.Equal(originalObject.Id, deserializedObject.Id);
        Assert.Equal(originalObject.Name, deserializedObject.Name);
        Assert.Equal(originalObject.Value, deserializedObject.Value);
        Assert.Equal(originalObject.IsActive, deserializedObject.IsActive);
        Assert.Equal(originalObject.CreatedAt, deserializedObject.CreatedAt);
    }

    [Fact]
    public void SerializeDeserialize_IEvent_RoundTrip_Succeeds()
    {
        // Arrange - Create an IEvent using EventFactory
        var eventFactory = new EventFactory();
        var aggregateId = Guid.NewGuid();
        var testCommand = new SerializerTestCommand("Test_Event");
        var payload = new SerializerTestAggregate { id = aggregateId, Name = "Test Aggregate", Value = 123 };

        IEvent originalEvent = eventFactory.Create<SerializerTestAggregate>(testCommand, aggregateId, payload);

        // Act
        var stream = _serializer.ToStream(originalEvent);
        var deserializedEvent = _serializer.FromStream<IEvent>(stream);

        // Assert
        Assert.NotNull(deserializedEvent);
        Assert.Equal(originalEvent.id, deserializedEvent.id);
        Assert.Equal(originalEvent.aggregateRootId, deserializedEvent.aggregateRootId);
        Assert.Equal(originalEvent.command.ToString(), deserializedEvent.command.ToString());
        Assert.Equal(originalEvent.timestamp, deserializedEvent.timestamp);
        Assert.Equal(originalEvent.userId, deserializedEvent.userId);
        Assert.Equal(originalEvent.partitionKey, deserializedEvent.partitionKey);

        // Verify payload deserialization
        var deserializedPayload = deserializedEvent.GetPayload<SerializerTestAggregate>();
        Assert.NotNull(deserializedPayload);
        Assert.Equal(payload.id, deserializedPayload.id);
        Assert.Equal(payload.Name, deserializedPayload.Name);
        Assert.Equal(payload.Value, deserializedPayload.Value);
    }

    [Fact]
    public void SerializeDeserialize_EventWithNullPayload_Succeeds()
    {
        // Arrange - Create an event with null payload
        var eventFactory = new EventFactory();
        var aggregateId = Guid.NewGuid();
        var deleteCommand = new SerializerTestCommand("Delete_Test");

        IEvent originalEvent = eventFactory.CreateNullPayloadEvent(deleteCommand, aggregateId);

        // Act
        var stream = _serializer.ToStream(originalEvent);
        var deserializedEvent = _serializer.FromStream<IEvent>(stream);

        // Assert
        Assert.NotNull(deserializedEvent);
        Assert.Equal(originalEvent.id, deserializedEvent.id);
        Assert.Equal(originalEvent.aggregateRootId, deserializedEvent.aggregateRootId);
        Assert.Equal(originalEvent.command.ToString(), deserializedEvent.command.ToString());
        Assert.NotNull(deserializedEvent.payload);
        // The payload is an empty anonymous object, not null
        var payloadType = deserializedEvent.payload.GetType();
        Assert.True(payloadType.Name.Contains("AnonymousType") || payloadType == typeof(Newtonsoft.Json.Linq.JObject));
    }

    [Fact]
    public void FromStream_EmptyStream_ReturnsDefault()
    {
        // Arrange
        var emptyStream = new MemoryStream();

        // Act
        var result = _serializer.FromStream<TestSerializableObject>(emptyStream);

        // Assert
        Assert.Equal(default(TestSerializableObject), result);
    }

    [Fact]
    public void FromStream_NullStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _serializer.FromStream<TestSerializableObject>(null!));
    }

    [Fact]
    public void ToStream_UsesUTF8WithoutBOM()
    {
        // Arrange
        var testObject = new TestSerializableObject { Name = "Test" };

        // Act
        var stream = _serializer.ToStream(testObject);

        // Assert - Check that the stream starts with valid UTF-8 without BOM
        var buffer = new byte[3];
        stream.Read(buffer, 0, 3);
        stream.Position = 0; // Reset for potential reuse

        // UTF-8 BOM would be 0xEF, 0xBB, 0xBF at the start
        // Valid JSON should start with '{' (0x7B) for an object
        Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, buffer.Take(3).ToArray());
    }

    [Fact]
    public void ToStream_StreamPositionResetToZero()
    {
        // Arrange
        var testObject = new TestSerializableObject { Name = "Test" };

        // Act
        var stream = _serializer.ToStream(testObject);

        // Assert
        Assert.Equal(0, stream.Position);
        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void SerializeDeserialize_ComplexObjectWithNestedTypes_Succeeds()
    {
        // Arrange
        var complexObject = new ComplexTestObject
        {
            Id = Guid.NewGuid(),
            SimpleObject = new TestSerializableObject
            {
                Id = Guid.NewGuid(),
                Name = "Nested Object",
                Value = 999,
                IsActive = false
            },
            Items = new[]
            {
                new TestSerializableObject { Id = Guid.NewGuid(), Name = "Item1", Value = 1 },
                new TestSerializableObject { Id = Guid.NewGuid(), Name = "Item2", Value = 2 }
            },
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 42,
                ["key3"] = true
            }
        };

        // Act
        var stream = _serializer.ToStream(complexObject);
        var deserializedObject = _serializer.FromStream<ComplexTestObject>(stream);

        // Assert
        Assert.NotNull(deserializedObject);
        Assert.Equal(complexObject.Id, deserializedObject.Id);

        Assert.NotNull(deserializedObject.SimpleObject);
        Assert.Equal(complexObject.SimpleObject.Id, deserializedObject.SimpleObject.Id);
        Assert.Equal(complexObject.SimpleObject.Name, deserializedObject.SimpleObject.Name);
        Assert.Equal(complexObject.SimpleObject.Value, deserializedObject.SimpleObject.Value);

        Assert.NotNull(deserializedObject.Items);
        Assert.Equal(2, deserializedObject.Items.Length);
        Assert.Equal(complexObject.Items[0].Name, deserializedObject.Items[0].Name);
        Assert.Equal(complexObject.Items[1].Value, deserializedObject.Items[1].Value);

        Assert.NotNull(deserializedObject.Metadata);
        Assert.Equal(3, deserializedObject.Metadata.Count);
        Assert.Equal("value1", deserializedObject.Metadata["key1"]);
        Assert.Equal(42, (long)deserializedObject.Metadata["key2"]);
        Assert.Equal(true, deserializedObject.Metadata["key3"]);
    }

    [Fact]
    public void SerializeDeserialize_NullValue_Succeeds()
    {
        // Arrange
        TestSerializableObject? nullObject = null;

        // Act
        var stream = _serializer.ToStream(nullObject);
        var deserializedObject = _serializer.FromStream<TestSerializableObject?>(stream);

        // Assert
        Assert.Null(deserializedObject);
    }

    [Fact]
    public void FromStream_NonSeekableStream_HandlesGracefully()
    {
        // Arrange - Create a non-seekable stream by wrapping a string in a way that doesn't support seeking
        var json = "{\"Name\":\"Test\",\"Value\":123}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var nonSeekableStream = new NonSeekableMemoryStream(bytes);

        // Act
        var result = _serializer.FromStream<TestSerializableObject>(nonSeekableStream);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(123, result.Value);
    }
}

// Test classes for serializer testing
public class TestSerializableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ComplexTestObject
{
    public Guid Id { get; set; }
    public TestSerializableObject SimpleObject { get; set; } = null!;
    public TestSerializableObject[] Items { get; set; } = Array.Empty<TestSerializableObject>();
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}

public class SerializerTestAggregate : NostifyObject, IAggregate
{
    public static string aggregateType => "SerializerTestAggregate";
    public static string currentStateContainerName => "SerializerTestAggregateCurrentState";
    public bool isDeleted { get; set; } = false;
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }

    public override void Apply(IEvent e)
    {
        UpdateProperties<SerializerTestAggregate>(e.payload);
    }
}

public class SerializerTestCommand : NostifyCommand
{
    public SerializerTestCommand(string name, bool isNew = false) : base(name, isNew)
    {
    }
}

// Helper class for testing non-seekable streams
public class NonSeekableMemoryStream : Stream
{
    private readonly byte[] _data;
    private int _position;

    public NonSeekableMemoryStream(byte[] data)
    {
        _data = data;
        _position = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesToRead = Math.Min(count, _data.Length - _position);
        if (bytesToRead > 0)
        {
            Array.Copy(_data, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;
        }
        return bytesToRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}