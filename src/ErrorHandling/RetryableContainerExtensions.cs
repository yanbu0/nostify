using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace nostify;

/// <summary>
/// Extension methods for creating <see cref="IRetryableContainer"/> instances from Cosmos DB <see cref="Container"/> objects.
/// </summary>
public static class RetryableContainerExtensions
{
    /// <summary>
    /// Wraps a Cosmos DB <see cref="Container"/> with retry logic using the specified <see cref="RetryOptions"/>.
    /// </summary>
    /// <param name="container">The Cosmos DB container to wrap.</param>
    /// <param name="retryOptions">The retry options governing retry behavior.</param>
    /// <returns>An <see cref="IRetryableContainer"/> that proxies operations to the underlying container with retry logic.</returns>
    /// <example>
    /// <code>
    /// var container = await nostify.GetProjectionContainerAsync&lt;MyProjection&gt;();
    /// var retryable = container.WithRetry(new RetryOptions(maxRetries: 5, delay: TimeSpan.FromSeconds(2), retryWhenNotFound: true));
    /// var result = await retryable.ApplyAndPersistAsync&lt;MyProjection&gt;(evt);
    /// </code>
    /// </example>
    public static IRetryableContainer WithRetry(this Container container, RetryOptions retryOptions)
    {
        return new RetryableContainer(container, retryOptions);
    }

    /// <summary>
    /// Wraps a Cosmos DB <see cref="Container"/> with retry logic using default <see cref="RetryOptions"/>,
    /// optionally enabling retry on not-found (HTTP 404) responses for eventual consistency scenarios.
    /// </summary>
    /// <param name="container">The Cosmos DB container to wrap.</param>
    /// <param name="retryWhenNotFound">
    /// <c>true</c> to retry when a not-found (HTTP 404) response is received; otherwise, <c>false</c>.
    /// Set to <c>true</c> when reading data that may not yet be available due to eventual consistency.
    /// </param>
    /// <returns>An <see cref="IRetryableContainer"/> that proxies operations to the underlying container with retry logic.</returns>
    /// <example>
    /// <code>
    /// // Retry with eventual consistency support using all other defaults
    /// var retryable = container.WithRetry(true);
    /// var result = await retryable.ReadItemAsync&lt;MyProjection&gt;(id, partitionKey);
    /// </code>
    /// </example>
    public static IRetryableContainer WithRetry(this Container container, bool retryWhenNotFound)
    {
        return new RetryableContainer(container, new RetryOptions { RetryWhenNotFound = retryWhenNotFound });
    }

    /// <summary>
    /// Wraps a Cosmos DB <see cref="Container"/> with retry logic using default <see cref="RetryOptions"/>.
    /// Convenient shorthand for <c>container.WithRetry(new RetryOptions())</c>.
    /// </summary>
    /// <param name="container">The Cosmos DB container to wrap.</param>
    /// <returns>An <see cref="IRetryableContainer"/> with default retry behavior.</returns>
    /// <example>
    /// <code>
    /// var results = await container
    ///     .WithRetry()
    ///     .FilteredQuery&lt;MyProjection&gt;(tenantId)
    ///     .Where(x => x.name.Contains("test"))
    ///     .ReadAllAsync();
    /// </code>
    /// </example>
    public static IRetryableContainer WithRetry(this Container container)
    {
        return new RetryableContainer(container, new RetryOptions());
    }

    /// <summary>
    /// Wraps a Cosmos DB <see cref="Container"/> with retry logic using default <see cref="RetryOptions"/>
    /// and the specified <see cref="ILogger"/> for structured logging of retry operations.
    /// Automatically enables <see cref="RetryOptions.LogRetries"/>.
    /// </summary>
    /// <param name="container">The Cosmos DB container to wrap.</param>
    /// <param name="logger">The logger to use for retry diagnostics.</param>
    /// <returns>An <see cref="IRetryableContainer"/> with default retry behavior and logging enabled.</returns>
    /// <example>
    /// <code>
    /// var retryable = container.WithRetry(logger);
    /// var result = await retryable.ApplyAndPersistAsync&lt;MyProjection&gt;(evt);
    /// </code>
    /// </example>
    public static IRetryableContainer WithRetry(this Container container, ILogger logger)
    {
        return new RetryableContainer(container, new RetryOptions { Logger = logger, LogRetries = true });
    }

    /// <summary>
    /// Wraps a Cosmos DB <see cref="Container"/> with retry logic using default <see cref="RetryOptions"/>,
    /// optionally enabling retry on not-found (HTTP 404) responses, with the specified <see cref="ILogger"/>
    /// for structured logging of retry operations. Automatically enables <see cref="RetryOptions.LogRetries"/>.
    /// </summary>
    /// <param name="container">The Cosmos DB container to wrap.</param>
    /// <param name="retryWhenNotFound">
    /// <c>true</c> to retry when a not-found (HTTP 404) response is received; otherwise, <c>false</c>.
    /// </param>
    /// <param name="logger">The logger to use for retry diagnostics.</param>
    /// <returns>An <see cref="IRetryableContainer"/> with retry and logging behavior configured.</returns>
    /// <example>
    /// <code>
    /// var retryable = container.WithRetry(true, logger);
    /// var result = await retryable.ReadItemAsync&lt;MyProjection&gt;(id, partitionKey);
    /// </code>
    /// </example>
    public static IRetryableContainer WithRetry(this Container container, bool retryWhenNotFound, ILogger logger)
    {
        return new RetryableContainer(container, new RetryOptions { RetryWhenNotFound = retryWhenNotFound, Logger = logger, LogRetries = true });
    }
}
