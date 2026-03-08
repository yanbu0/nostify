using System;
using System.Collections.Generic;
using System.Linq;

namespace nostify;

/// <summary>
/// Represents a request configuration for fetching events from an external service via Kafka async messaging.
/// Mirrors <see cref="EventRequester{TProjection}"/> but uses a service name (for Kafka topic derivation)
/// instead of a URL (for HTTP calls).
/// </summary>
/// <typeparam name="TProjection">The type of projection that will be initialized</typeparam>
public class AsyncEventRequester<TProjection> where TProjection : IUniquelyIdentifiable
{
    /// <summary>
    /// The name of the external service. Used to derive the Kafka topic as {ServiceName}_EventRequest.
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    /// The Kafka topic name derived from the service name.
    /// </summary>
    public string TopicName => $"{ServiceName}_EventRequest";

    /// <summary>
    /// Functions to get the foreign id for the aggregates required to populate one or more fields in the projection.
    /// </summary>
    public Func<TProjection, Guid?>[] ForeignIdSelectors { get; }

    /// <summary>
    /// Single ID selectors
    /// </summary>
    public Func<TProjection, Guid?>[] SingleSelectors { get; private set; } = Array.Empty<Func<TProjection, Guid?>>();

    /// <summary>
    /// List ID selectors for cases where we need to expand lists
    /// </summary>
    public Func<TProjection, List<Guid?>>[] ListSelectors { get; private set; } = Array.Empty<Func<TProjection, List<Guid?>>>();

    /// <summary>
    /// Constructor for AsyncEventRequester with nullable Guid selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service. Must not be null or empty.</param>
    /// <param name="foreignIdSelectors">Functions to get the foreign id for the aggregates required to populate one or more fields in the projection</param>
    public AsyncEventRequester(string serviceName, params Func<TProjection, Guid?>[] foreignIdSelectors)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new NostifyException("Service name is required for AsyncEventRequester");
        }

        ServiceName = serviceName;
        ForeignIdSelectors = foreignIdSelectors ?? Array.Empty<Func<TProjection, Guid?>>();
        SingleSelectors = ForeignIdSelectors;
    }

    /// <summary>
    /// Constructor for AsyncEventRequester with non-nullable Guid selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id (non-nullable) for the aggregates</param>
    public AsyncEventRequester(string serviceName, params Func<TProjection, Guid>[] singleIdSelectors)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new NostifyException("Service name is required for AsyncEventRequester");
        }

        ServiceName = serviceName;
        SingleSelectors = singleIdSelectors?.Select(selector => new Func<TProjection, Guid?>(p => selector(p))).ToArray() ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Constructor for AsyncEventRequester with nullable Guid list selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service. Must not be null or empty.</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids for the aggregates</param>
    public AsyncEventRequester(string serviceName, params Func<TProjection, List<Guid?>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new NostifyException("Service name is required for AsyncEventRequester");
        }

        ServiceName = serviceName;
        SingleSelectors = Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Constructor for AsyncEventRequester with non-nullable Guid list selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service. Must not be null or empty.</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids (non-nullable) for the aggregates</param>
    public AsyncEventRequester(string serviceName, params Func<TProjection, List<Guid>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new NostifyException("Service name is required for AsyncEventRequester");
        }

        ServiceName = serviceName;
        SingleSelectors = Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors?.Select(selector => new Func<TProjection, List<Guid?>>(p => selector(p)?.Cast<Guid?>().ToList() ?? new List<Guid?>())).ToArray() ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Constructor for AsyncEventRequester that accepts a mix of single nullable and list nullable selectors.
    /// </summary>
    /// <param name="serviceName">The name of the external service. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id for the aggregates</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids for the aggregates</param>
    public AsyncEventRequester(string serviceName, Func<TProjection, Guid?>[] singleIdSelectors, Func<TProjection, List<Guid?>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new NostifyException("Service name is required for AsyncEventRequester");
        }

        ServiceName = serviceName;
        SingleSelectors = singleIdSelectors ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Constructor for AsyncEventRequester that accepts both single and list Guid selectors (non-nullable).
    /// </summary>
    /// <param name="serviceName">The name of the external service. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id (non-nullable) for the aggregates</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids (non-nullable) for the aggregates</param>
    public AsyncEventRequester(string serviceName, Func<TProjection, Guid>[] singleIdSelectors, Func<TProjection, List<Guid>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new NostifyException("Service name is required for AsyncEventRequester");
        }

        ServiceName = serviceName;
        SingleSelectors = singleIdSelectors?.Select(selector => new Func<TProjection, Guid?>(p => selector(p))).ToArray() ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors?.Select(selector => new Func<TProjection, List<Guid?>>(p => selector(p)?.Cast<Guid?>().ToList() ?? new List<Guid?>())).ToArray() ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Gets all foreign ID selectors as Func&lt;TProjection, Guid?&gt;[] by expanding list selectors.
    /// </summary>
    /// <param name="projectionsToInit">List of projections to use for expanding list selectors</param>
    /// <returns>Array of all foreign ID selectors</returns>
    public Func<TProjection, Guid?>[] GetAllForeignIdSelectors(List<TProjection> projectionsToInit)
    {
        var allSelectors = new List<Func<TProjection, Guid?>>();

        // Add single selectors directly
        allSelectors.AddRange(SingleSelectors);

        // Transform list selectors using the same logic as EventRequester
        if (ListSelectors.Any())
        {
            var expandedSelectors = projectionsToInit
                .SelectMany(p => ListSelectors.SelectMany(selector => selector(p)))
                .Select(guid => new Func<TProjection, Guid?>(_ => guid))
                .ToArray();

            allSelectors.AddRange(expandedSelectors);
        }

        return allSelectors.ToArray();
    }
}
