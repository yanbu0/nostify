
using System;

namespace nostify;

/// <summary>
/// Configures retry behavior for operations that may transiently fail, such as Cosmos DB queries or event processing.
/// Provides settings for the maximum number of retry attempts, the delay between retries, and whether to retry
/// when a resource is not found (HTTP 404).
/// </summary>
/// <remarks>
/// Default values are 3 retries with a 1-second delay between attempts, and no retry on not-found responses.
/// Use the parameterized constructor to customize retry behavior for specific operations.
/// </remarks>
/// <example>
/// <code>
/// // Use default retry options (3 retries, 1s delay)
/// var options = new RetryOptions();
///
/// // Custom retry options with 5 retries, 2s delay, retrying on not-found
/// var options = new RetryOptions(maxRetries: 5, delay: TimeSpan.FromSeconds(2), retryWhenNotFound: true);
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
    public RetryOptions(int maxRetries, TimeSpan delay, bool retryWhenNotFound)
    {
        MaxRetries = maxRetries;
        Delay = delay;
        RetryWhenNotFound = retryWhenNotFound;
    }
}