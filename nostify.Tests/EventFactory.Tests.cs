using System;
using System.ComponentModel.DataAnnotations;
using Xunit;
using nostify;

namespace nostify.Tests;

public class EventFactoryTests
{
    public class TestAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => "TestAggregate";
        public static string currentStateContainerName => $"{aggregateType}CurrentState";

        [Required(ErrorMessage = "Name is required")]
        public string? name { get; set; }

        public new Guid id { get; set; }
        public bool isDeleted { get; set; }

        public override void Apply(Event eventToApply)
        {
            throw new NotImplementedException();
        }
    }

    public class TestCommand : NostifyCommand
    {
        public static readonly TestCommand Create = new TestCommand("Test_Create", true);
        public static readonly TestCommand Update = new TestCommand("Test_Update", false);
        public static readonly TestCommand Delete = new TestCommand("Test_Delete", false);
        public TestCommand(string name, bool isNew = false) : base(name, isNew) { }
    }

    [Fact]
    public void Create_WithValidPayload_ShouldValidateAndReturnEvent()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var validPayload = new { id = aggregateId, name = "Test Item" };

        // Act
        var result = EventFactory.Create<TestAggregate>(command, aggregateId, validPayload);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(validPayload, result.payload);
    }

    [Fact]
    public void Create_WithInvalidPayload_ShouldThrowValidationException()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => 
            EventFactory.Create<TestAggregate>(command, aggregateId, invalidPayload));
        
        Assert.Contains("Name is required", exception.Message);
    }

    [Fact]
    public void Create_WithPayloadParsing_ShouldValidateAndReturnEvent()
    {
        // Arrange
        var command = TestCommand.Update;
        var aggregateId = Guid.NewGuid();
        var validPayload = new { id = aggregateId, name = "Test Item" };

        // Act
        var result = EventFactory.Create<TestAggregate>(command, validPayload);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(validPayload, result.payload);
    }

    [Fact]
    public void Create_WithStringParameters_ShouldValidateAndReturnEvent()
    {
        // Arrange
        var command = TestCommand.Update;
        var aggregateId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        var partitionKey = Guid.NewGuid().ToString();
        var validPayload = new { id = aggregateId, name = "Test Item" };

        // Act
        var result = EventFactory.Create<TestAggregate>(command, aggregateId, validPayload, userId, partitionKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(Guid.Parse(aggregateId), result.aggregateRootId);
        Assert.Equal(Guid.Parse(userId), result.userId);
        Assert.Equal(Guid.Parse(partitionKey), result.partitionKey);
        Assert.Equal(validPayload, result.payload);
    }

    [Fact]
    public void CreateNullPayloadEvent_ShouldReturnEventWithNullPayload()
    {
        // Arrange
        var command = TestCommand.Delete;
        var aggregateId = Guid.NewGuid();

        // Act
        var result = EventFactory.CreateNullPayloadEvent(command, aggregateId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Null(result.payload);
        Assert.Equal(default(Guid), result.userId);
        Assert.Equal(default(Guid), result.partitionKey);
    }

    [Fact]
    public void CreateNullPayloadEvent_WithUserIdAndPartitionKey_ShouldReturnEventWithNullPayload()
    {
        // Arrange
        var command = TestCommand.Delete;
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();

        // Act
        var result = EventFactory.CreateNullPayloadEvent(command, aggregateId, userId, partitionKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
        Assert.Null(result.payload);
    }

    [Fact]
    public void CreateNullPayloadEvent_ShouldNotValidatePayload()
    {
        // Arrange - Using a command that would normally require validation
        var command = TestCommand.Create; 
        var aggregateId = Guid.NewGuid();

        // Act - Should not throw even though payload is null and would normally fail validation
        var result = EventFactory.CreateNullPayloadEvent(command, aggregateId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Null(result.payload);
    }

    [Fact]
    public void CreateNullPayloadEvent_WithDefaultParameters_ShouldUseDefaults()
    {
        // Arrange
        var command = TestCommand.Delete;
        var aggregateId = Guid.NewGuid();

        // Act
        var result = EventFactory.CreateNullPayloadEvent(command, aggregateId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(default(Guid), result.userId);
        Assert.Equal(default(Guid), result.partitionKey);
        Assert.Null(result.payload);
    }
}