using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace nostify;

/// <summary>
/// Immutable options for a non-destructive Durable projection rebuild.
/// </summary>
public sealed class DurableRollingProjectionOptions : IJsonOnDeserialized
{
    private IReadOnlyList<string> _selectedProperties = [];
    private int _maxEtagRetries = 3;
    private TimeSpan _initialBackoff = TimeSpan.FromMilliseconds(250);
    private double _backoffCoefficient = 2.0;
    private string _partitionKeyPath = "/tenantId";
    private bool _hasMaxEtagRetries;
    private bool _hasInitialBackoff;
    private bool _hasBackoffCoefficient;
    private bool _hasPartitionKeyPath;

    [JsonConstructor]
    [Newtonsoft.Json.JsonConstructor]
    private DurableRollingProjectionOptions()
    {
    }

    /// <summary>
    /// Creates rolling rebuild options.
    /// </summary>
    /// <param name="selectedProperties">
    /// Projection properties to rebuild selectively. An empty collection performs a full rebuild.
    /// </param>
    /// <param name="maxEtagRetries">Maximum optimistic-concurrency retries before the final unconditional write.</param>
    /// <param name="initialBackoff">Delay before the first optimistic-concurrency retry.</param>
    /// <param name="backoffCoefficient">Multiplier used for exponential retry backoff.</param>
    /// <param name="partitionKeyPath">Cosmos partition-key path for the projection container.</param>
    public DurableRollingProjectionOptions(
        IReadOnlyList<string>? selectedProperties = null,
        int maxEtagRetries = 3,
        TimeSpan? initialBackoff = null,
        double backoffCoefficient = 2.0,
        string partitionKeyPath = "/tenantId")
    {
        var resolvedInitialBackoff = initialBackoff ?? TimeSpan.FromMilliseconds(250);
        ValidateOptions(maxEtagRetries, resolvedInitialBackoff, backoffCoefficient, partitionKeyPath);

        _selectedProperties = NormalizeSelectedProperties(selectedProperties);
        _maxEtagRetries = maxEtagRetries;
        _initialBackoff = resolvedInitialBackoff;
        _backoffCoefficient = backoffCoefficient;
        _partitionKeyPath = partitionKeyPath.StartsWith('/')
            ? partitionKeyPath
            : $"/{partitionKeyPath}";
    }

    /// <summary>Gets the normalized properties selected for replay.</summary>
    [JsonInclude]
    [Newtonsoft.Json.JsonProperty]
    public IReadOnlyList<string> SelectedProperties
    {
        get => _selectedProperties;
        private set => _selectedProperties = NormalizeSelectedProperties(value);
    }

    /// <summary>Gets the maximum number of ETag conflict retries.</summary>
    [JsonInclude]
    [Newtonsoft.Json.JsonProperty]
    public int MaxEtagRetries
    {
        get => _maxEtagRetries;
        private set
        {
            _maxEtagRetries = value;
            _hasMaxEtagRetries = true;
        }
    }

    /// <summary>Gets the delay before the first ETag conflict retry.</summary>
    [JsonInclude]
    [Newtonsoft.Json.JsonProperty]
    public TimeSpan InitialBackoff
    {
        get => _initialBackoff;
        private set
        {
            _initialBackoff = value;
            _hasInitialBackoff = true;
        }
    }

    /// <summary>Gets the exponential backoff coefficient.</summary>
    [JsonInclude]
    [Newtonsoft.Json.JsonProperty]
    public double BackoffCoefficient
    {
        get => _backoffCoefficient;
        private set
        {
            _backoffCoefficient = value;
            _hasBackoffCoefficient = true;
        }
    }

    /// <summary>Gets the projection container partition-key path.</summary>
    [JsonInclude]
    [Newtonsoft.Json.JsonProperty]
    public string PartitionKeyPath
    {
        get => _partitionKeyPath;
        private set
        {
            _partitionKeyPath = string.IsNullOrWhiteSpace(value) || value.StartsWith('/')
                ? value!
                : $"/{value}";
            _hasPartitionKeyPath = true;
        }
    }

    /// <summary>Gets a value indicating whether selected-property replay is enabled.</summary>
    public bool IsSelective => SelectedProperties.Count != 0;

    /// <summary>Calculates the delay for a zero-based retry attempt.</summary>
    public TimeSpan GetBackoff(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        return TimeSpan.FromMilliseconds(
            InitialBackoff.TotalMilliseconds * Math.Pow(BackoffCoefficient, attempt));
    }

    void IJsonOnDeserialized.OnDeserialized() => ValidateDeserializedOptions();

    [System.Runtime.Serialization.OnDeserialized]
    private void OnNewtonsoftJsonDeserialized(System.Runtime.Serialization.StreamingContext context)
        => ValidateDeserializedOptions();

    private void ValidateDeserializedOptions()
    {
        if (!_hasMaxEtagRetries || !_hasInitialBackoff || !_hasBackoffCoefficient || !_hasPartitionKeyPath)
        {
            throw new ArgumentException("Serialized rolling projection options must include all retry settings and the partition key path.");
        }

        ValidateOptions(MaxEtagRetries, InitialBackoff, BackoffCoefficient, PartitionKeyPath);
    }

    private static void ValidateOptions(
        int maxEtagRetries, TimeSpan initialBackoff, double backoffCoefficient, string partitionKeyPath)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEtagRetries);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialBackoff, TimeSpan.Zero);

        if (backoffCoefficient < 1.0 || double.IsNaN(backoffCoefficient) || double.IsInfinity(backoffCoefficient))
        {
            throw new ArgumentOutOfRangeException(nameof(backoffCoefficient));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKeyPath);
    }

    private static string[] NormalizeSelectedProperties(IReadOnlyList<string>? selectedProperties)
        => (selectedProperties ?? [])
            .Select(property => property?.Trim())
            .Where(property => !string.IsNullOrWhiteSpace(property))
            .Select(property => property!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(property => property, StringComparer.Ordinal)
            .ToArray();
}

/// <summary>
/// Serializable input supplied to a rolling Durable orchestration.
/// </summary>
public sealed class DurableRollingProjectionInput
{
    /// <summary>Creates orchestration input with default rolling options.</summary>
    public DurableRollingProjectionInput()
        : this(new DurableRollingProjectionOptions())
    {
    }

    /// <summary>Creates orchestration input.</summary>
    /// <param name="options">Rolling rebuild options, or null to use defaults.</param>
    [JsonConstructor]
    [Newtonsoft.Json.JsonConstructor]
    public DurableRollingProjectionInput(DurableRollingProjectionOptions? options)
    {
        Options = options ?? new DurableRollingProjectionOptions();
    }

    /// <summary>Gets the rolling rebuild options.</summary>
    public DurableRollingProjectionOptions Options { get; }
}

/// <summary>
/// Describes one aggregate and its Cosmos partition key for rolling replay.
/// </summary>
public sealed class DurableRollingProjectionWorkItem
{
    /// <summary>Creates a rolling work item.</summary>
    /// <param name="id">Aggregate and projection identifier.</param>
    /// <param name="partitionKey">Serialized partition-key value.</param>
    /// <param name="partitionKeyIsGuid">Whether the serialized value must be restored as a GUID partition key.</param>
    public DurableRollingProjectionWorkItem(Guid id, string partitionKey, bool partitionKeyIsGuid)
    {
        Id = id;
        PartitionKey = partitionKey ?? throw new ArgumentNullException(nameof(partitionKey));
        PartitionKeyIsGuid = partitionKeyIsGuid;
    }

    /// <summary>Gets the aggregate and projection identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the serialized Cosmos partition-key value.</summary>
    public string PartitionKey { get; }

    /// <summary>Gets whether the partition-key value represents a GUID.</summary>
    public bool PartitionKeyIsGuid { get; }

    /// <summary>Creates the Cosmos partition-key value.</summary>
    public Microsoft.Azure.Cosmos.PartitionKey ToPartitionKey()
        => PartitionKeyIsGuid
            ? Guid.Parse(PartitionKey).ToPartitionKey()
            : new Microsoft.Azure.Cosmos.PartitionKey(PartitionKey);
}

/// <summary>
/// Serializable activity payload for a rolling projection batch.
/// </summary>
public sealed class DurableRollingProjectionBatch
{
    /// <summary>Creates a rolling batch payload.</summary>
    /// <param name="items">Partition-aware work items.</param>
    /// <param name="options">Rolling rebuild options.</param>
    public DurableRollingProjectionBatch(
        IReadOnlyList<DurableRollingProjectionWorkItem> items,
        DurableRollingProjectionOptions options)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Gets the work items in this batch.</summary>
    public IReadOnlyList<DurableRollingProjectionWorkItem> Items { get; }

    /// <summary>Gets the rolling rebuild options.</summary>
    public DurableRollingProjectionOptions Options { get; }
}

/// <summary>
/// Activity names used by tenant-partitioned rolling projection initialization.
/// </summary>
public sealed class DurableRollingTenantInitActivityNames
{
    /// <summary>Creates the rolling activity-name group.</summary>
    public DurableRollingTenantInitActivityNames(string getTenantIds, string getIds, string processBatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(getTenantIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(getIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(processBatch);
        GetTenantIds = getTenantIds;
        GetIds = getIds;
        ProcessBatch = processBatch;
    }

    /// <summary>Gets the distinct-tenant retrieval activity name.</summary>
    public string GetTenantIds { get; }

    /// <summary>Gets the tenant-specific aggregate-ID retrieval activity name.</summary>
    public string GetIds { get; }

    /// <summary>Gets the rolling batch activity name.</summary>
    public string ProcessBatch { get; }
}
