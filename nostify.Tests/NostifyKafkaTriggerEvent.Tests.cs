using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using nostify;
using Xunit;

namespace nostify.Tests;

public class NostifyKafkaTriggerEventTests
{
    private NostifyKafkaTriggerEvent CreateKafkaEvent(string commandName)
    {
        var originalEvent = new Event
        {
            aggregateRootId = Guid.NewGuid(),
            command = new NostifyCommand(commandName),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = Guid.NewGuid()
        };

        var jsonValue = JsonConvert.SerializeObject(originalEvent, SerializationSettings.NostifyDefault);
        return new NostifyKafkaTriggerEvent
        {
            Value = jsonValue,
            Offset = 1,
            Partition = 0,
            Topic = "test-topic",
            Key = "test-key",
            Headers = new[] { "header1", "header2" }
        };
    }

    #region Basic GetEvent Tests

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

    #endregion

    #region GetEvent with Single String Filter Tests

    [Fact]
    public void GetEvent_WithMatchingSingleFilter_ReturnsEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");

        // Act
        var result = kafkaEvent.GetEvent("Create_Product");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Product", result.command.name);
    }

    [Fact]
    public void GetEvent_WithNonMatchingSingleFilter_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");

        // Act
        var result = kafkaEvent.GetEvent("Update_Product");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetEvent_WithNullFilter_ReturnsEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");

        // Act
        var result = kafkaEvent.GetEvent(eventTypeFilter: null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Product", result.command.name);
    }

    [Fact]
    public void GetEvent_WithEmptyStringFilter_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");

        // Act
        var result = kafkaEvent.GetEvent("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetEvent_FilterIsCaseSensitive_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");

        // Act
        var result = kafkaEvent.GetEvent("create_product");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetEvent with List Filter Tests

    [Fact]
    public void GetEvent_WithMatchingListFilter_ReturnsEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Update_Product");
        var filters = new List<string> { "Create_Product", "Update_Product", "Delete_Product" };

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Update_Product", result.command.name);
    }

    [Fact]
    public void GetEvent_WithNonMatchingListFilter_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Archive_Product");
        var filters = new List<string> { "Create_Product", "Update_Product", "Delete_Product" };

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetEvent_WithEmptyListFilter_ReturnsEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");
        var filters = new List<string>();

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Product", result.command.name);
    }

    [Fact]
    public void GetEvent_WithSingleItemListFilter_Matching_ReturnsEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");
        var filters = new List<string> { "Create_Product" };

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Product", result.command.name);
    }

    [Fact]
    public void GetEvent_WithSingleItemListFilter_NonMatching_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Update_Product");
        var filters = new List<string> { "Create_Product" };

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetEvent_WithMultipleFilters_FirstMatches_ReturnsEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");
        var filters = new List<string> { "Create_Product", "Update_Product" };

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetEvent_WithMultipleFilters_LastMatches_ReturnsEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Delete_Product");
        var filters = new List<string> { "Create_Product", "Update_Product", "Delete_Product" };

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetEvent_WithMultipleFilters_MiddleMatches_ReturnsEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Update_Product");
        var filters = new List<string> { "Create_Product", "Update_Product", "Delete_Product" };

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetEvent_WithDuplicateFilters_StillWorks()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");
        var filters = new List<string> { "Create_Product", "Create_Product", "Update_Product" };

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Product", result.command.name);
    }

    #endregion

    #region GetIEvent Basic Tests

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
    public void GetIEvent_WithNullValue_ReturnsNull()
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
        var result = kafkaEvent.GetIEvent();

        // Assert
        Assert.Null(result);
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

    #endregion

    #region GetIEvent with Single String Filter Tests

    [Fact]
    public void GetIEvent_WithMatchingSingleFilter_ReturnsIEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Order");

        // Act
        var result = kafkaEvent.GetIEvent("Create_Order");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Order", result.command.name);
    }

    [Fact]
    public void GetIEvent_WithNonMatchingSingleFilter_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Order");

        // Act
        var result = kafkaEvent.GetIEvent("Update_Order");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetIEvent_WithNullFilter_ReturnsIEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Order");

        // Act
        var result = kafkaEvent.GetIEvent(eventTypeFilter: null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Order", result.command.name);
    }

    [Fact]
    public void GetIEvent_WithEmptyStringFilter_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Order");

        // Act
        var result = kafkaEvent.GetIEvent("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetIEvent_FilterIsCaseSensitive_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Order");

        // Act
        var result = kafkaEvent.GetIEvent("create_order");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetIEvent with List Filter Tests

    [Fact]
    public void GetIEvent_WithMatchingListFilter_ReturnsIEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Update_Order");
        var filters = new List<string> { "Create_Order", "Update_Order", "Delete_Order" };

        // Act
        var result = kafkaEvent.GetIEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Update_Order", result.command.name);
    }

    [Fact]
    public void GetIEvent_WithNonMatchingListFilter_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Cancel_Order");
        var filters = new List<string> { "Create_Order", "Update_Order", "Delete_Order" };

        // Act
        var result = kafkaEvent.GetIEvent(filters);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetIEvent_WithEmptyListFilter_ReturnsIEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Order");
        var filters = new List<string>();

        // Act
        var result = kafkaEvent.GetIEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Order", result.command.name);
    }

    [Fact]
    public void GetIEvent_WithSingleItemListFilter_Matching_ReturnsIEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Order");
        var filters = new List<string> { "Create_Order" };

        // Act
        var result = kafkaEvent.GetIEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Create_Order", result.command.name);
    }

    [Fact]
    public void GetIEvent_WithSingleItemListFilter_NonMatching_ReturnsNull()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Update_Order");
        var filters = new List<string> { "Create_Order" };

        // Act
        var result = kafkaEvent.GetIEvent(filters);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetIEvent_WithMultipleFilters_AnyMatches_ReturnsIEvent()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Delete_Order");
        var filters = new List<string> { "Create_Order", "Update_Order", "Delete_Order" };

        // Act
        var result = kafkaEvent.GetIEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Delete_Order", result.command.name);
    }

    #endregion

    #region Edge Cases and Special Scenarios

    [Fact]
    public void GetEvent_WithWhitespaceFilter_DoesNotMatchTrimmed()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product");

        // Act
        var result = kafkaEvent.GetEvent(" Create_Product ");

        // Assert - whitespace matters
        Assert.Null(result);
    }

    [Fact]
    public void GetEvent_WithSpecialCharactersInCommandName_FiltersCorrectly()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Create_Product-v2");

        // Act
        var resultMatching = kafkaEvent.GetEvent("Create_Product-v2");
        var resultNonMatching = kafkaEvent.GetEvent("Create_Product");

        // Assert
        Assert.NotNull(resultMatching);
        Assert.Null(resultNonMatching);
    }

    [Fact]
    public void GetEvent_WithVeryLongFilterList_WorksCorrectly()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Target_Command");
        var filters = Enumerable.Range(1, 100)
            .Select(i => $"Command_{i}")
            .Append("Target_Command")
            .ToList();

        // Act
        var result = kafkaEvent.GetEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Target_Command", result.command.name);
    }

    [Fact]
    public void GetIEvent_WithVeryLongFilterList_WorksCorrectly()
    {
        // Arrange
        var kafkaEvent = CreateKafkaEvent("Target_Command");
        var filters = Enumerable.Range(1, 100)
            .Select(i => $"Command_{i}")
            .Append("Target_Command")
            .ToList();

        // Act
        var result = kafkaEvent.GetIEvent(filters);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Target_Command", result.command.name);
    }

    [Fact]
    public void Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var kafkaEvent = new NostifyKafkaTriggerEvent
        {
            Value = "test-value",
            Offset = 42,
            Partition = 3,
            Topic = "my-topic",
            Key = "my-key",
            Headers = new[] { "header1", "header2", "header3" }
        };

        // Assert
        Assert.Equal("test-value", kafkaEvent.Value);
        Assert.Equal(42, kafkaEvent.Offset);
        Assert.Equal(3, kafkaEvent.Partition);
        Assert.Equal("my-topic", kafkaEvent.Topic);
        Assert.Equal("my-key", kafkaEvent.Key);
        Assert.Equal(3, kafkaEvent.Headers.Length);
        Assert.Equal("header1", kafkaEvent.Headers[0]);
    }

    [Fact]
    public void Constructor_CreatesEmptyInstance()
    {
        // Act
        var kafkaEvent = new NostifyKafkaTriggerEvent();

        // Assert
        Assert.NotNull(kafkaEvent);
        Assert.Null(kafkaEvent.Value);
        Assert.Null(kafkaEvent.Topic);
        Assert.Null(kafkaEvent.Key);
        Assert.Null(kafkaEvent.Headers);
    }

    [Fact]
    public void GetEvent_WithComplexEventPayload_PreservesData()
    {
        // Arrange
        var originalEvent = new Event
        {
            aggregateRootId = Guid.NewGuid(),
            command = new NostifyCommand("Create_ComplexObject"),
            timestamp = DateTime.UtcNow,
            userId = Guid.NewGuid(),
            partitionKey = Guid.NewGuid(),
            payload = new { name = "Test", value = 123, nested = new { prop = "data" } }
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
        var result = kafkaEvent.GetEvent("Create_ComplexObject");

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.payload);
        Assert.Equal("Create_ComplexObject", result.command.name);
    }

    #endregion
}