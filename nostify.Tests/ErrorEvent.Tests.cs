using Newtonsoft.Json;
using System.Text.Json;
using Xunit;
using STJ = System.Text.Json.JsonSerializer;

namespace nostify.Tests;

/// <summary>
/// Verifies error-event and undeliverable-event data contracts.
/// </summary>
public sealed class ErrorEventTests
{
    [Fact]
    public void ErrorEventTypes_ExposeStableWireNames()
    {
        Assert.Equal("Error_BulkCreate", new BulkCreateErrorEventType().name);
        Assert.Equal("Error_BulkUpsert", new BulkUpsertErrorEventType().name);
        Assert.Equal("Error_BulkPersistEvent", new BulkPersistErrorEventType().name);
        Assert.Equal("Error_BulkApplyAndPersist", new BulkApplyAndPersistErrorEventType().name);
        Assert.Equal("Error_HandleProjection", new HandleProjectionErrorEventType().name);
        Assert.Equal("Error_HandleAggregateEvent", new HandleAggregateEventErrorEventType().name);
        Assert.Equal("Error_HandleMultiApplyEvent", new HandleMultiApplyEventErrorEventType().name);
    }

    [Fact]
#pragma warning disable CS0618 // Verifies the promised source-compatibility shim.
    public void ErrorCommands_ExposeStableWireNames()
    {
        Assert.Equal("Error_BulkCreate", ErrorCommand.BulkCreate.name);
        Assert.Equal("Error_BulkUpsert", ErrorCommand.BulkUpsert.name);
        Assert.Equal("Error_BulkPersistEvent", ErrorCommand.BulkPersistEvent.name);
        Assert.Equal("Error_BulkApplyAndPersist", ErrorCommand.BulkApplyAndPersist.name);
        Assert.Equal("Error_HandleProjection", ErrorCommand.HandleProjection.name);
        Assert.Equal("Error_HandleAggregateEvent", ErrorCommand.HandleAggregateEvent.name);
        Assert.Equal("Error_HandleMultiApplyEvent", ErrorCommand.HandleMultiApplyEvent.name);
    }
#pragma warning restore CS0618

    [Fact]
    public void ErrorPayload_DefaultConstructor_CreatesSerializationSafeValues()
    {
        var payload = new ErrorPayload();

        Assert.Empty(payload.ErrorMessage);
        Assert.Null(payload.StackTrace);
        Assert.NotNull(payload.Payload);
    }

    [Fact]
    public void ErrorPayload_ParameterizedConstructor_PreservesValues()
    {
        var failedPayload = new { value = 42 };

        var payload = new ErrorPayload("failure", failedPayload, "trace");

        Assert.Equal("failure", payload.ErrorMessage);
        Assert.Equal("trace", payload.StackTrace);
        Assert.Same(failedPayload, payload.Payload);
    }

    [Fact]
    public void NostifyErrorEvent_PreservesEventTypeAndContext()
    {
        Guid aggregateRootId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid partitionKey = Guid.NewGuid();
        var payload = new ErrorPayload("failure", new { value = 42 });

        var errorEvent = new NostifyErrorEvent(
            new BulkPersistErrorEventType(),
            aggregateRootId,
            payload,
            userId,
            partitionKey);

        Assert.IsType<BulkPersistErrorEventType>(errorEvent.eventType);
        Assert.Equal(2, errorEvent.schemaVersion);
        Assert.Equal(aggregateRootId, errorEvent.aggregateRootId);
        Assert.Equal(userId, errorEvent.userId);
        Assert.Equal(partitionKey, errorEvent.partitionKey);
        Assert.Same(payload, errorEvent.payload);
    }

    [Fact]
#pragma warning disable CS0618 // Verifies that legacy source calls author modern envelopes.
    public void NostifyErrorEvent_LegacyBuiltInCommand_AuthorsCanonicalSchemaVersionTwoEvent()
    {
        var errorEvent = new NostifyErrorEvent(
            ErrorCommand.BulkPersistEvent,
            Guid.NewGuid(),
            new ErrorPayload("failure", new { value = 42 }),
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.IsType<BulkPersistErrorEventType>(errorEvent.eventType);
        Assert.Equal(2, errorEvent.schemaVersion);
    }
#pragma warning restore CS0618

    [Fact]
#pragma warning disable CS0618 // Verifies custom ErrorCommand source compatibility.
    public void NostifyErrorEvent_CustomLegacyCommand_AuthorsSchemaVersionTwoAndPreservesMetadata()
    {
        var errorEvent = new NostifyErrorEvent(
            new ErrorCommand("Error_Custom", isNew: true),
            Guid.NewGuid(),
            new ErrorPayload("failure", new { value = 42 }),
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.IsAssignableFrom<ErrorEventType>(errorEvent.eventType);
        Assert.Equal("Error_Custom", errorEvent.eventType.name);
        Assert.True(errorEvent.eventType.isNew);
        Assert.Equal(2, errorEvent.schemaVersion);
    }
#pragma warning restore CS0618

    [Fact]
    public void NostifyErrorEvent_BuiltInType_RoundTripsToCanonicalTypeWithBothSerializers()
    {
        var original = new NostifyErrorEvent(
            new BulkPersistErrorEventType(),
            Guid.NewGuid(),
            new ErrorPayload("failure", new { value = 42 }),
            Guid.NewGuid(),
            Guid.NewGuid());

        string newtonsoftJson = JsonConvert.SerializeObject(original, SerializationSettings.NostifyDefault);
        Event? newtonsoftResult = JsonConvert.DeserializeObject<Event>(newtonsoftJson, SerializationSettings.NostifyDefault);
        Assert.IsType<BulkPersistErrorEventType>(newtonsoftResult?.eventType);

        JsonSerializerOptions options = WorkerConfigurationExtensions.CreateNostifyDefaultSystemTextJsonOptions();
        string systemTextJson = STJ.Serialize<Event>(original, options);
        Event? systemTextResult = STJ.Deserialize<Event>(systemTextJson, options);
        Assert.IsType<BulkPersistErrorEventType>(systemTextResult?.eventType);
    }

    [Fact]
#pragma warning disable CS0618 // Verifies the documented custom-name round-trip boundary.
    public void NostifyErrorEvent_CustomCommand_RoundTripPreservesWireMetadataInLegacyAdapter()
    {
        var original = new NostifyErrorEvent(
            new ErrorCommand("Error_CustomRoundTrip", isNew: true),
            Guid.NewGuid(),
            new ErrorPayload("failure", new { value = 42 }),
            Guid.NewGuid(),
            Guid.NewGuid());

        string json = JsonConvert.SerializeObject(original, SerializationSettings.NostifyDefault);
        Event? result = JsonConvert.DeserializeObject<Event>(json, SerializationSettings.NostifyDefault);

        var eventType = Assert.IsType<LegacyNostifyCommandEventType>(result?.eventType);
        Assert.Equal("Error_CustomRoundTrip", eventType.name);
        Assert.True(eventType.isNew);
    }
#pragma warning restore CS0618

    [Fact]
    public void UndeliverableEvent_PreservesFailureAndGeneratesIdentity()
    {
        Event failedEvent = CreateEvent();

        var undeliverable = new UndeliverableEvent("Handler", "failure", failedEvent);

        Assert.NotEqual(Guid.Empty, undeliverable.id);
        Assert.Equal("Handler", undeliverable.functionName);
        Assert.Equal("failure", undeliverable.errorMessage);
        Assert.Same(failedEvent, undeliverable.undeliverableEvent);
        Assert.Equal(failedEvent.aggregateRootId, undeliverable.aggregateRootId);
    }

    private static Event CreateEvent()
    {
        return new Event(
            new ErrorTestEventType(),
            Guid.NewGuid(),
            new { value = 42 },
            userId: Guid.NewGuid(),
            partitionKey: Guid.NewGuid());
    }

    private sealed class ErrorTestEventType : EventType
    {
        public ErrorTestEventType()
            : base("ErrorTest", isNew: false)
        {
        }
    }
}
