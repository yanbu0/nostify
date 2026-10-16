using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Xunit;
using nostify;

namespace nostify.Tests;

public class RequiredForAttributeTests
{
    [Fact]
    public void Constructor_WithSingleEvent_ShouldCreateAttributeWithOneEvent()
    {
        // Arrange
        var eventName = "Test_Create";

        // Act
        var attribute = new RequiredForAttribute(eventName);

        // Assert
        Assert.Single(attribute.Events);
        Assert.Equal(eventName, attribute.Events[0]);
    }

    [Fact]
    public void Constructor_WithEventArray_ShouldCreateAttributeWithAllEvents()
    {
        // Arrange
        var eventNames = new[] { "Test_Create", "Test_Update", "Test_Delete" };

        // Act
        var attribute = new RequiredForAttribute(eventNames);

        // Assert
        Assert.Equal(eventNames, attribute.Events);
    }

    [Fact]
    public void Constructor_WithParamsEvents_ShouldCreateAttributeWithAllEvents()
    {
        // Act
        var attribute = new RequiredForAttribute("Test_Create", "Test_Update", "Test_Delete");

        // Assert
        Assert.Equal(["Test_Create", "Test_Update", "Test_Delete"], attribute.Events);
    }

    [Fact]
    public void Constructor_PreservesPublishedNamedArguments()
    {
        var single = new RequiredForAttribute(command: "Test_Create");
        var multiple = new RequiredForAttribute(commands: ["Test_Create", "Test_Update"]);

        Assert.Equal(["Test_Create"], single.Events);
        Assert.Equal(["Test_Create", "Test_Update"], multiple.Events);
    }

    [Fact]
    public void Constructor_WithEventTypes_ShouldResolveEventNames()
    {
        // Act
        var attribute = new RequiredForAttribute(typeof(CreateTestEvent), typeof(UpdateTestEvent));

        // Assert
        Assert.Equal(["RequiredFor_Create", "RequiredFor_Update"], attribute.Events);
    }

    [Fact]
    public void Attribute_WithEventTypes_ShouldResolveEventNames()
    {
        // Act
        var property = typeof(TypedRequiredForModel).GetProperty(nameof(TypedRequiredForModel.Value))!;
        var attribute = (RequiredForAttribute)Attribute.GetCustomAttribute(
            property,
            typeof(RequiredForAttribute))!;

        // Assert
        Assert.Equal(["RequiredFor_Create", "RequiredFor_Update"], attribute.Events);
    }

    [Fact]
    public void Constructor_WithTypeThatIsNotAnEventType_ShouldThrow()
    {
        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RequiredForAttribute(typeof(string)));

        // Assert
        Assert.Contains(nameof(EventType), exception.Message);
    }

    [Fact]
    public void Constructor_WithAbstractEventType_ShouldThrow()
    {
        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RequiredForAttribute(typeof(AbstractTestEvent)));

        // Assert
        Assert.Contains("concrete", exception.Message);
    }

    [Fact]
    public void Constructor_WithEmptyEventArray_ShouldCreateAttributeWithEmptyList()
    {
        // Arrange
        var eventNames = Array.Empty<string>();

        // Act
        var attribute = new RequiredForAttribute(eventNames);

        // Assert
        Assert.Empty(attribute.Events);
    }

    [Fact]
    public void Constructor_WithNullEvent_ShouldCreateAttributeWithNullInList()
    {
        // Arrange
        string? eventName = null;

        // Act
        var attribute = new RequiredForAttribute(eventName!);

        // Assert
        Assert.Single(attribute.Events);
        Assert.Null(attribute.Events[0]);
    }

    [Fact]
    public void Commands_ShouldReferToEventsForBackwardsCompatibility()
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create", "Test_Update");

        // Assert
#pragma warning disable CS0618 // Verify the compatibility alias before its next-major-version removal.
        Assert.Same(attribute.Events, attribute.Commands);
#pragma warning restore CS0618
    }

    [Fact]
    public void IsValid_WithValidValueAndMatchingCommand_ShouldReturnSuccess()
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create");
        var command = new NostifyCommand("Test_Create");
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;
        var value = "Valid Value";

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithNullValueAndMatchingCommand_ShouldReturnValidationError()
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create");
        var command = new NostifyCommand("Test_Create");
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;
        object? value = null;

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.Contains("TestProperty", result.ErrorMessage);
        Assert.Contains("Test_Create", result.ErrorMessage);
    }

    [Fact]
    public void IsValid_WithEmptyStringAndMatchingCommand_ShouldReturnValidationError()
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create");
        var command = new NostifyCommand("Test_Create");
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;
        var value = "";

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithValidValueAndNonMatchingCommand_ShouldReturnSuccess()
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create");
        var command = new NostifyCommand("Test_Update"); // Different command
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;
        object? value = null; // Even null should be valid for non-matching commands

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithNullCommand_ShouldReturnValidationError()
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create");
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        // No command in validation context
        var value = "Valid Value";

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.Contains("TestProperty", result.ErrorMessage);
        Assert.Contains("event type to be specified", result.ErrorMessage);
    }

    [Fact]
    public void IsValid_WithMultipleCommands_ShouldValidateForAnyMatchingCommand()
    {
        // Arrange
        var commands = new[] { "Test_Create", "Test_Update" };
        var attribute = new RequiredForAttribute(commands);
        var command = new NostifyCommand("Test_Update"); // Matches one of the commands
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;
        object? value = null; // Should fail validation

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
        Assert.Contains("Test_Update", result.ErrorMessage);
    }

    [Fact]
    public void IsValid_WithMultipleCommandsAndNonMatching_ShouldReturnSuccess()
    {
        // Arrange
        var commands = new[] { "Test_Create", "Test_Update" };
        var attribute = new RequiredForAttribute(commands);
        var command = new NostifyCommand("Test_Delete"); // Doesn't match any command
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;
        object? value = null; // Should be valid since command doesn't match

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithCustomErrorMessage_ShouldUseCustomMessage()
    {
        // Arrange
        var customMessage = "Custom validation error message";
        var attribute = new RequiredForAttribute("Test_Create")
        {
            ErrorMessage = customMessage
        };
        var command = new NostifyCommand("Test_Create");
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;
        object? value = null;

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customMessage, result.ErrorMessage);
    }

    [Fact]
    public void IsValid_WithWhitespaceValue_ShouldReturnValidationError()
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create");
        var command = new NostifyCommand("Test_Create");
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;
        var value = "   "; // Whitespace only

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void INostifyValidation_Events_ShouldExposeEventsAndCompatibilityAlias()
    {
        // Arrange
        var eventNames = new[] { "Test_Create", "Test_Update" };
        var attribute = new RequiredForAttribute(eventNames);

        // Act
        INostifyValidation validation = attribute;

        // Assert
        Assert.Equal(eventNames, validation.Events);
#pragma warning disable CS0618 // Verify the compatibility alias before its next-major-version removal.
        Assert.Same(validation.Events, validation.Commands);
#pragma warning restore CS0618
    }

    [Fact]
    public void INostifyValidation_Events_DefaultAlias_ShouldDelegateToCommands()
    {
        INostifyValidation validation = new CommandsOnlyValidation("Test_Create", "Test_Update");

        Assert.Same(validation.Commands, validation.Events);
    }

    [Fact]
    public void Commands_Property_ShouldBeMarkedObsoleteForNextMajorVersionRemoval()
    {
        // Act
        var obsolete = (ObsoleteAttribute)Attribute.GetCustomAttribute(
            typeof(RequiredForAttribute).GetProperty(nameof(RequiredForAttribute.Commands))!,
            typeof(ObsoleteAttribute))!;

        // Assert
        Assert.Contains("next major version", obsolete.Message);
        Assert.Contains(nameof(RequiredForAttribute.Events), obsolete.Message);
    }

    [Fact]
    public void InheritanceFromRequiredAttribute_ShouldMaintainBaseClassBehavior()
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create");

        // Assert
        Assert.IsAssignableFrom<RequiredAttribute>(attribute);
        Assert.IsAssignableFrom<ValidationAttribute>(attribute);
        Assert.IsAssignableFrom<Attribute>(attribute);
        Assert.IsAssignableFrom<INostifyValidation>(attribute);
    }

    [Fact]
    public void AttributeUsage_ShouldBeConfiguredCorrectly()
    {
        // Arrange
        var attributeType = typeof(RequiredForAttribute);

        // Act
        var attributeUsage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(attributeType, typeof(AttributeUsageAttribute))!;

        // Assert
        Assert.NotNull(attributeUsage);
        Assert.Equal(AttributeTargets.Property, attributeUsage.ValidOn);
        Assert.False(attributeUsage.AllowMultiple);
        Assert.False(attributeUsage.Inherited);
    }

    [Fact]
    public void Events_Property_ShouldBeReadOnly()
    {
        // Arrange
        var eventNames = new[] { "Test_Create" };
        var attribute = new RequiredForAttribute(eventNames);

        // Act
        var events = attribute.Events;

        // Assert
        Assert.NotNull(events);
        Assert.False(typeof(RequiredForAttribute).GetProperty(nameof(RequiredForAttribute.Events))!.CanWrite);
        Assert.IsType<List<string>>(events);
    }

    [Fact]
    public void ValidatePayload_WithOmittedRequiredNonNullableGuid_ShouldReturnValidationError()
    {
        // Arrange: deserialization assigns Guid.Empty, but the original payload did not contain the property.
        var evt = new Event(new CreateTestEvent(), Guid.NewGuid(), new { OptionalValue = "present" });

        // Act
        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<NonNullableRequiredForModel>());

        // Assert
        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal(nameof(NonNullableRequiredForModel.RequiredId), Assert.Single(result.MemberNames));
    }

    [Fact]
    public void ValidatePayload_WithExplicitEmptyRequiredGuid_ShouldSucceed()
    {
        // Arrange: RequiredFor enforces payload presence; separate validators may reject Guid.Empty.
        var evt = new Event(new CreateTestEvent(), Guid.NewGuid(), new { RequiredId = Guid.Empty });

        // Act
        IEvent result = evt.ValidatePayload<NonNullableRequiredForModel>();

        // Assert
        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithOmittedRequiredNonNullableInteger_ShouldReturnValidationError()
    {
        // Arrange: verify presence checking is not specific to Guid values.
        var evt = new Event(new CreateTestEvent(), Guid.NewGuid(), new { OptionalValue = "present" });

        // Act
        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<NonNullableIntegerRequiredForModel>());

        // Assert
        Assert.Contains(nameof(NonNullableIntegerRequiredForModel.RequiredCount), exception.Message);
    }

    [Fact]
    public void ValidatePayload_WithExplicitZeroRequiredInteger_ShouldSucceed()
    {
        // Arrange
        var evt = new Event(new CreateTestEvent(), Guid.NewGuid(), new { RequiredCount = 0 });

        // Act
        IEvent result = evt.ValidatePayload<NonNullableIntegerRequiredForModel>();

        // Assert
        Assert.Same(evt, result);
    }

    // Test for edge cases with various data types
    [Theory]
    [InlineData(0)] // int
    [InlineData(false)] // bool
    [InlineData(0.0)] // double
    public void IsValid_WithNonStringNonNullValues_ShouldReturnSuccess(object value)
    {
        // Arrange
        var attribute = new RequiredForAttribute("Test_Create");
        var command = new NostifyCommand("Test_Create");
        var validationContext = new ValidationContext(new object())
        {
            MemberName = "TestProperty"
        };
        validationContext.Items["command"] = command;

        // Act
        var result = attribute.GetValidationResult(value, validationContext);

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    /// <summary>
    /// Test implementation that relies on the interface's default Events alias.
    /// </summary>
    private sealed class CommandsOnlyValidation(params string[] commandNames) : INostifyValidation
    {
        public List<string> Commands { get; } = [.. commandNames];
    }

    private sealed class CreateTestEvent : EventType
    {
        public CreateTestEvent() : base("RequiredFor_Create") { }
    }

    private sealed class UpdateTestEvent : EventType
    {
        public UpdateTestEvent() : base("RequiredFor_Update") { }
    }

    private abstract class AbstractTestEvent() : EventType("Test_Abstract");

    private sealed class TypedRequiredForModel
    {
        [RequiredFor(typeof(CreateTestEvent), typeof(UpdateTestEvent))]
        public string? Value { get; set; }
    }

    private sealed class NonNullableRequiredForModel
    {
        [RequiredFor(typeof(CreateTestEvent))]
        public Guid RequiredId { get; set; }

        public string? OptionalValue { get; set; }
    }

    private sealed class NonNullableIntegerRequiredForModel
    {
        [RequiredFor(typeof(CreateTestEvent))]
        public int RequiredCount { get; set; }

        public string? OptionalValue { get; set; }
    }
}
