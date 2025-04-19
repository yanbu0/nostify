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

    public override void Apply(Event e)
    {
        UpdateProperties<TestAggregate>(e.payload);
    }
}

public class TestProjection : NostifyObject, IProjection, IHasExternalData<TestProjection>
{
    public Guid id { get; set; }
    public bool initialized { get; set; } = false;
    public string name { get; set; } = string.Empty;

    public static string containerName => "TestProjectionContainer";

    public override void Apply(Event e)
    {
        UpdateProperties<TestProjection>(e.payload);
    }

    public static async Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<TestProjection> projections, INostify nostify, HttpClient? httpClient = null)
    {
        // Mock implementation for fetching external data events
        return await Task.FromResult(new List<ExternalDataEvent>());
    }

    public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<TestProjection> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
    {
        throw new NotImplementedException();
    }
}