using System;
using System.ComponentModel.DataAnnotations;
using Xunit;
using nostify;

namespace nostify.Tests;

public class EventBuilderTests
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
        public TestCommand(string name, bool isNew = false) : base(name, isNew) { }
    }

    [Fact]
    public void Create_WithValidateTrue_ShouldValidatePayload()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => 
            EventBuilder.Create<TestAggregate>(command, aggregateId, invalidPayload, validate: true));
        
        Assert.Contains("Name is required", exception.Message);
    }

    [Fact]
    public void Create_WithValidateFalse_ShouldNotValidatePayload()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act - Should not throw even with invalid payload
        var result = EventBuilder.Create<TestAggregate>(command, aggregateId, invalidPayload, validate: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
    }

    [Fact]
    public void Create_DefaultValidateParameter_ShouldValidateByDefault()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act & Assert - Default behavior should validate
        var exception = Assert.Throws<ValidationException>(() => 
            EventBuilder.Create<TestAggregate>(command, aggregateId, invalidPayload));
        
        Assert.Contains("Name is required", exception.Message);
    }

    [Fact]
    public void Create_WithPayloadParsing_ValidateTrue_ShouldValidatePayload()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => 
            EventBuilder.Create<TestAggregate>(command, invalidPayload, validate: true));
        
        Assert.Contains("Name is required", exception.Message);
    }

    [Fact]
    public void Create_WithPayloadParsing_ValidateFalse_ShouldNotValidatePayload()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act - Should not throw even with invalid payload
        var result = EventBuilder.Create<TestAggregate>(command, invalidPayload, validate: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
    }

    [Fact]
    public void Create_WithStringParameters_ValidateTrue_ShouldValidatePayload()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        var partitionKey = Guid.NewGuid().ToString();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => 
            EventBuilder.Create<TestAggregate>(command, aggregateId, invalidPayload, userId, partitionKey, validate: true));
        
        Assert.Contains("Name is required", exception.Message);
    }

    [Fact]
    public void Create_WithStringParameters_ValidateFalse_ShouldNotValidatePayload()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        var partitionKey = Guid.NewGuid().ToString();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act - Should not throw even with invalid payload
        var result = EventBuilder.Create<TestAggregate>(command, aggregateId, invalidPayload, userId, partitionKey, validate: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(Guid.Parse(aggregateId), result.aggregateRootId);
    }
}
