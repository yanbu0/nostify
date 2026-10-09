using System;
using System.Collections.Generic;
using nostify;

namespace nostify.Tests;

public class TestAggregate : NostifyObject, IAggregate
{
    public static string aggregateType => "TestAggregate";

    public static string currentStateContainerName => "TestAggregateCurrentState";

    public bool isDeleted { get; set; } = false;
    public string name { get; set; } = "Test1";

    protected override void Apply(EventType eventType, IEvent e)
    {
        UpdateProperties<TestAggregate>(e.payload);
    }
}

public class TestProjection : NostifyObject, IProjection, IHasExternalData<TestProjection>
{
    public bool initialized { get; set; } = false;
    public string name { get; set; } = string.Empty;

    public static string containerName => "TestProjectionContainer";

    protected override void Apply(EventType eventType, IEvent e)
    {
        UpdateProperties<TestProjection>(e.payload);
    }

    public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
        List<TestProjection> projectionsToInit,
        INostify nostify,
        HttpClient? httpClient = null,
        DateTime? pointInTime = null)
    {
        // The shared test projection has no external dependencies. Returning an empty result from
        // the interface's canonical overload also lets rolling replay tests exercise this path.
        return Task.FromResult(new List<ExternalDataEvent>());
    }
}