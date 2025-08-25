using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Xunit;
using nostify;

namespace nostify.Tests;

public class NostifyValidationExceptionTests
{
    [Fact]
    public void Constructor_WithSingleMessage_ShouldCreateExceptionWithOneValidationResult()
    {
        // Arrange
        var message = "Test validation error";

        // Act
        var exception = new NostifyValidationException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Single(exception.ValidationMessages);
        Assert.Equal(message, exception.ValidationMessages.First().ErrorMessage);
    }

    [Fact]
    public void Constructor_WithValidationResults_ShouldCreateExceptionWithAllResults()
    {
        // Arrange
        var validationResults = new List<ValidationResult>
        {
            new ValidationResult("First error", new[] { "Property1" }),
            new ValidationResult("Second error", new[] { "Property2" }),
            new ValidationResult("Third error", new[] { "Property3" })
        };

        // Act
        var exception = new NostifyValidationException(validationResults);

        // Assert
        Assert.Equal(3, exception.ValidationMessages.Count);
        Assert.Equal("First error", exception.ValidationMessages[0].ErrorMessage);
        Assert.Equal("Second error", exception.ValidationMessages[1].ErrorMessage);
        Assert.Equal("Third error", exception.ValidationMessages[2].ErrorMessage);
        
        // Verify member names
        Assert.Single(exception.ValidationMessages[0].MemberNames);
        Assert.Equal("Property1", exception.ValidationMessages[0].MemberNames.First());
    }

    [Fact]
    public void Constructor_WithValidationResults_ShouldCreateCombinedMessage()
    {
        // Arrange
        var validationResults = new List<ValidationResult>
        {
            new ValidationResult("First error"),
            new ValidationResult("Second error"),
            new ValidationResult("Third error")
        };

        // Act
        var exception = new NostifyValidationException(validationResults);

        // Assert
        var expectedMessage = "Validation failed: First error Second error Third error";
        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public void Constructor_WithEmptyValidationResults_ShouldCreateEmptyException()
    {
        // Arrange
        var validationResults = new List<ValidationResult>();

        // Act
        var exception = new NostifyValidationException(validationResults);

        // Assert
        Assert.Empty(exception.ValidationMessages);
        Assert.Equal("Validation failed with no specific errors.", exception.Message);
    }

    [Fact]
    public void Constructor_WithMixedValidationResults_ShouldHandleNullErrorMessages()
    {
        // Arrange
        var validationResults = new List<ValidationResult>
        {
            new ValidationResult("Valid error"),
            new ValidationResult(null), // Null error message
            new ValidationResult("Another valid error")
        };

        // Act
        var exception = new NostifyValidationException(validationResults);

        // Assert
        Assert.Equal(3, exception.ValidationMessages.Count);
        Assert.Equal("Valid error", exception.ValidationMessages[0].ErrorMessage);
        Assert.Null(exception.ValidationMessages[1].ErrorMessage);
        Assert.Equal("Another valid error", exception.ValidationMessages[2].ErrorMessage);
    }

    [Fact]
    public void ValidationMessages_ShouldBeReadOnly()
    {
        // Arrange
        var exception = new NostifyValidationException("Test error");

        // Act & Assert
        // ValidationMessages should be a read-only property
        Assert.NotNull(exception.ValidationMessages);
        
        // Verify it's a list that can be read
        var count = exception.ValidationMessages.Count;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Constructor_WithValidationResultsAndMemberNames_ShouldPreserveMemberNames()
    {
        // Arrange
        var memberNames = new[] { "Name", "Email", "Age" };
        var validationResults = new List<ValidationResult>
        {
            new ValidationResult("Multiple properties are invalid", memberNames)
        };

        // Act
        var exception = new NostifyValidationException(validationResults);

        // Assert
        Assert.Single(exception.ValidationMessages);
        var result = exception.ValidationMessages.First();
        Assert.Equal("Multiple properties are invalid", result.ErrorMessage);
        Assert.Equal(3, result.MemberNames.Count());
        Assert.Contains("Name", result.MemberNames);
        Assert.Contains("Email", result.MemberNames);
        Assert.Contains("Age", result.MemberNames);
    }

    [Fact]
    public void InheritanceFromValidationException_ShouldMaintainBaseClassBehavior()
    {
        // Arrange
        var message = "Test validation error";

        // Act
        var exception = new NostifyValidationException(message);

        // Assert
        Assert.IsAssignableFrom<ValidationException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Theory]
    [InlineData("Simple error message")]
    [InlineData("Error with special characters: !@#$%^&*()")]
    [InlineData("Multi-line\nerror\nmessage")]
    [InlineData("")]
    public void Constructor_WithVariousMessages_ShouldHandleAllMessageTypes(string message)
    {
        // Act
        var exception = new NostifyValidationException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Single(exception.ValidationMessages);
        Assert.Equal(message, exception.ValidationMessages.First().ErrorMessage);
    }

    [Fact]
    public void ToString_ShouldIncludeValidationMessages()
    {
        // Arrange
        var validationResults = new List<ValidationResult>
        {
            new ValidationResult("First error"),
            new ValidationResult("Second error")
        };
        var exception = new NostifyValidationException(validationResults);

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains("First error", result);
        Assert.Contains("Second error", result);
        Assert.Contains("NostifyValidationException", result);
    }

    [Fact]
    public void Constructor_WithLargeNumberOfValidationResults_ShouldHandleEfficiently()
    {
        // Arrange
        var validationResults = new List<ValidationResult>();
        for (int i = 0; i < 100; i++)
        {
            validationResults.Add(new ValidationResult($"Error {i}", new[] { $"Property{i}" }));
        }

        // Act
        var exception = new NostifyValidationException(validationResults);

        // Assert
        Assert.Equal(100, exception.ValidationMessages.Count);
        Assert.Equal("Error 0", exception.ValidationMessages[0].ErrorMessage);
        Assert.Equal("Error 99", exception.ValidationMessages[99].ErrorMessage);
        
        // Verify the combined message contains all errors
        var message = exception.Message;
        Assert.Contains("Error 0", message);
        Assert.Contains("Error 99", message);
    }
}
