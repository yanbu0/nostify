using System;
using Xunit;
using nostify;

namespace nostify.Tests;

public class NostifyCommandTests
{
    [Fact]
    public void Constructor_WithValidName_ShouldCreateCommand()
    {
        // Arrange
        var name = "Test_Create";

        // Act
        var command = new NostifyCommand(name);

        // Assert
        Assert.Equal(name, command.name);
        Assert.False(command.isNew);
        Assert.False(command.allowNullPayload);
    }

    [Fact]
    public void Constructor_WithAllParameters_ShouldCreateCommand()
    {
        // Arrange
        var name = "Test_Create";
        var isNew = true;
        var allowNullPayload = true;

        // Act
        var command = new NostifyCommand(name, isNew, allowNullPayload);

        // Assert
        Assert.Equal(name, command.name);
        Assert.Equal(isNew, command.isNew);
        Assert.Equal(allowNullPayload, command.allowNullPayload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Constructor_WithInvalidName_ShouldThrowArgumentException(string invalidName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new NostifyCommand(invalidName));
        Assert.Equal("Command name cannot be null or empty (Parameter 'name')", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullName_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new NostifyCommand(null!));
        Assert.Equal("Command name cannot be null or empty (Parameter 'name')", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ToString_ShouldReturnCommandName()
    {
        // Arrange
        var name = "Test_Create";
        var command = new NostifyCommand(name);

        // Act
        var result = command.ToString();

        // Assert
        Assert.Equal(name, result);
    }

    [Fact]
    public void Equals_WithSameNameCommand_ShouldReturnTrue()
    {
        // Arrange
        var name = "Test_Create";
        var command1 = new NostifyCommand(name, true, false);
        var command2 = new NostifyCommand(name, false, true);

        // Act
        var result = command1.Equals(command2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_WithDifferentNameCommand_ShouldReturnFalse()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Create");
        var command2 = new NostifyCommand("Test_Update");

        // Act
        var result = command1.Equals(command2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Equals_WithNullCommand_ShouldReturnFalse()
    {
        // Arrange
        var command = new NostifyCommand("Test_Create");

        // Act
        var result = command.Equals(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Equals_WithNonCommandObject_ShouldReturnFalse()
    {
        // Arrange
        var command = new NostifyCommand("Test_Create");
        var nonCommand = "Test_Create";

        // Act
        var result = command.Equals(nonCommand);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Equals_WithInheritedCommand_ShouldReturnTrue()
    {
        // Arrange
        var baseCommand = new NostifyCommand("Test_Create");
        var inheritedCommand = new TestInheritedCommand("Test_Create");

        // Act
        var result = baseCommand.Equals(inheritedCommand);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetHashCode_WithSameNameCommands_ShouldReturnSameHashCode()
    {
        // Arrange
        var name = "Test_Create";
        var command1 = new NostifyCommand(name, true);
        var command2 = new NostifyCommand(name, false);

        // Act
        var hash1 = command1.GetHashCode();
        var hash2 = command2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GetHashCode_WithDifferentNameCommands_ShouldReturnDifferentHashCode()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Create");
        var command2 = new NostifyCommand("Test_Update");

        // Act
        var hash1 = command1.GetHashCode();
        var hash2 = command2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void EqualityOperator_WithSameNameCommands_ShouldReturnTrue()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Create");
        var command2 = new NostifyCommand("Test_Create");

        // Act
        var result = command1 == command2;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EqualityOperator_WithDifferentNameCommands_ShouldReturnFalse()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Create");
        var command2 = new NostifyCommand("Test_Update");

        // Act
        var result = command1 == command2;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void InequalityOperator_WithSameNameCommands_ShouldReturnFalse()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Create");
        var command2 = new NostifyCommand("Test_Create");

        // Act
        var result = command1 != command2;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void InequalityOperator_WithDifferentNameCommands_ShouldReturnTrue()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Create");
        var command2 = new NostifyCommand("Test_Update");

        // Act
        var result = command1 != command2;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CompareTo_WithSameNameCommand_ShouldReturnZero()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Create");
        var command2 = new NostifyCommand("Test_Create");

        // Act
        var result = command1.CompareTo(command2);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareTo_WithAlphabeticallyEarlierCommand_ShouldReturnNegative()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Create");
        var command2 = new NostifyCommand("Test_Update");

        // Act
        var result = command1.CompareTo(command2);

        // Assert
        Assert.True(result < 0);
    }

    [Fact]
    public void CompareTo_WithAlphabeticallyLaterCommand_ShouldReturnPositive()
    {
        // Arrange
        var command1 = new NostifyCommand("Test_Update");
        var command2 = new NostifyCommand("Test_Create");

        // Act
        var result = command1.CompareTo(command2);

        // Assert
        Assert.True(result > 0);
    }

    [Fact]
    public void Properties_ShouldBeReadOnly()
    {
        // Arrange
        var name = "Test_Create";
        var isNew = true;
        var allowNullPayload = true;
        var command = new NostifyCommand(name, isNew, allowNullPayload);

        // Act & Assert - Properties should have no setters (verified by compilation)
        Assert.Equal(name, command.name);
        Assert.Equal(isNew, command.isNew);
        Assert.Equal(allowNullPayload, command.allowNullPayload);
    }

    // Test helper class for inheritance testing
    private class TestInheritedCommand : NostifyCommand
    {
        public TestInheritedCommand(string name) : base(name) { }
    }
}
