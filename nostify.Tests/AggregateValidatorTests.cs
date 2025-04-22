using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;
using Moq;
using nostify;
using Microsoft.Extensions.Configuration;

namespace nostify.Tests;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class AggregateValidatorTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly AggregateValidator _validator;

    // Test data classes
    private record TestAggregateDirect
    {
        [MaxStringLength(10)]
        public string? Name { get; init; }
        public string? Description { get; init; } // No attribute, should use default
    }

    private record TestAggregateConfig
    {
        [MaxStringLength("Validation:TestNameMaxLength")]
        public string? Name { get; init; }
        [MaxStringLength("Validation:MissingKey")] // Key doesn't exist, should use default
        public string? MissingKeyField { get; init; }
    }

    private record TestAggregateDefault
    {
        // No attribute, should use default
        public string? Notes { get; init; }
    }


    public AggregateValidatorTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        var configSectionStub = new Mock<IConfigurationSection>();

        // Setup default config value
        configSectionStub.Setup(x => x.Value).Returns("50"); // Default length from config
        _mockConfig.Setup(c => c.GetSection("Nostify:Validation:DefaultMaxStringLength"))
            .Returns(configSectionStub.Object);

        // Setup specific config value for TestAggregateConfig
        var specificConfigSectionStub = new Mock<IConfigurationSection>();
        specificConfigSectionStub.Setup(x => x.Value).Returns("20"); // Specific length from config
        _mockConfig.Setup(c => c.GetSection("Validation:TestNameMaxLength"))
            .Returns(specificConfigSectionStub.Object);

        // Setup mock for direct config key access used in MaxStringLengthAttribute
        _mockConfig.Setup(c => c["Nostify:Validation:DefaultMaxStringLength"]).Returns("50");
        _mockConfig.Setup(c => c["Validation:TestNameMaxLength"]).Returns("20");
        //dont' setup Validation:MissingKey in order to test default fallback

        _validator = new AggregateValidator(_mockConfig.Object);
    }

    [Fact]
    public void Validate_DirectLength_Valid_ReturnsNoErrors()
    {
        // Arrange
        var aggregate = new TestAggregateDirect { Name = "Valid", Description = "Short Desc" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_DirectLength_Invalid_ReturnsError()
    {
        // Arrange
        var aggregate = new TestAggregateDirect { Name = "This string is longer than 10 chars" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(TestAggregateDirect.Name), error.Property);
        Assert.Contains("exceeds max 10", error.Message);
    }

    [Fact]
    public void Validate_DirectLength_NullString_ReturnsNoErrors()
    {
        // Arrange
        var aggregate = new TestAggregateDirect { Name = null };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_DirectLength_EmptyString_ReturnsNoErrors()
    {
        // Arrange
        var aggregate = new TestAggregateDirect { Name = "" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Empty(errors);
    }


    [Fact]
    public void Validate_ConfigLength_Valid_ReturnsNoErrors()
    {
        // Arrange
        var aggregate = new TestAggregateConfig { Name = "Valid Config Name" }; // less than 20 characters

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ConfigLength_Invalid_ReturnsError()
    {
        // Arrange
        var aggregate = new TestAggregateConfig { Name = "This config name string is definitely way too long and is greater than 20 characters" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(TestAggregateConfig.Name), error.Property);
        Assert.Contains("exceeds max 20", error.Message);
    }

    [Fact]
    public void Validate_ConfigLength_MissingKey_UsesDefault_Valid_ReturnsNoErrors()
    {
        // Arrange
        // MissingKeyField uses "Validation:MissingKey", which is not configured.
        // Should fall back to default configured length (50).
        var aggregate = new TestAggregateConfig { MissingKeyField = "This fits within the default fifty characters" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Empty(errors);
    }


    [Fact]
    public void Validate_ConfigLength_MissingKey_UsesDefault_Invalid_ReturnsError()
    {
        // Arrange
        // MissingKeyField uses "Validation:MissingKey", which is not configured.
        // Should fall back to default configured length (50).
        var aggregate = new TestAggregateConfig { MissingKeyField = "This string is intentionally made longer than the default fifty characters limit set in the test setup" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(TestAggregateConfig.MissingKeyField), error.Property);
        Assert.Contains("exceeds max 50", error.Message);
    }

    [Fact]
    public void Validate_DefaultLength_Valid_ReturnsNoErrors()
    {
        // Arrange
        // TestAggregateDefault.Notes has no attribute, uses default configured length (50).
        var aggregate = new TestAggregateDefault { Notes = "Fits in default length of 50" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_DefaultLength_Invalid_ReturnsError()
    {
        // Arrange
        // TestAggregateDefault.Notes has no attribute, uses default configured length (50).
        var aggregate = new TestAggregateDefault { Notes = "This notes string is definitely going to be much longer than the default fifty characters limit we set up" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        var error = Assert.Single(errors);
        Assert.Equal(nameof(TestAggregateDefault.Notes), error.Property);
        Assert.Contains("exceeds max 50", error.Message);
    }

    [Fact]
    public void Validate_MultipleProperties_MixedValidity_ReturnsCorrectErrors()
    {
        // Arrange
        var aggregate = new TestAggregateDirect
        {
            Name = "This name is too long (more than ten characters)",
            Description = "This description is also way too long for the default limit of fifty characters set in the configuration"
        };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Property == nameof(TestAggregateDirect.Name) && e.Message.Contains("exceeds max 10"));
        Assert.Contains(errors, e => e.Property == nameof(TestAggregateDirect.Description) && e.Message.Contains("exceeds max 50"));
    }

    [Fact]
    public void Validate_NullAggregate_ThrowsArgumentNullException()
    {
        // Arrange
        TestAggregateDirect aggregate = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(aggregate));
    }

    [Fact]
    public void Validate_TypeWithoutStringProperties_ReturnsNoErrors()
    {
        // Arrange
        var aggregate = new { Number = 123, Date = DateTime.UtcNow };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_TypeWithoutValidationAttributes_ReturnsNoErrors()
    {
        _validator.DefaultMaxStringLengthValue = int.MaxValue;
        // Arrange
        // This type has no MaxStringLengthAttribute; DefaultMaxStringLengthValue is set to int.MaxValue
        var aggregate = new { SomeData = "This should pass as there's no validation configured for it implicitly or explicitly beyond the default" };

        // Act
        var errors = _validator.Validate(aggregate);

        // Assert
        Assert.Empty(errors);
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
