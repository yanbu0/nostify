using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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

        public override void Apply(IEvent eventToApply)
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
        var exception = Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregate>(command, aggregateId, invalidPayload));
        
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
        var result = new EventFactory().NoValidate().Create<TestAggregate>(command, aggregateId, invalidPayload);

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
        var exception = Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregate>(command, aggregateId, invalidPayload));
        
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
        var exception = Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregate>(command, invalidPayload));
        
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
        var result = new EventFactory().NoValidate().Create<TestAggregate>(command, invalidPayload);

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
        var exception = Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregate>(command, aggregateId, invalidPayload, userId, partitionKey));
        
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
        var result = new EventFactory().NoValidate().Create<TestAggregate>(command, aggregateId, invalidPayload, userId, partitionKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(Guid.Parse(aggregateId), result.aggregateRootId);
    }

    [Fact]
    public void NoValidate_ShouldReturnSameInstance()
    {
        // Arrange
        var factory = new EventFactory();

        // Act
        var result = factory.NoValidate();

        // Assert
        Assert.Same(factory, result);
        Assert.False(factory.ValidatePayload);
    }

    [Fact]
    public void ValidatePayload_ShouldDefaultToTrue()
    {
        // Arrange & Act
        var factory = new EventFactory();

        // Assert
        Assert.True(factory.ValidatePayload);
    }

    [Fact]
    public void NoValidate_ShouldAllowMethodChaining()
    {
        // Arrange
        var command = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var invalidPayload = new { id = aggregateId }; // Missing required 'name'

        // Act - Should not throw due to chained NoValidate()
        var result = new EventFactory()
            .NoValidate()
            .Create<TestAggregate>(command, aggregateId, invalidPayload);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
    }

    [Fact]
    public void EventFactory_SeparateInstances_ShouldHaveIndependentValidationSettings()
    {
        // Arrange
        var factory1 = new EventFactory();
        var factory2 = new EventFactory().NoValidate();

        // Assert
        Assert.True(factory1.ValidatePayload);
        Assert.False(factory2.ValidatePayload);
    }

    [Fact]
    public void CreateNullPayloadEvent_WithValidCommand_ShouldCreateEventWithEmptyPayload()
    {
        // Arrange
        var command = new TestCommand("test-command");
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();

        // Act
        var result = new EventFactory().CreateNullPayloadEvent(command, aggregateId, userId, partitionKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
        Assert.NotNull(result.payload);
    }

    [Fact]
    public void CreateNullPayloadEvent_WithStringParameters_ShouldCreateEventWithEmptyPayload()
    {
        // Arrange
        var command = new TestCommand("test-command");
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();

        // Act
        var result = new EventFactory().CreateNullPayloadEvent(
            command, 
            aggregateId.ToString(), 
            userId.ToString(), 
            partitionKey.ToString()
        );

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
        Assert.NotNull(result.payload);
    }

    [Fact]
    public void CreateNullPayloadEvent_ShouldAutomaticallyDisableValidation()
    {
        // Arrange
        var factory = new EventFactory();
        var command = new TestCommand("test-command");
        var aggregateId = Guid.NewGuid();

        // Verify factory starts with validation enabled
        Assert.True(factory.ValidatePayload);

        // Act
        var result = factory.CreateNullPayloadEvent(command, aggregateId);

        // Assert - Validation should be disabled after calling CreateNullPayloadEvent
        Assert.False(factory.ValidatePayload);
        Assert.NotNull(result);
    }

    [Fact]
    public void CreateNullPayloadEvent_JsonSerialization_ShouldSerializeSuccessfully()
    {
        // Arrange
        var command = new TestCommand("test-command");
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();

        // Act
        var result = new EventFactory().CreateNullPayloadEvent(command, aggregateId, userId, partitionKey);

        // Act - Serialize to JSON using System.Text.Json (as used in Saga tests)
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        // Assert - Should serialize without throwing exceptions
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        
        // Verify JSON contains expected properties
        Assert.Contains($"\"aggregateRootId\":\"{aggregateId}\"", json);
        Assert.Contains($"\"userId\":\"{userId}\"", json);
        Assert.Contains($"\"partitionKey\":\"{partitionKey}\"", json);
        
        // Verify payload is serialized as empty object
        Assert.Contains("\"payload\":{}", json);
    }
}
