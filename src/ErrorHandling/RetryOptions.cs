
using System;
using Microsoft.Extensions.Logging;

namespace nostify;

/// <summary>
/// Configures retry behavior for operations that may transiently fail, such as Cosmos DB queries or event processing.
/// Provides settings for the maximum number of retry attempts, the delay between retries, whether to retry
/// when a resource is not found (HTTP 404), exponential backoff via a delay multiplier, and optional logging.
/// </summary>
/// <remarks>
/// Default values are 3 retries with a 1-second delay between attempts, no retry on not-found responses,
/// exponential backoff with a 2x multiplier, and logging disabled.
/// Use the parameterized constructor to customize retry behavior for specific operations.
/// </remarks>
/// <example>
/// <code>
/// // Use default retry options (3 retries, 1s delay)
/// var options = new RetryOptions();
///
/// // Custom retry options with 5 retries, 2s delay, retrying on not-found
/// var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.FromSeconds(2), retryWhenNotFound: true);
///
/// // Constant delay (no backoff) with logging
/// var options = new RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(1), retryWhenNotFound: true, delayMultiplier: null, logRetries: true);
/// </code>
/// </example>
public class RetryOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts before the operation fails.
    /// </summary>
    /// <value>The maximum number of retries. Defaults to <c>3</c>.</value>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between successive retry attempts.
    /// </summary>
    /// <value>The <see cref="TimeSpan"/> delay between retries. Defaults to 1 second.</value>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets a value indicating whether the operation should be retried when a not-found (HTTP 404) response is received.
    /// </summary>
    /// <value><c>true</c> to retry when the resource is not found; otherwise, <c>false</c>. Defaults to <c>false</c>.</value>
    /// <remarks>
    /// Enable this when querying for a resource that may not yet be available due to eventual consistency,
    /// such as a projection that has not yet been materialized from its event stream.
    /// </remarks>
    public bool RetryWhenNotFound { get; set; } = false;

    /// <summary>
    /// Gets or sets the multiplier applied to the delay after each retry attempt for exponential backoff.
    /// When <c>null</c>, a constant delay is used between retries.
    /// </summary>
    /// <value>The delay multiplier, or <c>null</c> for constant delay. Defaults to <c>2.0</c>.</value>
    /// <remarks>
    /// With the default value of 2.0 and a <see cref="Delay"/> of 1 second,
    /// the delays between retries would be 1s, 2s, 4s, 8s, etc.
    /// Set to <c>null</c> for constant delay between retries.
    /// </remarks>
    public double? DelayMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets a value indicating whether retry attempts should be logged.
    /// When <c>true</c>, retry attempts are logged using the <see cref="Logger"/> if provided,
    /// otherwise falls back to <see cref="Console.Error"/>.
    /// </summary>
    /// <value><c>true</c> to log retry attempts; otherwise, <c>false</c>. Defaults to <c>false</c>.</value>
    public bool LogRetries { get; set; } = false;

    /// <summary>
    /// Gets or sets an optional <see cref="ILogger"/> instance used for logging retry attempts.
    /// If <c>null</c> and <see cref="LogRetries"/> is <c>true</c>, logging falls back to <see cref="Console.Error"/>.
    /// </summary>
    /// <value>An <see cref="ILogger"/> instance, or <c>null</c> for Console.Error fallback.</value>
    public ILogger? Logger { get; set; } = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryOptions"/> class with default values.
    /// </summary>
    public RetryOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryOptions"/> class with the specified retry parameters.
    /// </summary>
    /// <param name="maxRetries">The maximum number of retry attempts before the operation fails.</param>
    /// <param name="delay">The <see cref="TimeSpan"/> delay between successive retry attempts.</param>
    /// <param name="retryWhenNotFound">
    /// <c>true</c> to retry when a not-found (HTTP 404) response is received; otherwise, <c>false</c>.
    /// </param>
    /// <param name="delayMultiplier">Multiplier for exponential backoff. Defaults to <c>2.0</c>. Pass <c>null</c> for constant delay.</param>
    /// <param name="logRetries">Whether to log retry attempts. Defaults to <c>false</c>.</param>
    /// <param name="logger">Optional <see cref="ILogger"/> for structured logging. Falls back to Console.Error when <c>null</c>.</param>
    public RetryOptions(int maxRetries, TimeSpan delay, bool retryWhenNotFound, double? delayMultiplier = 2.0, bool logRetries = false, ILogger? logger = null)
    {
        MaxRetries = maxRetries;
        Delay = delay;
        RetryWhenNotFound = retryWhenNotFound;
        DelayMultiplier = delayMultiplier;
        LogRetries = logRetries;
        Logger = logger;
    }

    /// <summary>
    /// Calculates the delay for a given retry attempt, applying exponential backoff if <see cref="DelayMultiplier"/> is set.
    /// </summary>
    /// <param name="attempt">The zero-based attempt number (0 = first retry).</param>
    /// <param name="delay">Optional total milliseconds for the delay calculation. Defaults to <c>null</c> which will set it to <see cref="Delay"/>.</param>
    /// <param name="delayMultiplier">Optional delay multiplier for exponential backoff. Defaults to <c>null</c> which will set it to <see cref="DelayMultiplier"/>.</param>
    /// <returns>The calculated delay <see cref="TimeSpan"/> for the given attempt.</returns>
    public TimeSpan GetDelayForAttempt(int attempt, double? delay = null, double? delayMultiplier = null)
    {
        delayMultiplier ??= DelayMultiplier;
        delay ??= Delay.TotalMilliseconds;
        if (delayMultiplier == null || attempt == 0)
        {
            return TimeSpan.FromMilliseconds(delay.Value);
        }

        return TimeSpan.FromMilliseconds(delay.Value * Math.Pow(delayMultiplier.Value, attempt));
    }

    /// <summary>
    /// Logs a retry message using the configured <see cref="Logger"/> or falls back to <see cref="Console.Error"/>.
    /// Only logs if <see cref="LogRetries"/> is <c>true</c>.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void LogRetry(string message)
    {
        if (!LogRetries) return;

        if (Logger != null)
        {
            Logger.LogWarning(message);
        }
        else
        {
            Console.Error.WriteLine($"[nostify:retry] {message}");
        }
    }
}