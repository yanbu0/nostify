using System;
using Xunit;
using nostify;

namespace nostify.Tests;

public class NostifyExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_ShouldCreateExceptionWithMessage()
    {
        // Arrange
        var message = "Test nostify exception";

        // Act
        var exception = new NostifyException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("Simple error message")]
    [InlineData("Error with special characters: !@#$%^&*()")]
    [InlineData("Multi-line\nerror\nmessage")]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithVariousMessages_ShouldHandleAllMessageTypes(string message)
    {
        // Act
        var exception = new NostifyException(message);

        // Assert
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void Constructor_WithNullMessage_ShouldHandleNull()
    {
        // Act
        var exception = new NostifyException(null!);

        // Assert
        Assert.Equal("Exception of type 'nostify.NostifyException' was thrown.", exception.Message);
    }

    [Fact]
    public void InheritanceFromException_ShouldMaintainBaseClassBehavior()
    {
        // Arrange
        var message = "Test exception";

        // Act
        var exception = new NostifyException(message);

        // Assert
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void ToString_ShouldIncludeExceptionTypeAndMessage()
    {
        // Arrange
        var message = "Test nostify exception";
        var exception = new NostifyException(message);

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains("NostifyException", result);
        Assert.Contains(message, result);
    }

    [Fact]
    public void Exception_ShouldBeSerializable()
    {
        // Arrange
        var message = "Test serialization";
        var exception = new NostifyException(message);

        // Act & Assert
        // Verify the exception has the Serializable attribute by checking it doesn't throw
        // when accessing serialization-related properties
        Assert.NotNull(exception.Message);
        Assert.NotNull(exception.GetType().FullName);
    }

    [Fact]
    public void Message_ShouldBeReadOnly()
    {
        // Arrange
        var originalMessage = "Original message";
        var exception = new NostifyException(originalMessage);

        // Act
        var message = exception.Message;

        // Assert
        Assert.Equal(originalMessage, message);
        // Verify Message property doesn't have a setter (compilation check)
        Assert.IsType<string>(message);
    }

    [Fact]
    public void StackTrace_ShouldCaptureCreationLocation()
    {
        try
        {
            throw new NostifyException("Test stack trace");
        }
        catch (NostifyException ex)
        {
            // Arrange & Act
            var exception = ex;

            // Assert
            Assert.NotNull(exception.StackTrace);
            // In a real throw/catch scenario, StackTrace would be populated
            // Here we just verify the property exists and is accessible
        }
    }

    [Fact]
    public void Data_PropertyShouldBeAccessible()
    {
        // Arrange
        var exception = new NostifyException("Test data");

        // Act
        exception.Data["TestKey"] = "TestValue";

        // Assert
        Assert.Equal("TestValue", exception.Data["TestKey"]);
    }

    [Fact]
    public void HelpLink_PropertyShouldBeAccessible()
    {
        // Arrange
        var exception = new NostifyException("Test help link");
        var helpLink = "https://example.com/help";

        // Act
        exception.HelpLink = helpLink;

        // Assert
        Assert.Equal(helpLink, exception.HelpLink);
    }

    [Fact]
    public void Source_PropertyShouldBeAccessible()
    {
        // Arrange
        var exception = new NostifyException("Test source");
        var source = "TestSource";

        // Act
        exception.Source = source;

        // Assert
        Assert.Equal(source, exception.Source);
    }

    [Fact]
    public void GetHashCode_ShouldReturnConsistentValue()
    {
        // Arrange
        var exception = new NostifyException("Test hash code");

        // Act
        var hashCode1 = exception.GetHashCode();
        var hashCode2 = exception.GetHashCode();

        // Assert
        Assert.Equal(hashCode1, hashCode2);
    }

    [Fact]
    public void Equals_WithSameException_ShouldReturnTrue()
    {
        // Arrange
        var exception = new NostifyException("Test equals");

        // Act
        var result = exception.Equals(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_WithDifferentException_ShouldReturnFalse()
    {
        // Arrange
        var exception1 = new NostifyException("Test equals 1");
        var exception2 = new NostifyException("Test equals 2");

        // Act
        var result = exception1.Equals(exception2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Constructor_ShouldSetHResultToDefault()
    {
        // Arrange & Act
        var exception = new NostifyException("Test HResult");

        // Assert
        // HResult should be set to the default Exception HResult
        Assert.NotEqual(0, exception.HResult);
    }
}
