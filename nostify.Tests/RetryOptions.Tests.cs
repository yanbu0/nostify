using System;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace nostify.Tests;

public class RetryOptionsTests
{
    #region Constructor defaults

    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults()
    {
        var options = new RetryOptions();

        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Delay);
        Assert.False(options.RetryWhenNotFound);
        Assert.Equal(2.0, options.DelayMultiplier);
        Assert.False(options.LogRetries);
        Assert.Null(options.Logger);
    }

    [Fact]
    public void ParameterizedConstructor_SetsAllProperties()
    {
        var logger = new Mock<ILogger>().Object;
        var options = new RetryOptions(
            maxRetries: 5,
            delay: TimeSpan.FromSeconds(2),
            retryWhenNotFound: true,
            delayMultiplier: 2.0,
            logRetries: true,
            logger: logger
        );

        Assert.Equal(5, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Delay);
        Assert.True(options.RetryWhenNotFound);
        Assert.Equal(2.0, options.DelayMultiplier);
        Assert.True(options.LogRetries);
        Assert.Same(logger, options.Logger);
    }

    [Fact]
    public void ParameterizedConstructor_DefaultOptionalParameters()
    {
        var options = new RetryOptions(
            maxRetries: 2,
            delay: TimeSpan.FromMilliseconds(500),
            retryWhenNotFound: false
        );

        Assert.Equal(2, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.Delay);
        Assert.False(options.RetryWhenNotFound);
        Assert.Equal(2.0, options.DelayMultiplier);
        Assert.False(options.LogRetries);
        Assert.Null(options.Logger);
    }

    #endregion

    #region GetDelayForAttempt

    [Fact]
    public void GetDelayForAttempt_NoMultiplier_ReturnsConstantDelay()
    {
        var options = new RetryOptions { Delay = TimeSpan.FromSeconds(1), DelayMultiplier = null };

        Assert.Equal(TimeSpan.FromSeconds(1), options.GetDelayForAttempt(0));
        Assert.Equal(TimeSpan.FromSeconds(1), options.GetDelayForAttempt(1));
        Assert.Equal(TimeSpan.FromSeconds(1), options.GetDelayForAttempt(2));
        Assert.Equal(TimeSpan.FromSeconds(1), options.GetDelayForAttempt(5));
    }

    [Fact]
    public void GetDelayForAttempt_WithMultiplier_ReturnsExponentialDelay()
    {
        var options = new RetryOptions { Delay = TimeSpan.FromSeconds(1), DelayMultiplier = 2.0 };

        // attempt 0 always returns base delay
        Assert.Equal(TimeSpan.FromSeconds(1), options.GetDelayForAttempt(0));
        // attempt 1: 1000 * 2^1 = 2000ms
        Assert.Equal(TimeSpan.FromSeconds(2), options.GetDelayForAttempt(1));
        // attempt 2: 1000 * 2^2 = 4000ms
        Assert.Equal(TimeSpan.FromSeconds(4), options.GetDelayForAttempt(2));
        // attempt 3: 1000 * 2^3 = 8000ms
        Assert.Equal(TimeSpan.FromSeconds(8), options.GetDelayForAttempt(3));
    }

    [Fact]
    public void GetDelayForAttempt_WithMultiplier1Point5_ReturnsCorrectDelay()
    {
        var options = new RetryOptions { Delay = TimeSpan.FromMilliseconds(100), DelayMultiplier = 1.5 };

        Assert.Equal(TimeSpan.FromMilliseconds(100), options.GetDelayForAttempt(0));
        // attempt 1: 100 * 1.5^1 = 150ms
        Assert.Equal(TimeSpan.FromMilliseconds(150), options.GetDelayForAttempt(1));
        // attempt 2: 100 * 1.5^2 = 225ms
        Assert.Equal(TimeSpan.FromMilliseconds(225), options.GetDelayForAttempt(2));
    }

    [Fact]
    public void GetDelayForAttempt_DelayOverride_UsedInsteadOfInstanceDelay()
    {
        var options = new RetryOptions { Delay = TimeSpan.FromSeconds(1), DelayMultiplier = 2.0 };

        // attempt 0 always returns base delay (overridden value)
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.GetDelayForAttempt(0, delay: 250));
        // attempt 1: 250 * 2^1 = 500ms (instance multiplier still used)
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.GetDelayForAttempt(1, delay: 250));
        // attempt 2: 250 * 2^2 = 1000ms
        Assert.Equal(TimeSpan.FromMilliseconds(1000), options.GetDelayForAttempt(2, delay: 250));
    }

    [Fact]
    public void GetDelayForAttempt_DelayMultiplierOverride_UsedInsteadOfInstanceMultiplier()
    {
        var options = new RetryOptions { Delay = TimeSpan.FromMilliseconds(100), DelayMultiplier = 2.0 };

        // attempt 0 always returns base delay
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.GetDelayForAttempt(0, delayMultiplier: 3));
        // attempt 1: 100 * 3^1 = 300ms (overridden multiplier)
        Assert.Equal(TimeSpan.FromMilliseconds(300), options.GetDelayForAttempt(1, delayMultiplier: 3));
        // attempt 2: 100 * 3^2 = 900ms
        Assert.Equal(TimeSpan.FromMilliseconds(900), options.GetDelayForAttempt(2, delayMultiplier: 3));
    }

    [Fact]
    public void GetDelayForAttempt_BothOverrides_UsedInsteadOfInstanceValues()
    {
        var options = new RetryOptions { Delay = TimeSpan.FromSeconds(5), DelayMultiplier = 2.0 };

        // attempt 0 always returns base delay (overridden value)
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.GetDelayForAttempt(0, delay: 100, delayMultiplier: 3));
        // attempt 1: 100 * 3^1 = 300ms
        Assert.Equal(TimeSpan.FromMilliseconds(300), options.GetDelayForAttempt(1, delay: 100, delayMultiplier: 3));
        // attempt 2: 100 * 3^2 = 900ms
        Assert.Equal(TimeSpan.FromMilliseconds(900), options.GetDelayForAttempt(2, delay: 100, delayMultiplier: 3));
        // attempt 3: 100 * 3^3 = 2700ms
        Assert.Equal(TimeSpan.FromMilliseconds(2700), options.GetDelayForAttempt(3, delay: 100, delayMultiplier: 3));
    }

    [Fact]
    public void GetDelayForAttempt_NullMultiplierOnInstance_NoOverride_ReturnsConstantDelayUsingOverrideDelay()
    {
        var options = new RetryOptions { Delay = TimeSpan.FromSeconds(1), DelayMultiplier = null };

        // No multiplier (instance null, no override) -> constant override delay regardless of attempt
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.GetDelayForAttempt(0, delay: 200));
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.GetDelayForAttempt(1, delay: 200));
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.GetDelayForAttempt(5, delay: 200));
    }

    #endregion

    #region LogRetry

    [Fact]
    public void LogRetry_LogRetriesFalse_DoesNotLog()
    {
        var mockLogger = new Mock<ILogger>();
        var options = new RetryOptions { LogRetries = false, Logger = mockLogger.Object };

        options.LogRetry("test message");

        // Should not have logged anything
        mockLogger.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void LogRetry_LogRetriesTrue_WithLogger_LogsWarning()
    {
        var mockLogger = new Mock<ILogger>();
        var options = new RetryOptions { LogRetries = true, Logger = mockLogger.Object };

        options.LogRetry("test retry message");

        mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogRetry_LogRetriesTrue_WithoutLogger_FallsBackToConsoleError()
    {
        var options = new RetryOptions { LogRetries = true, Logger = null };

        // Redirect Console.Error to capture output
        var originalError = Console.Error;
        using var writer = new System.IO.StringWriter();
        Console.SetError(writer);

        try
        {
            options.LogRetry("fallback message");
            var output = writer.ToString();
            Assert.Contains("[nostify:retry]", output);
            Assert.Contains("fallback message", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    #endregion

    #region Property setters

    [Fact]
    public void Properties_CanBeSetAfterConstruction()
    {
        var options = new RetryOptions();

        options.MaxRetries = 10;
        options.Delay = TimeSpan.FromSeconds(5);
        options.RetryWhenNotFound = true;
        options.DelayMultiplier = 3.0;
        options.LogRetries = true;

        Assert.Equal(10, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Delay);
        Assert.True(options.RetryWhenNotFound);
        Assert.Equal(3.0, options.DelayMultiplier);
        Assert.True(options.LogRetries);
    }

    #endregion
}
