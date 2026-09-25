using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Proves that container rebuild entry points can replay one stream containing both legacy
/// command-only documents and current EventType documents after a service upgrades.
/// </summary>
public class MixedVersionContainerRebuildTests
{
    [Fact]
    public async Task DurableCurrentStateProcessBatch_RebuildsAndPersistsAggregateFromMixedVersionStream()
    {
        Guid aggregateId = Guid.NewGuid();
        List<Event> persistedEvents = DeserializeMixedVersionStream(aggregateId);
        Mock<Container> eventStore = CosmosTestHelpers.CreateMockContainer(persistedEvents);
        Mock<Container> currentState = CosmosTestHelpers.CreateMockContainer(new List<MixedVersionAggregate>());
        var nostify = new Mock<INostify>();
        nostify.Setup(candidate => candidate.GetEventStoreContainerAsync(false))
            .ReturnsAsync(eventStore.Object);
        nostify.Setup(candidate => candidate.GetBulkCurrentStateContainerAsync<MixedVersionAggregate>("/tenantId"))
            .ReturnsAsync(currentState.Object);

        List<MixedVersionAggregate>? persisted = null;
        var retryable = new Mock<IRetryableContainer>();
        retryable.Setup(candidate => candidate.DoBulkUpsertAsync(
                It.IsAny<List<MixedVersionAggregate>>(),
                It.IsAny<Func<MixedVersionAggregate, Exception, Task>?>()))
            .Callback<List<MixedVersionAggregate>, Func<MixedVersionAggregate, Exception, Task>?>(
                (items, _) => persisted = items.ToList())
            .Returns(Task.CompletedTask);
        var initializer = new DurableCurrentStateInitializer<MixedVersionAggregate>(
            nostify.Object,
            "mixed-version-current-state",
            "/tenantId",
            batchSize: 10,
            concurrentBatchCount: 1,
            durableTaskOptions: null,
            cosmosRetryOptions: null,
            InMemoryQueryExecutor.Default,
            (_, _) => retryable.Object);

        await initializer.ProcessBatch(new List<Guid> { aggregateId });

        MixedVersionAggregate rebuilt = Assert.Single(persisted!);
        Assert.Equal(aggregateId, rebuilt.id);
        Assert.Equal("current-update", rebuilt.name);
        Assert.Equal(2, rebuilt.appliedEventCount);
        Assert.IsType<LegacyNostifyCommandEventType>(persistedEvents[0].eventType);
        Assert.IsType<Update_MixedVersionRebuild>(persistedEvents[1].eventType);
    }

    [Fact]
    public async Task DurableProjectionProcessBatch_RebuildsInitializesAndPersistsProjectionFromMixedVersionStream()
    {
        Guid aggregateId = Guid.NewGuid();
        List<Event> persistedEvents = DeserializeMixedVersionStream(aggregateId);
        Mock<Container> eventStore = CosmosTestHelpers.CreateMockContainer(persistedEvents);
        var projectionContainer = new Mock<Container>();
        var nostify = new Mock<INostify>();
        nostify.Setup(candidate => candidate.GetEventStoreContainerAsync(false))
            .ReturnsAsync(eventStore.Object);
        nostify.Setup(candidate => candidate.GetBulkProjectionContainerAsync<MixedVersionProjection>("/tenantId"))
            .ReturnsAsync(projectionContainer.Object);

        List<MixedVersionProjection>? persisted = null;
        var retryable = new Mock<IRetryableContainer>();
        retryable.Setup(candidate => candidate.DoBulkUpsertAsync(
                It.IsAny<List<MixedVersionProjection>>(),
                It.IsAny<Func<MixedVersionProjection, Exception, Task>?>()))
            .Callback<List<MixedVersionProjection>, Func<MixedVersionProjection, Exception, Task>?>(
                (items, _) => persisted = items.ToList())
            .Returns(Task.CompletedTask);
        var projectionInitializer = new ProjectionInitializer(
            InMemoryQueryExecutor.Default,
            (_, _) => retryable.Object,
            _ => Task.CompletedTask);
        nostify.SetupGet(candidate => candidate.ProjectionInitializer)
            .Returns(projectionInitializer);

        var initializer = new DurableProjectionInitializer<MixedVersionProjection, MixedVersionAggregate>(
            new HttpClient(),
            nostify.Object,
            "mixed-version-projection",
            batchSize: 10,
            concurrentBatchCount: 1,
            durableTaskOptions: null,
            cosmosRetryOptions: null,
            InMemoryQueryExecutor.Default);

        await initializer.ProcessBatch(new List<Guid> { aggregateId });

        MixedVersionProjection rebuilt = Assert.Single(persisted!);
        Assert.Equal(aggregateId, rebuilt.id);
        Assert.Equal("current-update", rebuilt.name);
        Assert.Equal(2, rebuilt.appliedEventCount);
        Assert.True(rebuilt.initialized);
        nostify.VerifyGet(candidate => candidate.ProjectionInitializer, Times.Once);
    }

    /// <summary>
    /// Simulates the Cosmos serializer by independently deserializing the two persisted document
    /// shapes before they are exposed through the mocked event-store container.
    /// </summary>
    private static List<Event> DeserializeMixedVersionStream(Guid aggregateId)
    {
        string legacyJson = $$"""
            {
              "aggregateRootId": "{{aggregateId}}",
              "timestamp": "2025-01-01T00:00:00Z",
              "command": {
                "name": "Create_MixedVersionRebuild",
                "isNew": true,
                "allowNullPayload": false
              },
              "payload": {
                "id": "{{aggregateId}}",
                "name": "legacy-create"
              }
            }
            """;
        string currentJson = $$"""
            {
              "aggregateRootId": "{{aggregateId}}",
              "timestamp": "2025-01-01T00:01:00Z",
              "eventType": {
                "name": "Update_MixedVersionRebuild",
                "isNew": false,
                "allowNullPayload": false
              },
              "schemaVersion": 2,
              "payload": {
                "id": "{{aggregateId}}",
                "name": "current-update"
              }
            }
            """;

        return new List<Event>
        {
            JsonConvert.DeserializeObject<Event>(legacyJson, SerializationSettings.NostifyDefault)!,
            JsonConvert.DeserializeObject<Event>(currentJson, SerializationSettings.NostifyDefault)!
        };
    }

    public sealed class Create_MixedVersionRebuild : EventType
    {
        public static Create_MixedVersionRebuild Instance { get; } = new();

        public Create_MixedVersionRebuild()
            : base("Create_MixedVersionRebuild", true, false)
        {
        }
    }

    public sealed class Update_MixedVersionRebuild : EventType
    {
        public static Update_MixedVersionRebuild Instance { get; } = new();

        public Update_MixedVersionRebuild()
            : base("Update_MixedVersionRebuild", false, false)
        {
        }
    }

    public sealed class MixedVersionAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => "MixedVersionAggregate";
        public static string currentStateContainerName => "MixedVersionAggregateCurrentState";
        public bool isDeleted { get; set; }
        public string name { get; set; } = string.Empty;
        public int appliedEventCount { get; set; }

        [ApplyEvents(typeof(Create_MixedVersionRebuild))]
        [ApplyEvents(typeof(Update_MixedVersionRebuild))]
        private void ApplyStateChange(IEvent eventToApply)
        {
            ApplyPayload(eventToApply);
        }

        private void ApplyPayload(IEvent eventToApply)
        {
            JObject payload = JObject.FromObject(eventToApply.payload!);
            id = Guid.Parse(payload.Value<string>(nameof(id))!);
            name = payload.Value<string>(nameof(name)) ?? name;
            appliedEventCount++;
        }
    }

    public sealed class MixedVersionProjection : NostifyObject, IProjection, IHasExternalData<MixedVersionProjection>
    {
        public static string containerName => "MixedVersionProjection";
        public bool initialized { get; set; }
        public string name { get; set; } = string.Empty;
        public int appliedEventCount { get; set; }

        [ApplyEvents(typeof(Create_MixedVersionRebuild))]
        [ApplyEvents(typeof(Update_MixedVersionRebuild))]
        private void ApplyStateChange(IEvent eventToApply)
        {
            JObject payload = JObject.FromObject(eventToApply.payload!);
            id = Guid.Parse(payload.Value<string>(nameof(id))!);
            name = payload.Value<string>(nameof(name)) ?? name;
            appliedEventCount++;
        }

        /// <summary>
        /// Mirrors the template initialization contract. This projection has no additional foreign
        /// data, so initialization returns no external events before the rebuilt state is persisted.
        /// </summary>
        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
            List<MixedVersionProjection> projectionsToInit,
            INostify nostify,
            HttpClient? httpClient = null,
            DateTime? pointInTime = null)
            => Task.FromResult(new List<ExternalDataEvent>());
    }
}
