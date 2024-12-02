using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;
using Moq;
using nostify;
using System.ComponentModel.DataAnnotations;

public class EventTests
{
    public class TestAggregate : NostifyObject, IAggregate, ITenantFilterable
    {
        public static string aggregateType => "Test";

        public static string currentStateContainerName => $"{aggregateType}CurrentState";

        [RequiredForCreate]
        public string name { get; set; }

        [RequiredForCreate(NotEmptyGuid = true)]
        public Guid id { get; set; }

        public bool isDeleted { get; set; }

        public override void Apply(Event eventToApply)
        {
            throw new NotImplementedException();
        }
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenAllRequiredPropertiesArePresent()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test", id = Guid.NewGuid() };
        var eventToTest = new Event { command = command, payload = JObject.FromObject(payload) };

        // Act & Assert
        eventToTest.ValidatePayload<TestAggregate>();
    }

    [Fact]
    public void ValidatePayload_ShouldFail_WhenRequiredPropertiesAreMissing()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { id = Guid.NewGuid() };
        var eventToTest = new Event { command = command, payload = JObject.FromObject(payload) };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => eventToTest.ValidatePayload<TestAggregate>());
        Assert.Contains("name", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldFail_WhenRequiredPropertiesAreNull()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = (string)null, id = Guid.NewGuid() };
        var eventToTest = new Event { command = command, payload = JObject.FromObject(payload) };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => eventToTest.ValidatePayload<TestAggregate>());
        Assert.Contains("name", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldFail_WhenRequiredGuidIsEmpty()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test", id = Guid.Empty };
        var eventToTest = new Event { command = command, payload = JObject.FromObject(payload) };

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => eventToTest.ValidatePayload<TestAggregate>());
        Assert.Contains("id", exception.Message);
    }

    [Fact]
    public void EventConstructor_ShouldPass_WithValidParameters()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = JObject.FromObject(new { name = "Test", id = Guid.NewGuid() });

        // Act
        var eventToTest = new Event(command, payload);

        // Assert
        Assert.NotNull(eventToTest);
        Assert.Equal(command, eventToTest.command);
        Assert.Equal(payload, eventToTest.payload);
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithNullCommand()
    {
        // Arrange
        NostifyCommand command = null;
        var payload = JObject.FromObject(new { name = "Test", id = Guid.NewGuid().ToString() });

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Event(command, payload));
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithNullPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        JObject payload = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Event(command, payload));
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithEmptyPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new JObject();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Event(command, payload));
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithInvalidAggregateRootId()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        object payload = new { name = "Test", id = Guid.NewGuid() };
        string invalidAggregateRootId = "invalid-guid";
        string userId = Guid.NewGuid().ToString();
        string partitionKey = Guid.NewGuid().ToString();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Event(command, invalidAggregateRootId, payload, userId, partitionKey));
    }

    [Fact]
    public void EventConstructor_ShouldPass_WithValidAggregateRootId()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        Guid validAggregateRootId = Guid.NewGuid();
        var payload = new { name = "Test", id = validAggregateRootId };
        var userId = Guid.NewGuid();

        // Act
        var eventToTest = new Event(command, payload, userId);

        // Assert
        Assert.NotNull(eventToTest);
        Assert.Equal(command, eventToTest.command);
        Assert.Equal(payload, eventToTest.payload);
        Assert.Equal(validAggregateRootId, eventToTest.aggregateRootId);
    }
    
    [Fact]
    public void EventConstructor_ShouldFail_WithInvalidUserId()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        object payload = new { name = "Test", id = Guid.NewGuid() };
        string aggregateRootId = Guid.NewGuid().ToString();
        string invalidUserId = "not-a-guid";
        string partitionKey = Guid.NewGuid().ToString();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Event(command, aggregateRootId, payload, invalidUserId, partitionKey));
    }

    [Fact]
    public void EventConstructor_ShouldPass_WithValidUserId()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        Guid validAggregateRootId = Guid.NewGuid();
        var payload = new { name = "Test", id = validAggregateRootId };
        var userId = Guid.NewGuid();

        // Act
        var eventToTest = new Event(command, payload, userId);

        // Assert
        Assert.NotNull(eventToTest);
        Assert.Equal(command, eventToTest.command);
        Assert.Equal(payload, eventToTest.payload);
        Assert.Equal(validAggregateRootId, eventToTest.aggregateRootId);
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithInvalidAggregateRootIdInPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = JObject.FromObject(new { name = "Test", id = "invalid-guid" });
    
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Event(command, payload));
    }
    
    [Fact]
    public void EventConstructor_ShouldFail_WithMissingAggregateRootIdInPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = JObject.FromObject(new { name = "Test" });
    
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Event(command, payload));
    }
    
    [Fact]
    public void EventConstructor_ShouldPass_WithValidAggregateRootIdInPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = JObject.FromObject(new { name = "Test", id = Guid.NewGuid() });
    
        // Act
        var eventToTest = new Event(command, payload);
    
        // Assert
        Assert.NotNull(eventToTest);
        Assert.Equal(command, eventToTest.command);
        Assert.Equal(payload, eventToTest.payload);
    }
}