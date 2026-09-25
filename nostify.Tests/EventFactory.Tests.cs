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

        protected override void Apply(EventType eventType, IEvent eventToApply)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Event metadata used to exercise the supported EventType-based factory API.
    /// </summary>
    public class TestCommand : EventType
    {
        public static readonly TestCommand Create = new TestCommand("Test_Create", true);
        public TestCommand() : base("Test_Create", true) { }
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
        Assert.Equal(command, result.eventType);
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
        Assert.Equal(command, result.eventType);
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
        Assert.Equal(command, result.eventType);
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
        Assert.Equal(command, result.eventType);
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
        Assert.Equal(command, result.eventType);
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
        Assert.Equal(command, result.eventType);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
        Assert.NotNull(result.payload);
    }

    [Fact]
    public void CreateNullPayloadEvent_ShouldNotMutateValidationSetting()
    {
        // Arrange
        var factory = new EventFactory();
        var command = new TestCommand("test-command");
        var aggregateId = Guid.NewGuid();

        // Verify factory starts with validation enabled
        Assert.True(factory.ValidatePayload);

        // Act
        var result = factory.CreateNullPayloadEvent(command, aggregateId);

        // Assert - Creating a null-payload event should not mutate factory-wide validation settings
        Assert.True(factory.ValidatePayload);
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

    [Fact]
    public void Create_WithEventTypeAndPayloadId_UsesModernOverload()
    {
        // Statically type the metadata as EventType to exercise the supported API.
        EventType eventType = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();
        var payload = new { id = aggregateId, name = "Modern event" };

        IEvent result = new EventFactory().Create<TestAggregate>(
            eventType,
            payload,
            userId,
            partitionKey);

        Assert.Equal(eventType, result.eventType);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
    }

    [Fact]
    public void Create_WithEventTypeAndStringIds_UsesModernOverload()
    {
        // EventType static typing exercises the supported API contract.
        EventType eventType = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();
        var payload = new { id = aggregateId, name = "Modern string event" };

        IEvent result = new EventFactory().Create<TestAggregate>(
            eventType,
            aggregateId.ToString(),
            payload,
            userId.ToString(),
            partitionKey.ToString());

        Assert.Equal(eventType, result.eventType);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
    }

    [Fact]
    public void CreateNullPayloadEvent_WithEventTypeAndStringIds_UsesModernOverload()
    {
        // Verify the modern string overload parses all identifiers and supplies an empty payload.
        EventType eventType = TestCommand.Create;
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();

        IEvent result = new EventFactory().CreateNullPayloadEvent(
            eventType,
            aggregateId.ToString(),
            userId.ToString(),
            partitionKey.ToString());

        Assert.Equal(eventType, result.eventType);
        Assert.Equal(aggregateId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
        Assert.NotNull(result.payload);
    }
}
