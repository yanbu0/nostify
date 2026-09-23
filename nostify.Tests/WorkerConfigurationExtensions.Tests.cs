using System;
using System.Collections.Generic;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SystemTextJsonSerializer = System.Text.Json.JsonSerializer;

namespace nostify.Tests;

public class WorkerConfigurationExtensionsTests
{
    [Fact]
    public void UseNostifyDefaultJson_ConfiguresNewtonsoftWorkerSerializer()
    {
        var workerOptions = BuildWorkerOptions(builder => builder.UseNostifyDefaultJson());

        Assert.NotNull(workerOptions.Serializer);
        Assert.IsType<NewtonsoftJsonObjectSerializer>(workerOptions.Serializer);
    }

    [Fact]
    public void UseNostifyDefaultConfiguredNewtonsoftJson_ConfiguresNewtonsoftWorkerSerializer()
    {
        var workerOptions = BuildWorkerOptions(builder => builder.UseNostifyDefaultConfiguredNewtonsoftJson());

        Assert.NotNull(workerOptions.Serializer);
        Assert.IsType<NewtonsoftJsonObjectSerializer>(workerOptions.Serializer);
    }

    [Fact]
    public void SystemTextJsonOptions_RoundTripIEvent_Succeeds()
    {
        var aggregateId = Guid.NewGuid();
        IEvent originalEvent = new EventFactory().Create<SerializerTestAggregate>(
            new SerializerTestCommand("Create_SerializerTestAggregate", isNew: true),
            aggregateId,
            new SerializerTestAggregate { id = aggregateId, Name = "Test Aggregate", Value = 123 });

        var json = SystemTextJsonSerializer.Serialize(originalEvent, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
        var deserializedEvent = SystemTextJsonSerializer.Deserialize<IEvent>(json, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());

        Assert.NotNull(deserializedEvent);
        Assert.IsType<Event>(deserializedEvent);
        Assert.Equal(originalEvent.aggregateRootId, deserializedEvent.aggregateRootId);
        Assert.Equal(originalEvent.command.name, deserializedEvent.command.name);

        var deserializedPayload = deserializedEvent.GetPayload<SerializerTestAggregate>();
        Assert.Equal(aggregateId, deserializedPayload.id);
        Assert.Equal("Test Aggregate", deserializedPayload.Name);
        Assert.Equal(123, deserializedPayload.Value);
    }

    [Fact]
    public void SystemTextJsonOptions_RoundTripISaga_Succeeds()
    {
        var triggerEvent = new Event(
            new NostifyCommand("TriggerCommand", isNew: true),
            Guid.NewGuid(),
            new SerializerTestAggregate { id = Guid.NewGuid(), Name = "Trigger", Value = 1 });
        var rollbackEvent = new Event(
            new NostifyCommand("RollbackCommand"),
            Guid.NewGuid(),
            new SerializerTestAggregate { id = Guid.NewGuid(), Name = "Rollback", Value = 2 });
        ISaga originalSaga = new Saga("TestSaga", new List<SagaStep> { new SagaStep(1, triggerEvent, rollbackEvent) });

        var json = SystemTextJsonSerializer.Serialize(originalSaga, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
        var deserializedSaga = SystemTextJsonSerializer.Deserialize<ISaga>(json, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());

        Assert.NotNull(deserializedSaga);
        Assert.IsType<Saga>(deserializedSaga);
        Assert.Single(deserializedSaga.steps);
        Assert.IsType<Event>(deserializedSaga.steps[0].stepEvent);
        Assert.IsType<Event>(deserializedSaga.steps[0].rollbackEvent);
        Assert.Equal(triggerEvent.command.name, deserializedSaga.steps[0].stepEvent.command.name);
        Assert.Equal(rollbackEvent.command.name, deserializedSaga.steps[0].rollbackEvent?.command.name);
    }

    [Fact]
    public void SystemTextJsonOptions_PreservesNullValuesAndStringEnums()
    {
        var options = WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions();
        var saga = new Saga("NullSaga")
        {
            errorMessage = null,
            rollbackErrorMessage = null,
            status = SagaStatus.Pending
        };

        var json = SystemTextJsonSerializer.Serialize(saga, options);

        Assert.Contains("\"errorMessage\":null", json);
        Assert.Contains("\"rollbackErrorMessage\":null", json);
        Assert.Contains("\"status\":\"Pending\"", json);
    }

    [Fact]
    public void SystemTextJsonOptions_ObjectPayloadRoundTrip_UsesInferredTypes()
    {
        var options = WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions();
        var json = """
        {
            "name": "Test",
            "count": 42,
            "isActive": true,
            "nested": {
                "value": 9
            }
        }
        """;

        var deserialized = SystemTextJsonSerializer.Deserialize<Dictionary<string, object?>>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("Test", deserialized["name"]);
        Assert.Equal(42L, deserialized["count"]);
        Assert.Equal(true, deserialized["isActive"]);

        var nested = Assert.IsType<Dictionary<string, object?>>(deserialized["nested"]);
        Assert.Equal(9L, nested["value"]);
    }

    [Fact]
    public void SystemTextJsonOptions_ObjectPayloadRoundTrip_InfersAllPrimitiveAndCollectionTypes()
    {
        var options = WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions();
        var json = """
        {
            "falseValue": false,
            "decimalValue": 12.5,
            "doubleValue": 1e100,
            "dateValue": "2026-09-23T00:00:00Z",
            "nullValue": null,
            "items": [1, "two"]
        }
        """;

        Dictionary<string, object?>? result = SystemTextJsonSerializer.Deserialize<Dictionary<string, object?>>(json, options);

        Assert.NotNull(result);
        Assert.Equal(false, result["falseValue"]);
        Assert.Equal(12.5m, result["decimalValue"]);
        Assert.IsType<double>(result["doubleValue"]);
        Assert.Equal(new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc), result["dateValue"]);
        Assert.Null(result["nullValue"]);
        List<object?> items = Assert.IsType<List<object?>>(result["items"]);
        Assert.Equal(1L, items[0]);
        Assert.Equal("two", items[1]);
    }

    [Fact]
    public void SystemTextJsonOptions_SerializingJsonElement_WritesElementContent()
    {
        var options = WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions();
        using var document = System.Text.Json.JsonDocument.Parse("{\"value\":42}");
        object element = document.RootElement.Clone();

        string json = SystemTextJsonSerializer.Serialize(element, options);

        Assert.Equal("{\"value\":42}", json);
    }

    [Fact]
#pragma warning disable NOSTIFY001
    public void UseNostifySystemTextJson_ConfiguresSystemTextWorkerSerializer()
#pragma warning restore NOSTIFY001
    {
#pragma warning disable NOSTIFY001
        var workerOptions = BuildWorkerOptions(builder => builder.UseNostifySystemTextJson());
#pragma warning restore NOSTIFY001

        Assert.NotNull(workerOptions.Serializer);
        Assert.IsType<JsonObjectSerializer>(workerOptions.Serializer);
    }

    private static WorkerOptions BuildWorkerOptions(Action<IFunctionsWorkerApplicationBuilder> configureWorker)
    {
        using var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults(configureWorker)
            .Build();

        return host.Services.GetRequiredService<IOptions<WorkerOptions>>().Value;
    }

}
