namespace nostify.Tests;

/// <summary>
/// Serializes tests that mutate process-wide asynchronous event request environment variables.
/// Disabling parallelization also prevents unrelated collections from observing transient values.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AsyncEventRequestEnvironmentCollection
{
    /// <summary>The xUnit collection name used by environment-sensitive tests.</summary>
    public const string Name = "Async event request environment";
}
