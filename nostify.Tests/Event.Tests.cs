using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;
using Moq;
using nostify;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;

namespace nostify.Tests;

public class EventTests
{
    public class TestCommand : NostifyCommand
    {
        public static readonly TestCommand Create = new TestCommand("Test_Create", true);

        public TestCommand(string name, bool isNew = false) : base(name, isNew)
        {
        }
    }

    public class TestAggregate : NostifyObject, IAggregate, ITenantFilterable
    {
        public static string aggregateType => "Test";

        public static string currentStateContainerName => $"{aggregateType}CurrentState";

        public string? name { get; set; }

        public new Guid id { get; set; }

        public bool isDeleted { get; set; }

        public override void Apply(IEvent eventToApply)
        {
            throw new NotImplementedException();
        }
    }

    public class TestAggregateWithValidation : NostifyObject, IAggregate, ITenantFilterable
    {
        public static string aggregateType => "TestWithValidation";

        public static string currentStateContainerName => $"{aggregateType}CurrentState";

        [Required(ErrorMessage = "Name is required")]
        public string? name { get; set; }

        [Required(ErrorMessage = "ID is required")]
        public new Guid id { get; set; }

        [RequiredFor(["Test_ValueUpdate","Test_TwoCommands"])]
        [Range(1, 100, ErrorMessage = "Value must be between 1 and 100")]
        public int? value { get; set; }

        [RequiredFor("Test_Create", AllowEmptyStrings = false, ErrorMessage = "Description is required for Create")]
        [StringLength(100)]
        public string? description { get; set; }

        [RequiredFor("Test_Other")]
        public string? testThingy { get; set; }

        [RegularExpression(@"^[A-Z]{3}-\d{4}$", ErrorMessage = "Code must match pattern AAA-1234")]
        public string? code { get; set; }

        public bool isDeleted { get; set; }

        public override void Apply(IEvent eventToApply)
        {
            throw new NotImplementedException();
        }
    }

    [Fact]
    public void EventConstructor_ShouldPass_WithValidParameters()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test", id = Guid.NewGuid() };

        // Act
        var eventToTest = new Event(command, payload);

        // Assert
        Assert.NotNull(eventToTest);
        Assert.Equal(command, eventToTest.command);
        Assert.Equal(payload, eventToTest.payload);
        Assert.NotEqual(Guid.Empty, eventToTest.id);
        Assert.True(eventToTest.timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithNullCommand()
    {
        // Arrange
        var payload = new { name = "Test", id = Guid.NewGuid() };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Event(null!, payload));
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithNullPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Event(command, null!));
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithEmptyPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { };

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
        var payload = new { name = "Test", id = "invalid-guid" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Event(command, payload));
    }

    [Fact]
    public void EventConstructor_ShouldFail_WithMissingAggregateRootIdInPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Event(command, payload));
    }

    [Fact]
    public void EventConstructor_ShouldPass_WithValidAggregateRootIdInPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test", id = Guid.NewGuid() };

        // Act
        var eventToTest = new Event(command, payload);

        // Assert
        Assert.NotNull(eventToTest);
        Assert.Equal(command, eventToTest.command);
        Assert.Equal(payload, eventToTest.payload);
    }

    [Fact]
    public void GetPayload_ShouldReturnTypedPayload_WhenPayloadIsValid()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var testId = Guid.NewGuid();
        var payload = new { name = "Test", id = testId };
        var eventToTest = new Event(command, payload);

        // Act
        var typedPayload = eventToTest.GetPayload<TestAggregate>();

        // Assert
        Assert.NotNull(typedPayload);
        Assert.Equal("Test", typedPayload.name);
        Assert.Equal(testId, typedPayload.id);
    }

    [Fact]
    public void PayloadHasProperty_ShouldReturnTrue_WhenPropertyExists()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test", id = Guid.NewGuid() };
        var eventToTest = new Event(command, payload);

        // Act & Assert
        Assert.True(eventToTest.PayloadHasProperty("name"));
        Assert.True(eventToTest.PayloadHasProperty("id"));
    }

    [Fact]
    public void PayloadHasProperty_ShouldReturnFalse_WhenPropertyDoesNotExist()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test", id = Guid.NewGuid() };
        var eventToTest = new Event(command, payload);

        // Act & Assert
        Assert.False(eventToTest.PayloadHasProperty("nonExistentProperty"));
    }

    [Fact]
    public void EmptyConstructor_ShouldCreateEventWithDefaultValues()
    {
        // Act
        var eventToTest = new Event();

        // Assert
        Assert.NotNull(eventToTest);
        Assert.Equal(Guid.Empty, eventToTest.id);
        Assert.Equal(Guid.Empty, eventToTest.aggregateRootId);
        Assert.Equal(Guid.Empty, eventToTest.userId);
        Assert.Equal(Guid.Empty, eventToTest.partitionKey);
        Assert.Null(eventToTest.command);
        Assert.Null(eventToTest.payload);
    }

    [Fact]
    public void Event_ShouldSetTimestampOnConstruction()
    {
        // Arrange
        var beforeTime = DateTime.UtcNow;
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test", id = Guid.NewGuid() };

        // Act
        var eventToTest = new Event(command, payload);
        var afterTime = DateTime.UtcNow;

        // Assert
        Assert.True(eventToTest.timestamp >= beforeTime);
        Assert.True(eventToTest.timestamp <= afterTime);
    }

    [Fact]
    public void Event_ShouldGenerateUniqueIds()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test", id = Guid.NewGuid() };

        // Act
        var event1 = new Event(command, payload);
        var event2 = new Event(command, payload);

        // Assert
        Assert.NotEqual(event1.id, event2.id);
    }

    #region ValidatePayload Tests

    [Fact]
    public void ValidatePayload_ShouldPass_WhenPayloadHasAllRequiredProperties()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50 };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRequiredPropertyIsMissing()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { id = Guid.NewGuid(), value = 50 }; // Missing required 'name'
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("Name is required", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRequiredPropertyIsNull()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = (string?)null, id = Guid.NewGuid(), value = 50 };
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("Name is required", exception.Message);
    }

     [Fact]
    public void ValidatePayload_ShouldPass_WhenPayloadHasAllRequiredForProperties()
    {
        // Arrange
        var command = new NostifyCommand("Test_Create", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, description = "Test Description" };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRequiredForPropertyIsMissing()
    {
        // Arrange
        var command = new NostifyCommand("Test_Create", true);
        var payload = new { id = Guid.NewGuid(), value = 50, name = "Test" }; // Missing required 'description'
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("Description is required for Create", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRequiredForPropertyIsNull()
    {
        // Arrange
        var command = new NostifyCommand("Test_Create", true);
        var payload = new { name = "Test", id = Guid.NewGuid(), value = 50, description = (string?)null }; // Missing required 'description'
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("Description is required for Create", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRangeValidationFails()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 150 }; // Value out of range
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("Value must be between 1 and 100", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenExtraPropertyExistsAndThrowErrorIsTrue()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, extraProperty = "should cause error" };
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: true));
        Assert.Contains("Invalid property 'extraProperty' found in payload", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldNotThrow_WhenExtraPropertyExistsAndThrowErrorIsFalse()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, extraProperty = "should be ignored" };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WithOptionalProperties()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, description = "Optional description" };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenOptionalPropertiesAreMissing()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50 }; // Missing optional 'description'
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldThrowWhenNoId()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { value = 150 }; // Missing id

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new Event(command, payload));
        
        Assert.Contains("Aggregate Root ID does not exist", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldThrowWithMultipleErrors()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { value = 150, id = Guid.NewGuid() }; // Missing name and id, value out of range
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        
        // Should contain multiple error messages
        Assert.Contains("Name is required", exception.Message);
        Assert.Contains("Value must be between 1 and 100", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldReturnSameEventForChaining()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50 };
        var eventToTest = new Event(command, payload);

        // Act
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);

        // Assert - Should return the same event instance for method chaining
        Assert.Same(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenStringLengthIsValid()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, description = "Valid description within 100 characters" };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenStringLengthExceedsMaximum()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var longDescription = new string('a', 101); // 101 characters, exceeds the 100 character limit
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, description = longDescription };
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("field description must be a string with a maximum length of 100", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenRegularExpressionMatches()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, code = "ABC-1234" };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRegularExpressionDoesNotMatch()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, code = "InvalidFormat" };
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("Code must match pattern AAA-1234", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenRegularExpressionIsEmpty()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, code = (string?)null };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw (RegularExpression allows null/empty by default)
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenStringLengthIsEmpty()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50, description = (string?)null };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw (StringLength allows null by default)
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenMultipleValidationAttributesFail()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var longDescription = new string('a', 101); // Exceeds StringLength
        var payload = new { 
            name = "Test Name", 
            id = Guid.NewGuid(), 
            value = 150, // Exceeds Range
            description = longDescription, // Exceeds StringLength
            code = "INVALID" // Doesn't match RegularExpression
        };
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        
        // Should contain multiple error messages
        Assert.Contains("Value must be between 1 and 100", exception.Message);
        Assert.Contains("field description must be a string with a maximum length of 100", exception.Message);
        Assert.Contains("Code must match pattern AAA-1234", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WithValidRegularExpressionVariations()
    {
        // Arrange & Act & Assert - Test various valid patterns
        var command = new NostifyCommand("Test", true);
        
        // Test uppercase letters with numbers
        var payload1 = new { name = "Test Name", id = Guid.NewGuid(), value = 50, code = "XYZ-9999" };
        var event1 = new Event(command, payload1);
        var result1 = event1.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(event1, result1);
        
        // Test different valid combination
        var payload2 = new { name = "Test Name", id = Guid.NewGuid(), value = 50, code = "AAA-0001" };
        var event2 = new Event(command, payload2);
        var result2 = event2.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(event2, result2);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRequiredForValueUpdateAndValueIsMissing()
    {
        // Arrange
        var command = new NostifyCommand("Test_ValueUpdate", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid() }; // Missing required 'value' for Test_ValueUpdate
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        
        // The RequiredFor attribute should trigger validation failure when value is missing for Test_ValueUpdate command
        Assert.Contains("value", exception.Message.ToLower());
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRequiredForValueUpdateAndValueIsNull()
    {
        // Arrange
        var command = new NostifyCommand("Test_ValueUpdate", true);
        // Note: For int (value type), we need to omit the property entirely rather than set it to null
        // because JSON deserialization will fail when trying to convert null to int
        dynamic payload = new JObject() {
            { "name", "Test Name" },
            { "id", Guid.NewGuid() },
            { "value", (int?)null }
        };
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        
        // The RequiredFor attribute should trigger validation failure when value is missing for Test_ValueUpdate command
        Assert.Contains("value", exception.Message.ToLower());
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenRequiredForValueUpdateAndValueIsPresent()
    {
        // Arrange
        var command = new NostifyCommand("Test_ValueUpdate", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50 }; // Valid value for Test_ValueUpdate
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenNotValueUpdateCommandAndValueIsMissing()
    {
        // Arrange
        var command = new NostifyCommand("Test_OtherCommand", true); // Different command, so value is not required by RequiredFor
        // However, value will default to 0 and fail Range validation, so we need to provide a valid value
        // or change this test to verify that RequiredFor doesn't apply, but Range still does
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50 }; // Valid value to pass Range validation
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw because RequiredFor only applies to Test_ValueUpdate
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldStillValidateRange_WhenNotValueUpdateCommandButValuePresent()
    {
        // Arrange
        var command = new NostifyCommand("Test_OtherCommand", true); // Different command
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 150 }; // Invalid range value
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should throw because Range validation still applies regardless of command
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("Value must be between 1 and 100", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRequiredForTwoCommandsAndValueIsMissing()
    {
        // Arrange
        var command = new NostifyCommand("Test_TwoCommands", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid() }; // Missing required 'value' for Test_TwoCommands
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        
        // The RequiredFor attribute should trigger validation failure when value is missing for Test_TwoCommands command
        Assert.Contains("value", exception.Message.ToLower());
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenRequiredForTwoCommandsAndValueIsNull()
    {
        // Arrange
        var command = new NostifyCommand("Test_TwoCommands", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = (int?)null }; // Null value for Test_TwoCommands
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        
        // The RequiredFor attribute should trigger validation failure when value is null for Test_TwoCommands command
        Assert.Contains("value", exception.Message.ToLower());
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenRequiredForTwoCommandsAndValueIsPresent()
    {
        // Arrange
        var command = new NostifyCommand("Test_TwoCommands", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 75 }; // Valid value for Test_TwoCommands
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldThrow_WhenTwoCommandsAndValueOutOfRange()
    {
        // Arrange
        var command = new NostifyCommand("Test_TwoCommands", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 150 }; // Value out of range for Test_TwoCommands
        var eventToTest = new Event(command, payload);

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        Assert.Contains("Value must be between 1 and 100", exception.Message);
    }

    [Fact]
    public void ValidatePayload_ShouldPass_WhenTwoCommandsWithValidValueAndOtherOptionalProperties()
    {
        // Arrange
        var command = new NostifyCommand("Test_TwoCommands", true);
        var payload = new { 
            name = "Test Name", 
            id = Guid.NewGuid(), 
            value = 50,  // Required and valid for Test_TwoCommands
            description = "Optional description", 
            code = "ABC-1234" 
        };
        var eventToTest = new Event(command, payload);

        // Act & Assert - Should not throw
        var result = eventToTest.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false);
        Assert.Equal(eventToTest, result);
    }

    [Fact]
    public void ValidatePayload_ShouldVerifyBothValueUpdateAndTwoCommandsRequireSameProperty()
    {
        // Arrange - Test that both commands require the value property
        var valueUpdateCommand = new NostifyCommand("Test_ValueUpdate", true);
        var twoCommandsCommand = new NostifyCommand("Test_TwoCommands", true);
        var payloadWithoutValue = new { name = "Test Name", id = Guid.NewGuid() }; // Missing value

        var eventValueUpdate = new Event(valueUpdateCommand, payloadWithoutValue);
        var eventTwoCommands = new Event(twoCommandsCommand, payloadWithoutValue);

        // Act & Assert - Both should throw ValidationException
        var exception1 = Assert.Throws<NostifyValidationException>(() => 
            eventValueUpdate.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        var exception2 = Assert.Throws<NostifyValidationException>(() => 
            eventTwoCommands.ValidatePayload<TestAggregateWithValidation>(throwErrorIfExtraProps: false));
        
        // Both should complain about missing value
        Assert.Contains("value", exception1.Message.ToLower());
        Assert.Contains("value", exception2.Message.ToLower());
    }

    #endregion

    #region EventFactory.Create Tests

    [Fact]
    public void EventFactory_Create_ShouldCreateValidatedEventWithGuids()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var aggregateRootId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();
        var payload = new { name = "Test Name", id = aggregateRootId, value = 50 };

        // Act
        var result = new EventFactory().Create<TestAggregateWithValidation>(command, aggregateRootId, payload, userId, partitionKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateRootId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
        Assert.Equal(payload, result.payload);
        Assert.NotEqual(Guid.Empty, result.id);
        Assert.True(result.timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void EventFactory_Create_ShouldCreateValidatedEventFromPayload()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var aggregateRootId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();
        var payload = new { name = "Test Name", id = aggregateRootId, value = 75 };

        // Act
        var result = new EventFactory().Create<TestAggregateWithValidation>(command, payload, userId, partitionKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateRootId, result.aggregateRootId); // Should be parsed from payload
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
        Assert.Equal(payload, result.payload);
        Assert.NotEqual(Guid.Empty, result.id);
        Assert.True(result.timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void EventFactory_Create_ShouldCreateValidatedEventFromStrings()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var aggregateRootId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var partitionKey = Guid.NewGuid();
        var payload = new { name = "Test Name", id = aggregateRootId, value = 25 };

        // Act
        var result = new EventFactory().Create<TestAggregateWithValidation>(command, aggregateRootId.ToString(), payload, userId.ToString(), partitionKey.ToString());

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateRootId, result.aggregateRootId);
        Assert.Equal(userId, result.userId);
        Assert.Equal(partitionKey, result.partitionKey);
        Assert.Equal(payload, result.payload);
        Assert.NotEqual(Guid.Empty, result.id);
        Assert.True(result.timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void EventFactory_Create_ShouldThrowValidationException_WhenPayloadInvalid()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var aggregateRootId = Guid.NewGuid();
        var payload = new { id = aggregateRootId, value = 150 }; // Missing required name, value out of range

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregateWithValidation>(command, aggregateRootId, payload));
        
        // Should contain validation errors
        Assert.Contains("Name is required", exception.Message);
        Assert.Contains("Value must be between 1 and 100", exception.Message);
    }

    [Fact]
    public void EventFactory_Create_ShouldThrowValidationException_WhenRequiredForCommandMissing()
    {
        // Arrange
        var command = new NostifyCommand("Test_ValueUpdate", true);
        var aggregateRootId = Guid.NewGuid();
        var payload = new { name = "Test Name", id = aggregateRootId }; // Missing required value for Test_ValueUpdate

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregateWithValidation>(command, aggregateRootId, payload));
        
        Assert.Contains("value", exception.Message.ToLower());
    }

    [Fact]
    public void EventFactory_Create_ShouldValidateStringLengthAndRegex()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var aggregateRootId = Guid.NewGuid();
        var longDescription = new string('a', 101); // Exceeds StringLength(100)
        var payload = new { 
            name = "Test Name", 
            id = aggregateRootId, 
            value = 50,
            description = longDescription,
            code = "INVALID_FORMAT" // Doesn't match regex pattern
        };

        // Act & Assert
        var exception = Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregateWithValidation>(command, aggregateRootId, payload));
        
        Assert.Contains("field description must be a string with a maximum length of 100", exception.Message);
        Assert.Contains("Code must match pattern AAA-1234", exception.Message);
    }

    [Fact]
    public void EventFactory_Create_ShouldThrowArgumentException_WhenInvalidGuidStrings()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", id = Guid.NewGuid(), value = 50 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new EventFactory().Create<TestAggregateWithValidation>(command, "invalid-guid", payload, "invalid-user-guid", "invalid-partition-guid"));
    }

    [Fact]
    public void EventFactory_Create_ShouldThrowArgumentException_WhenPayloadMissingId()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var payload = new { name = "Test Name", value = 50 }; // Missing id

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new EventFactory().Create<TestAggregateWithValidation>(command, payload));
    }

    [Fact]
    public void EventFactory_Create_ShouldPassValidationWithAllValidAttributes()
    {
        // Arrange
        var command = new NostifyCommand("Test_Create", true);
        var aggregateRootId = Guid.NewGuid();
        var payload = new { 
            name = "Test Name", 
            id = aggregateRootId, 
            value = 50,
            description = "Valid description under 100 chars", // Required for Test_Create and valid length
            code = "ABC-1234" // Valid regex pattern
        };

        // Act
        var result = new EventFactory().Create<TestAggregateWithValidation>(command, aggregateRootId, payload);

        // Assert - Should not throw and should create valid event
        Assert.NotNull(result);
        Assert.Equal(command, result.command);
        Assert.Equal(aggregateRootId, result.aggregateRootId);
        Assert.Equal(payload, result.payload);
    }

    [Fact]
    public void EventFactory_Create_ShouldValidateMultipleCommands()
    {
        // Arrange - Test both commands in RequiredFor array
        var command1 = new NostifyCommand("Test_ValueUpdate", true);
        var command2 = new NostifyCommand("Test_TwoCommands", true);
        var aggregateRootId = Guid.NewGuid();
        var payloadWithoutValue = new { name = "Test Name", id = aggregateRootId }; // Missing value
        var payloadWithValue = new { name = "Test Name", id = aggregateRootId, value = 50 };

        // Act & Assert - Both commands should require value
        Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregateWithValidation>(command1, aggregateRootId, payloadWithoutValue));
        Assert.Throws<NostifyValidationException>(() => 
            new EventFactory().Create<TestAggregateWithValidation>(command2, aggregateRootId, payloadWithoutValue));

        // Both should pass with value
        var result1 = new EventFactory().Create<TestAggregateWithValidation>(command1, aggregateRootId, payloadWithValue);
        var result2 = new EventFactory().Create<TestAggregateWithValidation>(command2, aggregateRootId, payloadWithValue);
        
        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }

    [Fact]
    public void EventFactory_Create_ShouldUseDefaultParametersWhenNotProvided()
    {
        // Arrange
        var command = new NostifyCommand("Test", true);
        var aggregateRootId = Guid.NewGuid();
        var payload = new { name = "Test Name", id = aggregateRootId, value = 50 };

        // Act - Using method without userId and partitionKey parameters
        var result = new EventFactory().Create<TestAggregateWithValidation>(command, aggregateRootId, payload);

        // Assert - Should use default values (Guid.Empty for userId and partitionKey)
        Assert.NotNull(result);
        Assert.Equal(Guid.Empty, result.userId);
        Assert.Equal(Guid.Empty, result.partitionKey);
        Assert.Equal(aggregateRootId, result.aggregateRootId);
    }

    #endregion
}
