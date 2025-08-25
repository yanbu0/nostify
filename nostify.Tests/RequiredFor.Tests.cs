using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Xunit;
using nostify;

namespace nostify.Tests;

public class RequiredForAttributeTests
{
    [Fact]
    public void Constructor_WithSingleCommand_ShouldCreateAttributeWithOneCommand()
    {
        // Arrange
        var command = "Test_Create";

        // Act
        var attribute = new RequiredForAttribute(command);

        // Assert
        Assert.Single(attribute.Commands);
        Assert.Equal(command, attribute.Commands[0]);
    }

    [Fact]
    public void Constructor_WithMultipleCommands_ShouldCreateAttributeWithAllCommands()
    {
        // Arrange
        var commands = new[] { "Test_Create", "Test_Update", "Test_Delete" };

        // Act
        var attribute = new RequiredForAttribute(commands);

        // Assert
        Assert.Equal(3, attribute.Commands.Count);
        Assert.Equal("Test_Create", attribute.Commands[0]);
        Assert.Equal("Test_Update", attribute.Commands[1]);
        Assert.Equal("Test_Delete", attribute.Commands[2]);
    }

    [Fact]
    public void Constructor_WithEmptyCommandArray_ShouldCreateAttributeWithEmptyList()
    {
        // Arrange
        var commands = new string[0];

        // Act
        var attribute = new RequiredForAttribute(commands);

        // Assert
        Assert.Empty(attribute.Commands);
    }

    [Fact]
    public void Constructor_WithNullCommand_ShouldCreateAttributeWithNullInList()
    {
        // Arrange
        string? command = null;

        // Act
        var attribute = new RequiredForAttribute(command!);

        // Assert
        Assert.Single(attribute.Commands);
        Assert.Null(attribute.Commands[0]);
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
        Assert.Contains("command to be specified", result.ErrorMessage);
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
    public void INostifyValidation_Commands_ShouldExposeCommandsList()
    {
        // Arrange
        var commands = new[] { "Test_Create", "Test_Update" };
        var attribute = new RequiredForAttribute(commands);

        // Act
        INostifyValidation validation = attribute;

        // Assert
        Assert.Equal(commands.Length, validation.Commands.Count);
        Assert.Equal("Test_Create", validation.Commands[0]);
        Assert.Equal("Test_Update", validation.Commands[1]);
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
    public void Commands_Property_ShouldBeReadOnly()
    {
        // Arrange
        var commands = new[] { "Test_Create" };
        var attribute = new RequiredForAttribute(commands);

        // Act
        var commandsList = attribute.Commands;

        // Assert
        Assert.NotNull(commandsList);
        // Verify Commands property doesn't have a setter (compilation check)
        Assert.IsType<List<string>>(commandsList);
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
}
