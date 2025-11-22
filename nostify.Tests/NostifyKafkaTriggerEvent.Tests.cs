using System;
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using nostify;
using Xunit;

namespace nostify.Tests;

public class NostifyKafkaTriggerEventTests
{
    [Fact]
    public void GetEvent_WithValidJson_ReturnsEvent()
    {
        // Arrange
        var originalEvent = new Event
        {
            aggregateRootId = Guid.NewGuid(),
            command = new NostifyCommand("TestCommand"),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = Guid.NewGuid()
        };

        var jsonValue = JsonConvert.SerializeObject(originalEvent);
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = jsonValue,
            Offset = 1,
            Partition = 0,
            Topic = "test-topic",
            Key = "test-key",
            Headers = new[] { "header1", "header2" }
        };

        // Act
        var result = kafkaEvent.GetEvent();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(originalEvent.id, result.id);
        Assert.Equal(originalEvent.aggregateRootId, result.aggregateRootId);
        Assert.Equal(originalEvent.command.ToString(), result.command.ToString());
        Assert.Equal(originalEvent.timestamp, result.timestamp);
        Assert.Equal(originalEvent.userId, result.userId);
        Assert.Equal(originalEvent.partitionKey, result.partitionKey);
    }

    [Fact]
    public void GetEvent_WithNullValue_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = "",
            Offset = 1,
            Partition = 0,
            Topic = "test-topic"
        };

        // Act
        var result = kafkaEvent.GetEvent();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetEvent_WithInvalidJson_ThrowsJsonReaderException()
    {
        // Arrange
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = "invalid json",
            Offset = 1,
            Partition = 0,
            Topic = "test-topic"
        };

        // Act & Assert
        Assert.Throws<JsonReaderException>(() => kafkaEvent.GetEvent());
    }

    [Fact]
    public void GetIEvent_WithValidJson_ReturnsIEvent()
    {
        // Arrange
        var originalEvent = new Event
        {
            aggregateRootId = Guid.NewGuid(),
            command = new NostifyCommand("TestCommand"),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = Guid.NewGuid()
        };

        var jsonValue = JsonConvert.SerializeObject(originalEvent, SerializationSettings.NostifyDefault);
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = jsonValue,
            Offset = 1,
            Partition = 0,
            Topic = "test-topic"
        };

        // Act
        var result = kafkaEvent.GetIEvent();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(originalEvent.id, result.id);
        Assert.Equal(originalEvent.aggregateRootId, result.aggregateRootId);
        Assert.Equal(originalEvent.command.ToString(), result.command.ToString());
        Assert.Equal(originalEvent.timestamp, result.timestamp);
        Assert.Equal(originalEvent.userId, result.userId);
        Assert.Equal(originalEvent.partitionKey, result.partitionKey);
    }

    [Fact]
    public void GetIEvent_UsesProperSerializationSettings()
    {
        // Arrange - Create an event and serialize it with the proper settings
        var originalEvent = new Event
        {
            aggregateRootId = Guid.NewGuid(),
            command = new NostifyCommand("TestCommand"),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = Guid.NewGuid()
        };

        // Serialize with the same settings that GetIEvent uses
        var jsonValue = JsonConvert.SerializeObject(originalEvent, SerializationSettings.NostifyDefault);
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = jsonValue,
            Offset = 1,
            Partition = 0,
            Topic = "test-topic"
        };

        // Act
        var result = kafkaEvent.GetIEvent();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEvent>(result);
        Assert.Equal(originalEvent.id, result.id);
        Assert.Equal(originalEvent.aggregateRootId, result.aggregateRootId);
    }

    [Fact]
    public void GetIEvent_WithNullValue_ThrowsInvalidOperationException()
    {
        // Arrange
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = "",
            Offset = 1,
            Partition = 0,
            Topic = "test-topic"
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => kafkaEvent.GetIEvent());
    }

    [Fact]
    public void GetIEvent_WithInvalidJson_ThrowsJsonReaderException()
    {
        // Arrange
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = "invalid json",
            Offset = 1,
            Partition = 0,
            Topic = "test-topic"
        };

        // Act & Assert
        Assert.Throws<JsonReaderException>(() => kafkaEvent.GetIEvent());
    }
}