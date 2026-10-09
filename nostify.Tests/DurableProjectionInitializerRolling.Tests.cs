using System.Net;
using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Exercises the rolling projection initializer's Cosmos persistence and replay paths.
/// </summary>
public sealed class DurableProjectionInitializerRollingTests
{
    [Fact]
    public async Task ProcessRollingBatch_SelectiveExistingDocument_UpdatesOnlySelectedProperties()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "rebuilt", description = "event-description" });
        var existing = new RollingProjection
        {
            id = id,
            name = "old-name",
            description = "preserve-me",
            initialized = false
        };
        var projectionContainer = new Mock<Container>();
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(existing, "etag-1"));

        RollingProjection? replaced = null;
        ItemRequestOptions? requestOptions = null;
        projectionContainer
            .Setup(container => container.ReplaceItemAsync(
                It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<RollingProjection, string, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (projection, _, _, options, _) =>
                {
                    replaced = projection;
                    requestOptions = options;
                })
            .ReturnsAsync(CreateResponse(new RollingProjection()));

        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer([source], projectionContainer);

        await initializer.ProcessRollingBatch(CreateBatch(id, [nameof(RollingProjection.name)]));

        Assert.NotNull(replaced);
        Assert.Equal("rebuilt", replaced!.name);
        Assert.Equal("preserve-me", replaced.description);
        Assert.False(replaced.initialized);
        Assert.Equal("etag-1", requestOptions!.IfMatchEtag);
        JObject originalPayload = JObject.FromObject(source.payload!);
        Assert.Equal("event-description", originalPayload.Value<string>(nameof(RollingProjection.description)));
        projectionContainer.Verify(container => container.PatchItemAsync<RollingProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<IReadOnlyList<PatchOperation>>(),
            It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        projectionContainer.Verify(container => container.UpsertItemAsync(
            It.IsAny<RollingProjection>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRollingBatch_FullExistingDocument_ConditionallyReplacesCompleteProjection()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "rebuilt", description = "rebuilt-description" });
        var projectionContainer = new Mock<Container>();
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(new RollingProjection { id = id }, "etag-full"));

        RollingProjection? replaced = null;
        ItemRequestOptions? requestOptions = null;
        projectionContainer
            .Setup(container => container.ReplaceItemAsync(
                It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<RollingProjection, string, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (projection, _, _, options, _) =>
                {
                    replaced = projection;
                    requestOptions = options;
                })
            .ReturnsAsync(CreateResponse(new RollingProjection()));

        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer([source], projectionContainer);

        await initializer.ProcessRollingBatch(CreateBatch(id));

        Assert.NotNull(replaced);
        Assert.Equal("rebuilt", replaced!.name);
        Assert.Equal("rebuilt-description", replaced.description);
        Assert.True(replaced.initialized);
        Assert.Equal("etag-full", requestOptions!.IfMatchEtag);
    }

    [Fact]
    public async Task ProcessRollingBatch_ConflictThenSuccess_RereadsAndUsesLatestEtag()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "rebuilt" });
        var projectionContainer = new Mock<Container>();
        int readCount = 0;
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateResponse(
                new RollingProjection { id = id, description = $"live-{readCount}" },
                $"etag-{++readCount}"));

        var attemptedEtags = new List<string?>();
        int replaceCount = 0;
        projectionContainer
            .Setup(container => container.ReplaceItemAsync(
                It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<RollingProjection, string, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (_, _, _, options, _) => attemptedEtags.Add(options.IfMatchEtag))
            .Returns<RollingProjection, string, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (_, _, _, _, _) => ++replaceCount == 1
                    ? Task.FromException<ItemResponse<RollingProjection>>(Conflict(HttpStatusCode.PreconditionFailed))
                    : Task.FromResult(CreateResponse(new RollingProjection())));

        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer([source], projectionContainer);
        DurableRollingProjectionBatch batch = CreateBatch(
            id,
            [nameof(RollingProjection.name)],
            maxEtagRetries: 1);

        await initializer.ProcessRollingBatch(batch);

        Assert.Equal(2, readCount);
        Assert.Equal(2, replaceCount);
        Assert.Equal(["etag-1", "etag-2"], attemptedEtags);
        projectionContainer.Verify(container => container.PatchItemAsync<RollingProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<IReadOnlyList<PatchOperation>>(),
            It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRollingBatch_SelectiveConflictsExhausted_PerformsUnconditionalSelectedPatch()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "final-name", description = "must-not-patch" });
        var projectionContainer = new Mock<Container>();
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(
                new RollingProjection { id = id, name = "old", description = "live-value", initialized = false },
                "etag"));
        projectionContainer
            .Setup(container => container.ReplaceItemAsync(
                It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Conflict(HttpStatusCode.PreconditionFailed));

        IReadOnlyList<PatchOperation>? operations = null;
        PatchItemRequestOptions? patchOptions = new PatchItemRequestOptions();
        projectionContainer
            .Setup(container => container.PatchItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, PartitionKey, IReadOnlyList<PatchOperation>, PatchItemRequestOptions, CancellationToken>(
                (_, _, captured, options, _) =>
                {
                    operations = captured;
                    patchOptions = options;
                })
            .ReturnsAsync(CreateResponse(new RollingProjection()));

        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer([source], projectionContainer);

        await initializer.ProcessRollingBatch(CreateBatch(
            id,
            [nameof(RollingProjection.name)],
            maxEtagRetries: 1));

        projectionContainer.Verify(container => container.ReplaceItemAsync(
            It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Null(patchOptions);
        PatchOperation operation = Assert.Single(operations!);
        Assert.Equal("/name", operation.Path);
        Assert.DoesNotContain(operations!, item => item.Path == "/description" || item.Path == "/initialized");
    }

    [Fact]
    public async Task ProcessRollingBatch_FullConflictExhausted_PerformsUnconditionalUpsert()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "final-name", description = "final-description" });
        var projectionContainer = new Mock<Container>();
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(new RollingProjection { id = id }, "etag"));
        projectionContainer
            .Setup(container => container.ReplaceItemAsync(
                It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Conflict(HttpStatusCode.Conflict));

        RollingProjection? upserted = null;
        ItemRequestOptions? upsertOptions = new ItemRequestOptions();
        projectionContainer
            .Setup(container => container.UpsertItemAsync(
                It.IsAny<RollingProjection>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<RollingProjection, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (projection, _, options, _) =>
                {
                    upserted = projection;
                    upsertOptions = options;
                })
            .ReturnsAsync(CreateResponse(new RollingProjection()));

        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer([source], projectionContainer);

        await initializer.ProcessRollingBatch(CreateBatch(id, maxEtagRetries: 0));

        Assert.NotNull(upserted);
        Assert.Equal("final-name", upserted!.name);
        Assert.Equal("final-description", upserted.description);
        Assert.True(upserted.initialized);
        Assert.Null(upsertOptions);
        projectionContainer.Verify(container => container.ReplaceItemAsync(
            It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        projectionContainer.Verify(container => container.UpsertItemAsync(
            It.IsAny<RollingProjection>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessRollingBatch_SelectiveMissingDocument_CreatesFullyReconstructedProjection()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "selected", description = "required-full-state" });
        var projectionContainer = new Mock<Container>();
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("missing", HttpStatusCode.NotFound, 0, string.Empty, 0));

        RollingProjection? created = null;
        projectionContainer
            .Setup(container => container.CreateItemAsync(
                It.IsAny<RollingProjection>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<RollingProjection, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (projection, _, _, _) => created = projection)
            .ReturnsAsync(CreateResponse(new RollingProjection()));

        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer([source], projectionContainer);

        await initializer.ProcessRollingBatch(CreateBatch(id, [nameof(RollingProjection.name)]));

        Assert.NotNull(created);
        Assert.Equal("selected", created!.name);
        Assert.Equal("required-full-state", created.description);
        Assert.True(created.initialized);
        projectionContainer.Verify(container => container.CreateItemAsync(
            It.IsAny<RollingProjection>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessRollingBatch_SelectiveMissingDocument_PerformsSingleReplaySetup()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "selected", description = "required-full-state" });
        int externalLookupCount = 0;
        RollingProjection.ExternalEventsFactory = _ =>
        {
            externalLookupCount++;
            return
            [
                new ExternalDataEvent(id,
                [
                    CreateEvent(id, new { id, history = "external" }, timestamp: DateTime.UnixEpoch.AddSeconds(5))
                ])
            ];
        };

        try
        {
            var projectionContainer = new Mock<Container>();
            projectionContainer
                .Setup(container => container.ReadItemAsync<RollingProjection>(
                    id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CosmosException("missing", HttpStatusCode.NotFound, 0, string.Empty, 0));

            RollingProjection? created = null;
            projectionContainer
                .Setup(container => container.CreateItemAsync(
                    It.IsAny<RollingProjection>(), It.IsAny<PartitionKey>(),
                    It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
                .Callback<RollingProjection, PartitionKey?, ItemRequestOptions, CancellationToken>(
                    (projection, _, _, _) => created = projection)
                .ReturnsAsync(CreateResponse(new RollingProjection()));

            DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
                CreateInitializer([source], projectionContainer, out Mock<INostify> nostify);

            await initializer.ProcessRollingBatch(CreateBatch(id, [nameof(RollingProjection.name)]));

            Assert.NotNull(created);
            Assert.Equal("selected", created!.name);
            Assert.Equal("required-full-state", created.description);
            Assert.Equal("external", created.history);
            Assert.True(created.initialized);
            Assert.Equal(1, externalLookupCount);
            nostify.Verify(value => value.GetEventStoreContainerAsync(It.IsAny<bool>()), Times.Once);
        }
        finally
        {
            RollingProjection.ExternalEventsFactory = null;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessRollingBatch_ExistingDocument_PerformsSingleReplaySetup(bool selective)
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "rebuilt", description = "event-description" });
        int externalLookupCount = 0;
        RollingProjection.ExternalEventsFactory = _ =>
        {
            externalLookupCount++;
            return [];
        };

        try
        {
            var projectionContainer = new Mock<Container>();
            projectionContainer
                .Setup(container => container.ReadItemAsync<RollingProjection>(
                    id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateResponse(
                    new RollingProjection { id = id, name = "old", description = "preserve-me", initialized = false },
                    "etag"));

            RollingProjection? replaced = null;
            projectionContainer
                .Setup(container => container.ReplaceItemAsync(
                    It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                    It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
                .Callback<RollingProjection, string, PartitionKey?, ItemRequestOptions, CancellationToken>(
                    (projection, _, _, _, _) => replaced = projection)
                .ReturnsAsync(CreateResponse(new RollingProjection()));

            DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
                CreateInitializer([source], projectionContainer, out Mock<INostify> nostify);

            await initializer.ProcessRollingBatch(CreateBatch(
                id,
                selective ? [nameof(RollingProjection.name)] : null));

            Assert.NotNull(replaced);
            Assert.Equal("rebuilt", replaced!.name);
            Assert.Equal(selective ? "preserve-me" : "event-description", replaced.description);
            Assert.Equal(!selective, replaced.initialized);
            Assert.Equal(1, externalLookupCount);
            nostify.Verify(value => value.GetEventStoreContainerAsync(It.IsAny<bool>()), Times.Once);
            projectionContainer.Verify(container => container.CreateItemAsync(
                It.IsAny<RollingProjection>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            RollingProjection.ExternalEventsFactory = null;
        }
    }

    [Fact]
    public async Task ProcessRollingBatch_ThrottleAndConflictRetries_AreIndependent()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "rebuilt" });
        var projectionContainer = new Mock<Container>();
        int readCount = 0;
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateResponse(new RollingProjection { id = id }, $"etag-{++readCount}"));

        int replaceCount = 0;
        projectionContainer
            .Setup(container => container.ReplaceItemAsync(
                It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns<RollingProjection, string, PartitionKey?, ItemRequestOptions, CancellationToken>(
                (_, _, _, _, _) => ++replaceCount switch
                {
                    1 => Task.FromException<ItemResponse<RollingProjection>>(Conflict(HttpStatusCode.TooManyRequests)),
                    2 => Task.FromException<ItemResponse<RollingProjection>>(Conflict(HttpStatusCode.PreconditionFailed)),
                    _ => Task.FromResult(CreateResponse(new RollingProjection()))
                });

        var retryOptions = new RetryOptions(
            maxRetries: 1,
            delay: TimeSpan.Zero,
            retryWhenNotFound: false);
        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer([source], projectionContainer, retryOptions);

        await initializer.ProcessRollingBatch(CreateBatch(
            id,
            [nameof(RollingProjection.name)],
            maxEtagRetries: 1));

        Assert.Equal(2, readCount);
        Assert.Equal(3, replaceCount);
        projectionContainer.Verify(container => container.PatchItemAsync<RollingProjection>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<IReadOnlyList<PatchOperation>>(),
            It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRollingBatch_ThrottleRetriesExhausted_PropagatesTooManyRequests()
    {
        Guid id = Guid.NewGuid();
        Event source = CreateEvent(id, new { id, name = "rebuilt" });
        var projectionContainer = new Mock<Container>();
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(new RollingProjection { id = id }, "etag"));
        projectionContainer
            .Setup(container => container.ReplaceItemAsync(
                It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Conflict(HttpStatusCode.TooManyRequests));

        var retryOptions = new RetryOptions(
            maxRetries: 1,
            delay: TimeSpan.Zero,
            retryWhenNotFound: false);
        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer([source], projectionContainer, retryOptions);

        CosmosException exception = await Assert.ThrowsAsync<CosmosException>(() =>
            initializer.ProcessRollingBatch(CreateBatch(id)));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        projectionContainer.Verify(container => container.ReplaceItemAsync(
            It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        projectionContainer.Verify(container => container.UpsertItemAsync(
            It.IsAny<RollingProjection>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRollingBatch_CancelledBetweenItems_StopsBeforeSecondItem()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        var events = new List<Event>
        {
            CreateEvent(firstId, new { id = firstId, name = "first" }),
            CreateEvent(secondId, new { id = secondId, name = "second" })
        };
        var projectionContainer = new Mock<Container>();
        projectionContainer
            .Setup(container => container.ReadItemAsync<RollingProjection>(
                firstId.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(new RollingProjection { id = firstId }, "etag-first"));
        projectionContainer
            .Setup(container => container.ReplaceItemAsync(
                It.IsAny<RollingProjection>(), firstId.ToString(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(new RollingProjection()));

        var client = new Mock<Microsoft.DurableTask.Client.DurableTaskClient>("rolling-tests");
        client.SetupSequence(value => value.GetInstanceAsync(
                "rolling-tests",
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMetadata(Microsoft.DurableTask.Client.OrchestrationRuntimeStatus.Running))
            .ReturnsAsync(CreateMetadata(Microsoft.DurableTask.Client.OrchestrationRuntimeStatus.Running))
            .ReturnsAsync(CreateMetadata(Microsoft.DurableTask.Client.OrchestrationRuntimeStatus.Terminated));
        DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
            CreateInitializer(events, projectionContainer);

        await initializer.ProcessRollingBatch(CreateBatch([firstId, secondId]), client.Object);

        projectionContainer.Verify(container => container.ReplaceItemAsync(
            It.IsAny<RollingProjection>(), firstId.ToString(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        projectionContainer.Verify(container => container.ReadItemAsync<RollingProjection>(
            secondId.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(value => value.GetInstanceAsync(
            "rolling-tests",
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessRollingBatch_SelectiveReplay_FiltersPayloadAndPreservesBaseThenExternalOrder()
    {
        Guid id = Guid.NewGuid();
        Event firstByTimestamp = CreateEvent(id, new { id, history = "B", description = "ignored" },
            timestamp: DateTime.UnixEpoch.AddSeconds(1), eventId: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        Event firstById = CreateEvent(id, new { id, history = "A" },
            timestamp: DateTime.UnixEpoch.AddSeconds(1), eventId: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Event ignored = CreateEvent(id, new { id, description = "not-selected" },
            timestamp: DateTime.UnixEpoch.AddSeconds(2));
        JObject firstPayloadBefore = (JObject)JObject.FromObject(firstByTimestamp.payload!).DeepClone();

        RollingProjection.ExternalEventsFactory = projections =>
        {
            RollingProjection shadow = Assert.Single(projections);
            Assert.Equal("AB", shadow.history);
            Assert.Equal("not-selected", shadow.description);
            return
            [
                new ExternalDataEvent(id,
                [
                    CreateEvent(id, new { id, history = "D" }, timestamp: DateTime.UnixEpoch),
                    CreateEvent(id, new { id, history = "C" }, timestamp: DateTime.UnixEpoch.AddSeconds(-1))
                ])
            ];
        };

        try
        {
            var projectionContainer = new Mock<Container>();
            projectionContainer
                .Setup(container => container.ReadItemAsync<RollingProjection>(
                    id.ToString(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateResponse(
                    new RollingProjection { id = id, description = "preserved", initialized = false },
                    "etag"));

            RollingProjection? replaced = null;
            projectionContainer
                .Setup(container => container.ReplaceItemAsync(
                    It.IsAny<RollingProjection>(), id.ToString(), It.IsAny<PartitionKey>(),
                    It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
                .Callback<RollingProjection, string, PartitionKey?, ItemRequestOptions, CancellationToken>(
                    (projection, _, _, _, _) => replaced = projection)
                .ReturnsAsync(CreateResponse(new RollingProjection()));

            DurableProjectionInitializer<RollingProjection, RollingAggregate> initializer =
                CreateInitializer([firstByTimestamp, ignored, firstById], projectionContainer);

            await initializer.ProcessRollingBatch(CreateBatch(id, [nameof(RollingProjection.history)]));

            Assert.NotNull(replaced);
            Assert.Equal("ABCD", replaced!.history);
            Assert.Equal("preserved", replaced.description);
            Assert.False(replaced.initialized);
            Assert.True(JToken.DeepEquals(firstPayloadBefore, JObject.FromObject(firstByTimestamp.payload!)));
        }
        finally
        {
            RollingProjection.ExternalEventsFactory = null;
        }
    }

    private static DurableProjectionInitializer<RollingProjection, RollingAggregate> CreateInitializer(
        List<Event> events,
        Mock<Container> projectionContainer,
        RetryOptions? retryOptions = null)
        => CreateInitializer(events, projectionContainer, out _, retryOptions);

    private static DurableProjectionInitializer<RollingProjection, RollingAggregate> CreateInitializer(
        List<Event> events,
        Mock<Container> projectionContainer,
        out Mock<INostify> nostify,
        RetryOptions? retryOptions = null)
    {
        Mock<Container> eventContainer = CosmosTestHelpers.CreateMockContainer(events);
        nostify = new Mock<INostify>();
        nostify.Setup(value => value.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(eventContainer.Object);
        nostify.Setup(value => value.GetProjectionContainerAsync<RollingProjection>(It.IsAny<string>()))
            .ReturnsAsync(projectionContainer.Object);

        return new DurableProjectionInitializer<RollingProjection, RollingAggregate>(
            new HttpClient(),
            nostify.Object,
            "rolling-tests",
            batchSize: 10,
            concurrentBatchCount: 1,
            durableTaskOptions: null,
            cosmosRetryOptions: retryOptions ?? new RetryOptions(0, TimeSpan.Zero, false),
            queryExecutor: InMemoryQueryExecutor.Default);
    }

    private static DurableRollingProjectionBatch CreateBatch(
        Guid id,
        IReadOnlyList<string>? selectedProperties = null,
        int maxEtagRetries = 0)
        => CreateBatch([id], selectedProperties, maxEtagRetries);

    private static DurableRollingProjectionBatch CreateBatch(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<string>? selectedProperties = null,
        int maxEtagRetries = 0)
        => new(
            ids.Select(id => new DurableRollingProjectionWorkItem(id, Guid.Empty.ToString(), true)).ToList(),
            new DurableRollingProjectionOptions(
                selectedProperties,
                maxEtagRetries,
                TimeSpan.Zero,
                backoffCoefficient: 2,
                partitionKeyPath: "/tenantId"));

    private static Microsoft.DurableTask.Client.OrchestrationMetadata CreateMetadata(
        Microsoft.DurableTask.Client.OrchestrationRuntimeStatus status)
    {
        // RuntimeStatus has a private setter, so construct the sealed SDK type and set it reflectively.
        var metadata = new Microsoft.DurableTask.Client.OrchestrationMetadata("rolling-tests", "rolling-tests");
        typeof(Microsoft.DurableTask.Client.OrchestrationMetadata)
            .GetProperty(nameof(metadata.RuntimeStatus))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(metadata, [status]);
        return metadata;
    }

    private static ItemResponse<RollingProjection> CreateResponse(
        RollingProjection projection,
        string etag = "etag")
    {
        var response = new Mock<ItemResponse<RollingProjection>>();
        response.SetupGet(value => value.Resource).Returns(projection);
        response.SetupGet(value => value.ETag).Returns(etag);
        return response.Object;
    }

    private static CosmosException Conflict(HttpStatusCode statusCode)
        => new("simulated Cosmos response", statusCode, 0, string.Empty, 0);

    private static Event CreateEvent(
        Guid aggregateId,
        object payload,
        DateTime? timestamp = null,
        Guid? eventId = null)
        => new(RollingEventType.Instance, aggregateId, payload)
        {
            id = eventId ?? Guid.NewGuid(),
            timestamp = timestamp ?? DateTime.UtcNow
        };

    public sealed class RollingEventType : EventType
    {
        public static RollingEventType Instance { get; } = new();

        public RollingEventType()
            : base("RollingTestEvent", false, false)
        {
        }
    }

    public sealed class RollingAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => "RollingAggregate";
        public static string currentStateContainerName => "RollingAggregateCurrentState";
        public bool isDeleted { get; set; }
    }

    public sealed class RollingProjection : NostifyObject, IProjection, IHasExternalData<RollingProjection>
    {
        public static Func<List<RollingProjection>, List<ExternalDataEvent>>? ExternalEventsFactory { get; set; }

        public static string containerName => "RollingProjection";
        public bool initialized { get; set; }
        public string name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string history { get; set; } = string.Empty;

        protected override void Apply(EventType eventType, IEvent eventToApply)
        {
            JObject payload = JObject.FromObject(eventToApply.payload!);
            id = payload.Value<Guid?>(nameof(id)) ?? id;
            if (payload.TryGetValue(nameof(name), out JToken? nameValue))
            {
                name = nameValue.Value<string>() ?? string.Empty;
            }

            if (payload.TryGetValue(nameof(description), out JToken? descriptionValue))
            {
                description = descriptionValue.Value<string>() ?? string.Empty;
            }

            if (payload.TryGetValue(nameof(history), out JToken? historyValue))
            {
                history += historyValue.Value<string>();
            }
        }

        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
            List<RollingProjection> projectionsToInit,
            INostify nostify,
            HttpClient? httpClient = null,
            DateTime? pointInTime = null)
            => Task.FromResult(ExternalEventsFactory?.Invoke(projectionsToInit) ?? []);
    }
}
