using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using nostify;
using nostify.Grpc;
using Xunit;

namespace nostify.Tests;

public class GrpcEventMappingTests
{
    #region MapToProto Tests

    [Fact]
    public void MapToProto_SingleEvent_MapsAllFields()
    {
        // Arrange
        var evt = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = Guid.NewGuid(),
            timestamp = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            partitionKey = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            schemaVersion = 3,
            command = new NostifyCommand("CreateItem", true, false),
            payload = JObject.FromObject(new { name = "Test", value = 42 })
        };

        // Act
        var msg = GrpcEventMapping.MapToProto(evt);

        // Assert
        Assert.Equal(evt.id.ToString(), msg.Id);
        Assert.Equal(evt.aggregateRootId.ToString(), msg.AggregateRootId);
        Assert.Equal(evt.timestamp, msg.Timestamp.ToDateTime());
        Assert.Equal(evt.partitionKey.ToString(), msg.PartitionKey);
        Assert.Equal(evt.userId.ToString(), msg.UserId);
        Assert.Equal(3, msg.SchemaVersion);
        Assert.Equal("CreateItem", msg.Command.Name);
        Assert.True(msg.Command.IsNew);
        Assert.False(msg.Command.AllowNullPayload);
        Assert.Contains("Test", msg.PayloadJson);
        Assert.Contains("42", msg.PayloadJson);
    }

    [Fact]
    public void MapToProto_NullPayload_SetsEmptyPayloadJson()
    {
        var evt = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = Guid.NewGuid(),
            timestamp = DateTime.UtcNow,
            command = new NostifyCommand("Delete"),
            payload = null!
        };

        var msg = GrpcEventMapping.MapToProto(evt);

        Assert.Equal(string.Empty, msg.PayloadJson);
    }

    [Fact]
    public void MapToProto_NullCommand_CreatesUnknownCommand()
    {
        var evt = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = Guid.NewGuid(),
            timestamp = DateTime.UtcNow,
            command = null!,
            payload = null!
        };

        var msg = GrpcEventMapping.MapToProto(evt);

        Assert.Equal("Unknown", msg.Command.Name);
    }

    [Fact]
    public void MapToProto_NullEvent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GrpcEventMapping.MapToProto((Event)null!));
    }

    [Fact]
    public void MapToProto_BatchEvents_MapsAll()
    {
        var events = new List<Event>
        {
            new Event { id = Guid.NewGuid(), aggregateRootId = Guid.NewGuid(), timestamp = DateTime.UtcNow, command = new NostifyCommand("A") },
            new Event { id = Guid.NewGuid(), aggregateRootId = Guid.NewGuid(), timestamp = DateTime.UtcNow, command = new NostifyCommand("B") },
            new Event { id = Guid.NewGuid(), aggregateRootId = Guid.NewGuid(), timestamp = DateTime.UtcNow, command = new NostifyCommand("C") }
        };

        var messages = GrpcEventMapping.MapToProto(events);

        Assert.Equal(3, messages.Count);
        Assert.Equal("A", messages[0].Command.Name);
        Assert.Equal("B", messages[1].Command.Name);
        Assert.Equal("C", messages[2].Command.Name);
    }

    [Fact]
    public void MapToProto_EmptyList_ReturnsEmptyList()
    {
        var messages = GrpcEventMapping.MapToProto(new List<Event>());
        Assert.Empty(messages);
    }

    [Fact]
    public void MapToProto_NullList_ReturnsEmptyList()
    {
        var messages = GrpcEventMapping.MapToProto((IEnumerable<Event>)null!);
        Assert.Empty(messages);
    }

    #endregion

    #region MapFromProto Tests

    [Fact]
    public void MapFromProto_SingleMessage_MapsAllFields()
    {
        // Arrange
        var id = Guid.NewGuid();
        var aggId = Guid.NewGuid();
        var pk = Guid.NewGuid();
        var uid = Guid.NewGuid();
        var ts = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var msg = new EventMessage
        {
            Id = id.ToString(),
            AggregateRootId = aggId.ToString(),
            Timestamp = Timestamp.FromDateTime(ts),
            PartitionKey = pk.ToString(),
            UserId = uid.ToString(),
            SchemaVersion = 2,
            Command = new CommandMessage { Name = "UpdateItem", IsNew = false, AllowNullPayload = true },
            PayloadJson = JsonConvert.SerializeObject(new { name = "Updated" })
        };

        // Act
        var evt = GrpcEventMapping.MapFromProto(msg);

        // Assert
        Assert.Equal(id, evt.id);
        Assert.Equal(aggId, evt.aggregateRootId);
        Assert.Equal(ts, evt.timestamp);
        Assert.Equal(pk, evt.partitionKey);
        Assert.Equal(uid, evt.userId);
        Assert.Equal("UpdateItem", evt.command.name);
        Assert.False(evt.command.isNew);
        Assert.True(evt.command.allowNullPayload);
        Assert.NotNull(evt.payload);
    }

    [Fact]
    public void MapFromProto_EmptyPayloadJson_SetsNullPayload()
    {
        var msg = new EventMessage
        {
            Id = Guid.NewGuid().ToString(),
            AggregateRootId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Command = new CommandMessage { Name = "Test" },
            PayloadJson = ""
        };

        var evt = GrpcEventMapping.MapFromProto(msg);

        // Empty payload JSON should result in null payload
        Assert.Null(evt.payload);
    }

    [Fact]
    public void MapFromProto_NullCommand_SetsUnknownCommand()
    {
        var msg = new EventMessage
        {
            Id = Guid.NewGuid().ToString(),
            AggregateRootId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Command = null,
            PayloadJson = ""
        };

        var evt = GrpcEventMapping.MapFromProto(msg);

        Assert.Equal("Unknown", evt.command.name);
    }

    [Fact]
    public void MapFromProto_NullMessage_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GrpcEventMapping.MapFromProto((EventMessage)null!));
    }

    [Fact]
    public void MapFromProto_EmptyPartitionKeyAndUserId_SetsGuidEmpty()
    {
        var msg = new EventMessage
        {
            Id = Guid.NewGuid().ToString(),
            AggregateRootId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Command = new CommandMessage { Name = "Test" },
            PartitionKey = "",
            UserId = ""
        };

        var evt = GrpcEventMapping.MapFromProto(msg);

        Assert.Equal(Guid.Empty, evt.partitionKey);
        Assert.Equal(Guid.Empty, evt.userId);
    }

    [Fact]
    public void MapFromProto_BatchMessages_MapsAll()
    {
        var messages = new List<EventMessage>
        {
            new EventMessage { Id = Guid.NewGuid().ToString(), AggregateRootId = Guid.NewGuid().ToString(), Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Command = new CommandMessage { Name = "X" } },
            new EventMessage { Id = Guid.NewGuid().ToString(), AggregateRootId = Guid.NewGuid().ToString(), Timestamp = Timestamp.FromDateTime(DateTime.UtcNow), Command = new CommandMessage { Name = "Y" } }
        };

        var events = GrpcEventMapping.MapFromProto(messages);

        Assert.Equal(2, events.Count);
        Assert.Equal("X", events[0].command.name);
        Assert.Equal("Y", events[1].command.name);
    }

    [Fact]
    public void MapFromProto_EmptyList_ReturnsEmptyList()
    {
        var events = GrpcEventMapping.MapFromProto(new List<EventMessage>());
        Assert.Empty(events);
    }

    [Fact]
    public void MapFromProto_NullList_ReturnsEmptyList()
    {
        var events = GrpcEventMapping.MapFromProto((IEnumerable<EventMessage>)null!);
        Assert.Empty(events);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_EventToProtoAndBack_PreservesAllFields()
    {
        // Arrange
        var original = new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = Guid.NewGuid(),
            timestamp = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc),
            partitionKey = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            schemaVersion = 5,
            command = new NostifyCommand("TestCommand", true, true),
            payload = JObject.FromObject(new { key = "value", count = 99 })
        };

        // Act
        var proto = GrpcEventMapping.MapToProto(original);
        var roundTripped = GrpcEventMapping.MapFromProto(proto);

        // Assert
        Assert.Equal(original.id, roundTripped.id);
        Assert.Equal(original.aggregateRootId, roundTripped.aggregateRootId);
        Assert.Equal(original.timestamp, roundTripped.timestamp);
        Assert.Equal(original.partitionKey, roundTripped.partitionKey);
        Assert.Equal(original.userId, roundTripped.userId);
        Assert.Equal(original.command.name, roundTripped.command.name);
        Assert.Equal(original.command.isNew, roundTripped.command.isNew);
        Assert.Equal(original.command.allowNullPayload, roundTripped.command.allowNullPayload);
        // Payload round-trips through JSON serialization
        Assert.NotNull(roundTripped.payload);
    }

    [Fact]
    public void RoundTrip_BatchEventsToProtoAndBack_PreservesCount()
    {
        var originals = Enumerable.Range(0, 10).Select(i => new Event
        {
            id = Guid.NewGuid(),
            aggregateRootId = Guid.NewGuid(),
            timestamp = DateTime.UtcNow,
            command = new NostifyCommand($"Command{i}")
        }).ToList();

        var protos = GrpcEventMapping.MapToProto(originals);
        var roundTripped = GrpcEventMapping.MapFromProto(protos);

        Assert.Equal(originals.Count, roundTripped.Count);
        for (int i = 0; i < originals.Count; i++)
        {
            Assert.Equal(originals[i].id, roundTripped[i].id);
            Assert.Equal(originals[i].aggregateRootId, roundTripped[i].aggregateRootId);
            Assert.Equal($"Command{i}", roundTripped[i].command.name);
        }
    }

    #endregion
}
