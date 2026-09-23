using Xunit;

namespace nostify.Tests;

/// <summary>
/// Verifies error-event and undeliverable-event data contracts.
/// </summary>
public sealed class ErrorEventTests
{
    [Fact]
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
    public void NostifyErrorEvent_PreservesCommandMetadataAndContext()
    {
        Guid aggregateRootId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid partitionKey = Guid.NewGuid();
        var payload = new ErrorPayload("failure", new { value = 42 });

        var errorEvent = new NostifyErrorEvent(
            ErrorCommand.BulkPersistEvent,
            aggregateRootId,
            payload,
            userId,
            partitionKey);

        // Legacy error commands are converted to EventType instances; the
        // stable wire metadata, rather than object identity, is the contract.
        Assert.Equal(ErrorCommand.BulkPersistEvent.name, errorEvent.eventType.name);
        Assert.Equal(aggregateRootId, errorEvent.aggregateRootId);
        Assert.Equal(userId, errorEvent.userId);
        Assert.Equal(partitionKey, errorEvent.partitionKey);
        Assert.Same(payload, errorEvent.payload);
    }

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
