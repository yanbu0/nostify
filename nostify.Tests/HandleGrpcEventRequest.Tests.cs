using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using nostify;
using nostify.Grpc;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Tests for <see cref="DefaultEventRequestHandlers.HandleGrpcEventRequestAsync"/>.
/// </summary>
public class HandleGrpcEventRequestTests
{
    private readonly Mock<INostify> _mockNostify;
    private readonly Mock<ILogger> _mockLogger;

    public HandleGrpcEventRequestTests()
    {
        _mockNostify = new Mock<INostify>();
        _mockLogger = new Mock<ILogger>();
        _mockNostify.Setup(n => n.Logger).Returns(_mockLogger.Object);
    }

    private void SetupEventStore(List<Event> events)
    {
        var mockContainer = CosmosTestHelpers.CreateMockContainerWithEvents(events);
        _mockNostify.Setup(n => n.GetEventStoreContainerAsync(It.IsAny<bool>()))
            .ReturnsAsync(mockContainer.Object);
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_NullRequest_ReturnsEmptyResponse()
    {
        // Arrange
        SetupEventStore(new List<Event>());

        // Act
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, null!, InMemoryQueryExecutor.Default, _mockLogger.Object);

        // Assert
        Assert.NotNull(response);
        Assert.Empty(response.Events);
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_EmptyAggregateRootIds_ReturnsEmptyResponse()
    {
        // Arrange
        SetupEventStore(new List<Event>());
        var request = new EventRequestMessage();

        // Act
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, request, InMemoryQueryExecutor.Default, _mockLogger.Object);

        // Assert
        Assert.NotNull(response);
        Assert.Empty(response.Events);
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_WithMatchingEvents_ReturnsEvents()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var events = new List<Event>
        {
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId,
                timestamp = DateTime.UtcNow.AddMinutes(-10),
                command = new NostifyCommand("CreateItem"),
                partitionKey = aggId,
                userId = Guid.NewGuid()
            }
        };
        SetupEventStore(events);

        var request = new EventRequestMessage();
        request.AggregateRootIds.Add(aggId.ToString());

        // Act
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, request, InMemoryQueryExecutor.Default, _mockLogger.Object);

        // Assert
        Assert.NotNull(response);
        Assert.Single(response.Events);
        Assert.Equal(aggId.ToString(), response.Events[0].AggregateRootId);
        Assert.Equal("CreateItem", response.Events[0].Command.Name);
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_WithMultipleAggregateRootIds_ReturnsAllMatchingEvents()
    {
        // Arrange
        var aggId1 = Guid.NewGuid();
        var aggId2 = Guid.NewGuid();
        var events = new List<Event>
        {
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId1,
                timestamp = DateTime.UtcNow.AddMinutes(-20),
                command = new NostifyCommand("CreateItem1"),
                partitionKey = aggId1
            },
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId2,
                timestamp = DateTime.UtcNow.AddMinutes(-10),
                command = new NostifyCommand("CreateItem2"),
                partitionKey = aggId2
            }
        };
        SetupEventStore(events);

        var request = new EventRequestMessage();
        request.AggregateRootIds.Add(aggId1.ToString());
        request.AggregateRootIds.Add(aggId2.ToString());

        // Act
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, request, InMemoryQueryExecutor.Default, _mockLogger.Object);

        // Assert
        Assert.Equal(2, response.Events.Count);
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_WithPointInTime_FiltersEvents()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var pointInTime = DateTime.UtcNow.AddMinutes(-15);
        var events = new List<Event>
        {
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId,
                timestamp = pointInTime.AddMinutes(-5), // Before point in time - included
                command = new NostifyCommand("CreateItem"),
                partitionKey = aggId
            },
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId,
                timestamp = pointInTime.AddMinutes(5), // After point in time - excluded
                command = new NostifyCommand("UpdateItem"),
                partitionKey = aggId
            }
        };
        SetupEventStore(events);

        var request = new EventRequestMessage
        {
            HasPointInTime = true,
            PointInTime = Timestamp.FromDateTime(DateTime.SpecifyKind(pointInTime, DateTimeKind.Utc))
        };
        request.AggregateRootIds.Add(aggId.ToString());

        // Act
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, request, InMemoryQueryExecutor.Default, _mockLogger.Object);

        // Assert
        Assert.NotNull(response);
        // The underlying HandleEventRequestAsync should filter by point in time
        Assert.All(response.Events, e =>
        {
            var eventTimestamp = e.Timestamp.ToDateTime();
            Assert.True(eventTimestamp <= pointInTime);
        });
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_WithoutPointInTime_DoesNotFilter()
    {
        // Arrange
        var aggId = Guid.NewGuid();
        var events = new List<Event>
        {
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId,
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                command = new NostifyCommand("CreateItem"),
                partitionKey = aggId
            },
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = aggId,
                timestamp = DateTime.UtcNow.AddMinutes(-5),
                command = new NostifyCommand("UpdateItem"),
                partitionKey = aggId
            }
        };
        SetupEventStore(events);

        var request = new EventRequestMessage { HasPointInTime = false };
        request.AggregateRootIds.Add(aggId.ToString());

        // Act
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, request, InMemoryQueryExecutor.Default, _mockLogger.Object);

        // Assert
        Assert.Equal(2, response.Events.Count);
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_InvalidGuidStrings_SkipsInvalidIds()
    {
        // Arrange
        var validId = Guid.NewGuid();
        var events = new List<Event>
        {
            new Event
            {
                id = Guid.NewGuid(),
                aggregateRootId = validId,
                timestamp = DateTime.UtcNow,
                command = new NostifyCommand("CreateItem"),
                partitionKey = validId
            }
        };
        SetupEventStore(events);

        var request = new EventRequestMessage();
        request.AggregateRootIds.Add(validId.ToString());
        request.AggregateRootIds.Add("not-a-guid");
        request.AggregateRootIds.Add("also-invalid");

        // Act
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, request, InMemoryQueryExecutor.Default, _mockLogger.Object);

        // Assert - should only process valid GUIDs
        Assert.NotNull(response);
        Assert.Single(response.Events);
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_NoMatchingEvents_ReturnsEmptyEvents()
    {
        // Arrange
        SetupEventStore(new List<Event>());

        var request = new EventRequestMessage();
        request.AggregateRootIds.Add(Guid.NewGuid().ToString());

        // Act
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, request, InMemoryQueryExecutor.Default, _mockLogger.Object);

        // Assert
        Assert.NotNull(response);
        Assert.Empty(response.Events);
    }

    [Fact]
    public async Task HandleGrpcEventRequestAsync_NullLogger_UsesNostifyLogger()
    {
        // Arrange
        SetupEventStore(new List<Event>());
        var request = new EventRequestMessage();
        request.AggregateRootIds.Add(Guid.NewGuid().ToString());

        // Act - pass null logger, use InMemoryQueryExecutor
        var response = await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            _mockNostify.Object, request, InMemoryQueryExecutor.Default, null);

        // Assert - should not throw, falls back to nostify.Logger
        Assert.NotNull(response);
    }
}
