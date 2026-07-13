using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

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

        var json = JsonSerializer.Serialize(originalEvent, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
        var deserializedEvent = JsonSerializer.Deserialize<IEvent>(json, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());

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

        var json = JsonSerializer.Serialize(originalSaga, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
        var deserializedSaga = JsonSerializer.Deserialize<ISaga>(json, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());

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

        var json = JsonSerializer.Serialize(saga, options);

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

        var deserialized = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("Test", deserialized["name"]);
        Assert.Equal(42L, deserialized["count"]);
        Assert.Equal(true, deserialized["isActive"]);

        var nested = Assert.IsType<Dictionary<string, object?>>(deserialized["nested"]);
        Assert.Equal(9L, nested["value"]);
    }

    [Fact]
    public void NewtonsoftAndSystemTextJson_CommandSerialization_PerformanceComparison()
    {
        var command = new NostifyCommand("Create_Test", isNew: true, allowNullPayload: true);
        var iterations = 2_000;

        var newtonsoftElapsed = Measure(iterations, () =>
        {
            var json = JsonConvert.SerializeObject(command, SerializationSettings.NostifyDefault);
            var result = JsonConvert.DeserializeObject<NostifyCommand>(json, SerializationSettings.NostifyDefault);
            Assert.NotNull(result);
            Assert.Equal(command.name, result.name);
        });

        var systemTextElapsed = Measure(iterations, () =>
        {
            var json = JsonSerializer.Serialize(command, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
            var result = JsonSerializer.Deserialize<NostifyCommand>(json, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
            Assert.NotNull(result);
            Assert.Equal(command.name, result.name);
        });

        Console.WriteLine($"Command round-trip over {iterations} iterations -> Newtonsoft: {newtonsoftElapsed.TotalMilliseconds:F2} ms, System.Text.Json: {systemTextElapsed.TotalMilliseconds:F2} ms");
        Assert.True(newtonsoftElapsed > TimeSpan.Zero);
        Assert.True(systemTextElapsed > TimeSpan.Zero);
    }

    [Fact]
    public void NewtonsoftAndSystemTextJson_EventPublishing_PerformanceComparison()
    {
        var aggregateId = Guid.NewGuid();
        IEvent evt = new EventFactory().Create<SerializerTestAggregate>(
            new SerializerTestCommand("Create_SerializerTestAggregate", isNew: true),
            aggregateId,
            new SerializerTestAggregate { id = aggregateId, Name = "Publish Test", Value = 77 });
        var iterations = 1_000;

        var newtonsoftElapsed = Measure(iterations, () =>
        {
            var json = JsonConvert.SerializeObject(evt, SerializationSettings.NostifyDefault);
            Assert.Contains("Publish Test", json);
        });

        var systemTextElapsed = Measure(iterations, () =>
        {
            var json = JsonSerializer.Serialize(evt, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
            Assert.Contains("Publish Test", json);
        });

        Console.WriteLine($"Event publish serialization over {iterations} iterations -> Newtonsoft: {newtonsoftElapsed.TotalMilliseconds:F2} ms, System.Text.Json: {systemTextElapsed.TotalMilliseconds:F2} ms");
        Assert.True(newtonsoftElapsed > TimeSpan.Zero);
        Assert.True(systemTextElapsed > TimeSpan.Zero);
    }

    [Fact]
    public void NewtonsoftAndSystemTextJson_EventConsumption_PerformanceComparison()
    {
        var aggregateId = Guid.NewGuid();
        IEvent evt = new EventFactory().Create<SerializerTestAggregate>(
            new SerializerTestCommand("Update_SerializerTestAggregate"),
            aggregateId,
            new SerializerTestAggregate { id = aggregateId, Name = "Consume Test", Value = 91 });
        var newtonsoftJson = JsonConvert.SerializeObject(evt, SerializationSettings.NostifyDefault);
        var systemTextJson = JsonSerializer.Serialize(evt, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
        var iterations = 1_000;

        var newtonsoftElapsed = Measure(iterations, () =>
        {
            var result = JsonConvert.DeserializeObject<IEvent>(newtonsoftJson, SerializationSettings.NostifyDefault);
            Assert.NotNull(result);
            Assert.Equal("Consume Test", result.GetPayload<SerializerTestAggregate>().Name);
        });

        var systemTextElapsed = Measure(iterations, () =>
        {
            var result = JsonSerializer.Deserialize<IEvent>(systemTextJson, WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions());
            Assert.NotNull(result);
            Assert.Equal("Consume Test", result.GetPayload<SerializerTestAggregate>().Name);
        });

        Console.WriteLine($"Event consume deserialization over {iterations} iterations -> Newtonsoft: {newtonsoftElapsed.TotalMilliseconds:F2} ms, System.Text.Json: {systemTextElapsed.TotalMilliseconds:F2} ms");
        Assert.True(newtonsoftElapsed > TimeSpan.Zero);
        Assert.True(systemTextElapsed > TimeSpan.Zero);
    }

    [Fact]
#pragma warning disable NOSTIFY001
    public void UseNostifySystemTextJson_ConfiguresSystemTextWorkerSerializer()
#pragma warning restore NOSTIFY001
    {
        var workerOptions = BuildWorkerOptions(builder => builder.UseNostifySystemTextJson());

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

    private static TimeSpan Measure(int iterations, Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            action();
        }

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }
}
