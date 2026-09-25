namespace nostify.IntegrationTestModels;

/// <summary>A minimal aggregate used to isolate generic Kafka topic discovery.</summary>
public sealed class KafkaDiscoveryAggregate : NostifyObject, IAggregate
{
    /// <summary>Gets the stable aggregate type used by request/response topics.</summary>
    public static string aggregateType => "NostifyIntegrationDiscovery";

    /// <summary>Gets the future Cosmos current-state container name.</summary>
    public static string currentStateContainerName => "NostifyIntegrationDiscoveryCurrentState";

    /// <summary>Gets or sets whether the test aggregate is deleted.</summary>
    public bool isDeleted { get; set; }

    /// <inheritdoc />
    protected override void Apply(EventType eventType, IEvent eventToApply)
    {
        // Topic discovery tests never apply events; the implementation satisfies the aggregate contract.
    }
}

/// <summary>The first logical topic discovered from the isolated model assembly.</summary>
public sealed class Create_NostifyIntegrationDiscovery : EventType
{
    /// <summary>Initializes the event metadata used by reflection-based discovery.</summary>
    public Create_NostifyIntegrationDiscovery()
        : base("Create_NostifyIntegrationDiscovery", isNew: true)
    {
    }
}

/// <summary>The second logical topic discovered from the isolated model assembly.</summary>
public sealed class Update_NostifyIntegrationDiscovery : EventType
{
    /// <summary>Initializes the event metadata used by reflection-based discovery.</summary>
    public Update_NostifyIntegrationDiscovery()
        : base("Update_NostifyIntegrationDiscovery")
    {
    }
}
