using System.Net;
using System.Reflection;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Verifies the narrowly scoped emulator fallback used by rolling projection replay.
/// </summary>
public sealed class DurableProjectionInitializerFallbackTests
{
    private const string KnownMarker = "Unknown JsonNodeType: Unknown";

    [Fact]
    public async Task RebuildProjection_KnownMarker_UsesPartitionedScalarQueryAndOrderedPointReads()
    {
        Guid aggregateId = Guid.NewGuid();
        Event later = CreateEvent(
            aggregateId,
            "B",
            DateTime.UnixEpoch.AddSeconds(2),
            Guid.Parse("00000000-0000-0000-0000-000000000003"));
        Event firstByHigherId = CreateEvent(
            aggregateId,
            "A",
            DateTime.UnixEpoch.AddSeconds(1),
            Guid.Parse("00000000-0000-0000-0000-000000000002"));
        Event firstByLowerId = CreateEvent(
            aggregateId,
            "C",
            DateTime.UnixEpoch.AddSeconds(1),
            Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var eventsById = new Dictionary<string, Event>(StringComparer.Ordinal)
        {
            [later.id.ToString()] = later,
            [firstByHigherId.id.ToString()] = firstByHigherId,
            [firstByLowerId.id.ToString()] = firstByLowerId
        };
        var container = new Mock<Container>();
        QueryDefinition? capturedDefinition = null;
        QueryRequestOptions? capturedOptions = null;
        container.Setup(value => value.GetItemLinqQueryable<Event>(
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>(),
                It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(Array.Empty<Event>().AsQueryable().OrderBy(_ => 1));
        container.Setup(value => value.GetItemQueryIterator<string>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Callback<QueryDefinition, string, QueryRequestOptions>((definition, _, options) =>
            {
                capturedDefinition = definition;
                capturedOptions = options;
            })
            .Returns(CreateIterator([later.id.ToString(), firstByHigherId.id.ToString(), firstByLowerId.id.ToString()]));
        var pointReadIds = new List<string>();
        var pointReadPartitions = new List<PartitionKey>();
        container.Setup(value => value.ReadItemAsync<Event>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .Callback<string, PartitionKey, ItemRequestOptions, CancellationToken>((id, partition, _, _) =>
            {
                pointReadIds.Add(id);
                pointReadPartitions.Add(partition);
            })
            .Returns<string, PartitionKey, ItemRequestOptions, CancellationToken>(
                (id, _, _, _) => Task.FromResult(CreateResponse(eventsById[id])));
        DurableProjectionInitializer<FallbackProjection, FallbackAggregate> initializer =
            CreateInitializer(container, new ThrowingQueryExecutor(KnownMarker));

        FallbackProjection projection = await InvokeRebuildAsync(initializer, aggregateId);

        Assert.Equal("CAB", projection.history);
        Assert.Equal(
            "SELECT VALUE eventItem.id FROM eventItem WHERE eventItem.aggregateRootId = @aggregateRootId",
            capturedDefinition!.QueryText);
        Assert.Equal(aggregateId, GetQueryParameter(capturedDefinition, "@aggregateRootId"));
        Assert.Equal(aggregateId.ToPartitionKey().ToString(), capturedOptions!.PartitionKey!.Value.ToString());
        Assert.Equal(eventsById.Keys, pointReadIds);
        Assert.All(pointReadPartitions, partition =>
            Assert.Equal(aggregateId.ToPartitionKey().ToString(), partition.ToString()));
    }

    [Theory]
    [InlineData("Different failure")]
    [InlineData("Unknown JsonNodeType: Object")]
    [InlineData("unknown JsonNodeType: Unknown")]
    public async Task RebuildProjection_UnrelatedArgumentOutOfRange_Propagates(string message)
    {
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemLinqQueryable<Event>(
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>(),
                It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(Array.Empty<Event>().AsQueryable().OrderBy(_ => 1));
        DurableProjectionInitializer<FallbackProjection, FallbackAggregate> initializer =
            CreateInitializer(container, new ThrowingQueryExecutor(message));

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => InvokeRebuildAsync(initializer, Guid.NewGuid()));

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        container.Verify(value => value.GetItemQueryIterator<string>(
            It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()), Times.Never);
    }

    [Fact]
    public async Task RebuildProjection_ScalarPage429_RetriesToConfiguredLimit()
    {
        Guid aggregateId = Guid.NewGuid();
        var container = CreateFallbackContainer(
            CreateIteratorFailureSequence<string>(Throttle(), Throttle(), EmptyFeedResponse<string>()));
        DurableProjectionInitializer<FallbackProjection, FallbackAggregate> initializer =
            CreateInitializer(container, new ThrowingQueryExecutor(KnownMarker), maxRetries: 1);

        CosmosException exception = await Assert.ThrowsAsync<CosmosException>(
            () => InvokeRebuildAsync(initializer, aggregateId));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Mock.Get(GetIterator(container)).Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RebuildProjection_ScalarPageNon429_PropagatesWithoutRetry()
    {
        var iterator = CreateIteratorFailureSequence<string>(Failure(HttpStatusCode.InternalServerError));
        var container = CreateFallbackContainer(iterator);
        DurableProjectionInitializer<FallbackProjection, FallbackAggregate> initializer =
            CreateInitializer(container, new ThrowingQueryExecutor(KnownMarker), maxRetries: 3);

        CosmosException exception = await Assert.ThrowsAsync<CosmosException>(
            () => InvokeRebuildAsync(initializer, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Mock.Get(iterator).Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RebuildProjection_PointRead429_RetriesToConfiguredLimit()
    {
        Guid aggregateId = Guid.NewGuid();
        string eventId = Guid.NewGuid().ToString();
        var container = CreateFallbackContainer(CreateIterator([eventId]));
        container.Setup(value => value.ReadItemAsync<Event>(
                eventId, It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(Throttle());
        DurableProjectionInitializer<FallbackProjection, FallbackAggregate> initializer =
            CreateInitializer(container, new ThrowingQueryExecutor(KnownMarker), maxRetries: 2);

        CosmosException exception = await Assert.ThrowsAsync<CosmosException>(
            () => InvokeRebuildAsync(initializer, aggregateId));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        container.Verify(value => value.ReadItemAsync<Event>(
            eventId, It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RebuildProjection_PointReadNon429_PropagatesWithoutRetry()
    {
        string eventId = Guid.NewGuid().ToString();
        var container = CreateFallbackContainer(CreateIterator([eventId]));
        container.Setup(value => value.ReadItemAsync<Event>(
                eventId, It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(Failure(HttpStatusCode.InternalServerError));
        DurableProjectionInitializer<FallbackProjection, FallbackAggregate> initializer =
            CreateInitializer(container, new ThrowingQueryExecutor(KnownMarker), maxRetries: 3);

        CosmosException exception = await Assert.ThrowsAsync<CosmosException>(
            () => InvokeRebuildAsync(initializer, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        container.Verify(value => value.ReadItemAsync<Event>(
            eventId, It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DurableProjectionInitializer<FallbackProjection, FallbackAggregate> CreateInitializer(
        Mock<Container> eventStore,
        IQueryExecutor queryExecutor,
        int maxRetries = 0)
    {
        var nostify = new Mock<INostify>();
        nostify.Setup(value => value.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(eventStore.Object);
        return new DurableProjectionInitializer<FallbackProjection, FallbackAggregate>(
            new HttpClient(),
            nostify.Object,
            "fallback-tests",
            10,
            1,
            durableTaskOptions: null,
            new RetryOptions(maxRetries, TimeSpan.Zero, false),
            queryExecutor);
    }

    private static async Task<FallbackProjection> InvokeRebuildAsync(
        DurableProjectionInitializer<FallbackProjection, FallbackAggregate> initializer,
        Guid aggregateId)
    {
        MethodInfo method = typeof(DurableProjectionInitializer<FallbackProjection, FallbackAggregate>)
            .GetMethod("RebuildProjectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<FallbackProjection>)method.Invoke(initializer, [aggregateId, Array.Empty<string>()])!;
        return await task;
    }

    private static Mock<Container> CreateFallbackContainer(FeedIterator<string> iterator)
    {
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemLinqQueryable<Event>(
                It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>(),
                It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(Array.Empty<Event>().AsQueryable().OrderBy(_ => 1));
        container.Setup(value => value.GetItemQueryIterator<string>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(iterator);
        return container;
    }

    private static FeedIterator<string> GetIterator(Mock<Container> container)
        => container.Object.GetItemQueryIterator<string>(new QueryDefinition("SELECT 1"));

    private static FeedIterator<T> CreateIterator<T>(IReadOnlyList<T> items)
    {
        FeedResponse<T> response = FeedResponse(items);
        var iterator = new Mock<FeedIterator<T>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(response);
        return iterator.Object;
    }

    private static FeedIterator<T> CreateIteratorFailureSequence<T>(params object[] outcomes)
    {
        var iterator = new Mock<FeedIterator<T>>();
        iterator.SetupGet(value => value.HasMoreResults).Returns(true);
        var sequence = iterator.SetupSequence(value => value.ReadNextAsync(It.IsAny<CancellationToken>()));
        foreach (object outcome in outcomes)
        {
            if (outcome is Exception exception)
            {
                sequence.ThrowsAsync(exception);
            }
            else
            {
                sequence.ReturnsAsync((FeedResponse<T>)outcome);
            }
        }

        return iterator.Object;
    }

    private static FeedResponse<T> FeedResponse<T>(IReadOnlyList<T> items)
    {
        var response = new Mock<FeedResponse<T>>();
        response.Setup(value => value.GetEnumerator()).Returns(() => items.GetEnumerator());
        return response.Object;
    }

    private static FeedResponse<T> EmptyFeedResponse<T>() => FeedResponse(Array.Empty<T>());

    private static ItemResponse<Event> CreateResponse(Event @event)
    {
        var response = new Mock<ItemResponse<Event>>();
        response.SetupGet(value => value.Resource).Returns(@event);
        return response.Object;
    }

    private static object? GetQueryParameter(QueryDefinition definition, string name)
    {
        MethodInfo method = typeof(QueryDefinition).GetMethod("GetQueryParameters")!;
        var parameters = (System.Collections.IEnumerable)method.Invoke(definition, null)!;
        foreach (object parameter in parameters)
        {
            Type type = parameter.GetType();
            string? parameterName = type.GetProperty("Name")?.GetValue(parameter)?.ToString()
                ?? type.GetField("Item1")?.GetValue(parameter)?.ToString();
            if (string.Equals(parameterName, name, StringComparison.Ordinal))
            {
                return type.GetProperty("Value")?.GetValue(parameter)
                    ?? type.GetField("Item2")?.GetValue(parameter);
            }
        }

        return null;
    }

    private static Event CreateEvent(Guid aggregateId, string history, DateTime timestamp, Guid eventId)
        => new(FallbackEventType.Instance, aggregateId, new { id = aggregateId, history })
        {
            id = eventId,
            timestamp = timestamp
        };

    private static CosmosException Throttle()
        => new("throttled", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);

    private static CosmosException Failure(HttpStatusCode statusCode)
        => new("failed", statusCode, 0, string.Empty, 0);

    private sealed class ThrowingQueryExecutor(string message) : IQueryExecutor
    {
        public Task<List<T>> ReadAllAsync<T>(IQueryable<T> query)
            => throw new ArgumentOutOfRangeException(nameof(query), message);

        public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query)
            => throw new NotSupportedException();

        public Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new()
            => throw new NotSupportedException();

        public Task<int> CountAsync<T>(IQueryable<T> query)
            => throw new NotSupportedException();
    }

    public sealed class FallbackEventType : EventType
    {
        public static FallbackEventType Instance { get; } = new();

        public FallbackEventType()
            : base("FallbackEvent", false, false)
        {
        }
    }

    public sealed class FallbackAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => "FallbackAggregate";
        public static string currentStateContainerName => "FallbackAggregateCurrentState";
        public bool isDeleted { get; set; }
    }

    public sealed class FallbackProjection : NostifyObject, IProjection, IHasExternalData<FallbackProjection>
    {
        public static string containerName => "FallbackProjection";
        public bool initialized { get; set; }
        public string history { get; set; } = string.Empty;

        protected override void Apply(EventType eventType, IEvent eventToApply)
        {
            var payload = Newtonsoft.Json.Linq.JObject.FromObject(eventToApply.payload!);
            id = payload.Value<Guid>(nameof(id));
            history += payload.Value<string>(nameof(history));
        }

        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
            List<FallbackProjection> projections,
            INostify nostify,
            HttpClient? httpClient = null,
            DateTime? pointInTime = null)
            => Task.FromResult(new List<ExternalDataEvent>());
    }
}
