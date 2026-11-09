using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace nostify;

internal static class EventTypeQueryExtensions
{
    /// <summary>
    /// Converts EventTypes to strings without throwing on nulls
    /// </summary>
    internal static List<string> ToEventTypeNames(IEnumerable<EventType>? eventTypes)
    {
        if (eventTypes == null)
        {
            return [];
        }

        List<string> names = eventTypes
            .Select(eventType => eventType?.name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => name != null)
            .Select(name => name!)
            .ToList();

        return names;
    }

    /// <summary>
    /// Only get events whose entityType.name or command.name is in eventTypeNames
    /// </summary>
    internal static IQueryable<TEvent> WhereEventTypeNameIn<TEvent>(
        this IQueryable<TEvent> query,
        IEnumerable<string>? eventTypeNames)
        where TEvent : IEvent
    {
        if (eventTypeNames is null || !eventTypeNames.Any())
        {
            return query;
        }

#pragma warning disable CS0618
        return query.Where(e => 
            eventTypeNames.Contains(e.eventType.name)
            || eventTypeNames.Contains(e.command.name) && 
               (!e.eventType.IsDefined() || e.eventType == null || !e.eventType.name.IsDefined() || e.eventType.name == null || e.eventType.name == ""));
#pragma warning restore CS0618
    }

    /// <summary>
    /// Only get events whose entityType.name or command.name is in eventTypes
    /// </summary>
    internal static IQueryable<TEvent> WhereEventTypeNameIn<TEvent>(
        this IQueryable<TEvent> query,
        IEnumerable<EventType>? eventTypes)
        where TEvent : IEvent
    {
        return WhereEventTypeNameIn(query, ToEventTypeNames(eventTypes));
    }
}
