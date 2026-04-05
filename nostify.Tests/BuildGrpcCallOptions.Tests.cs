using System.Linq;
using nostify;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Tests for <see cref="ExternalDataEvent.BuildGrpcCallOptions"/>.
/// Validates that the auth token is correctly piped into gRPC metadata.
/// </summary>
public class BuildGrpcCallOptionsTests
{
    [Fact]
    public void BuildGrpcCallOptions_EmptyToken_ReturnsNoHeaders()
    {
        // Act
        var options = ExternalDataEvent.BuildGrpcCallOptions("");

        // Assert
        Assert.Null(options.Headers);
    }

    [Fact]
    public void BuildGrpcCallOptions_NullToken_ReturnsNoHeaders()
    {
        // Act
        var options = ExternalDataEvent.BuildGrpcCallOptions(null!);

        // Assert
        Assert.Null(options.Headers);
    }

    [Fact]
    public void BuildGrpcCallOptions_DefaultParameter_ReturnsNoHeaders()
    {
        // Act
        var options = ExternalDataEvent.BuildGrpcCallOptions();

        // Assert
        Assert.Null(options.Headers);
    }

    [Fact]
    public void BuildGrpcCallOptions_WithToken_AddsAuthorizationHeader()
    {
        // Act
        var options = ExternalDataEvent.BuildGrpcCallOptions("my-secret-token");

        // Assert
        Assert.NotNull(options.Headers);
        Assert.Single(options.Headers);
        var header = options.Headers.First();
        Assert.Equal("authorization", header.Key);
        Assert.Equal("Bearer my-secret-token", header.Value);
    }

    [Fact]
    public void BuildGrpcCallOptions_WithToken_FormatsAsBearerScheme()
    {
        // Arrange
        var token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test.signature";

        // Act
        var options = ExternalDataEvent.BuildGrpcCallOptions(token);

        // Assert
        Assert.NotNull(options.Headers);
        var header = options.Headers.First();
        Assert.StartsWith("Bearer ", header.Value);
        Assert.Equal($"Bearer {token}", header.Value);
    }
}
