using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json;
using nostify.Grpc;

namespace nostify;

/// <summary>
/// Provides mapping helpers between nostify <see cref="Event"/> objects and gRPC protobuf messages.
/// </summary>
public static class GrpcEventMapping
{
    /// <summary>
    /// Converts a gRPC <see cref="EventMessage"/> to a nostify <see cref="Event"/>.
    /// </summary>
    /// <param name="msg">The protobuf event message to convert</param>
    /// <returns>A nostify Event instance</returns>
    public static Event MapFromProto(EventMessage msg)
    {
        if (msg == null) throw new ArgumentNullException(nameof(msg));

        var evt = new Event
        {
            id = Guid.Parse(msg.Id),
            aggregateRootId = Guid.Parse(msg.AggregateRootId),
            timestamp = msg.Timestamp?.ToDateTime() ?? DateTime.UtcNow,
            partitionKey = string.IsNullOrEmpty(msg.PartitionKey) ? Guid.Empty : Guid.Parse(msg.PartitionKey),
            userId = string.IsNullOrEmpty(msg.UserId) ? Guid.Empty : Guid.Parse(msg.UserId),
            command = new NostifyCommand(
                msg.Command?.Name ?? "Unknown",
                msg.Command?.IsNew ?? false,
                msg.Command?.AllowNullPayload ?? false
            ),
            payload = string.IsNullOrEmpty(msg.PayloadJson)
                ? null!
                : JsonConvert.DeserializeObject<object>(msg.PayloadJson)!
        };

        return evt;
    }

    /// <summary>
    /// Converts a nostify <see cref="Event"/> to a gRPC <see cref="EventMessage"/>.
    /// </summary>
    /// <param name="evt">The nostify event to convert</param>
    /// <returns>A protobuf EventMessage instance</returns>
    public static EventMessage MapToProto(Event evt)
    {
        if (evt == null) throw new ArgumentNullException(nameof(evt));

        var msg = new EventMessage
        {
            Id = evt.id.ToString(),
            AggregateRootId = evt.aggregateRootId.ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(evt.timestamp, DateTimeKind.Utc)),
            PartitionKey = evt.partitionKey.ToString(),
            UserId = evt.userId.ToString(),
            SchemaVersion = evt.schemaVersion,
            PayloadJson = evt.payload != null
                ? JsonConvert.SerializeObject(evt.payload)
                : string.Empty,
            Command = evt.command != null
                ? new CommandMessage
                {
                    Name = evt.command.name ?? "Unknown",
                    IsNew = evt.command.isNew,
                    AllowNullPayload = evt.command.allowNullPayload
                }
                : new CommandMessage { Name = "Unknown" }
        };

        return msg;
    }

    /// <summary>
    /// Converts a list of nostify <see cref="Event"/> objects to a list of gRPC <see cref="EventMessage"/> objects.
    /// </summary>
    /// <param name="events">The events to convert</param>
    /// <returns>List of protobuf EventMessage instances</returns>
    public static List<EventMessage> MapToProto(IEnumerable<Event> events)
    {
        return events?.Select(MapToProto).ToList() ?? new List<EventMessage>();
    }

    /// <summary>
    /// Converts a list of gRPC <see cref="EventMessage"/> objects to a list of nostify <see cref="Event"/> objects.
    /// </summary>
    /// <param name="messages">The protobuf messages to convert</param>
    /// <returns>List of nostify Event instances</returns>
    public static List<Event> MapFromProto(IEnumerable<EventMessage> messages)
    {
        return messages?.Select(MapFromProto).ToList() ?? new List<Event>();
    }
}
