using System;
using System.Collections.Generic;
using System.Linq;

namespace nostify;

/// <summary>
/// Represents a request configuration for fetching events from an external service to initialize projections
/// </summary>
/// <typeparam name="TProjection">The type of projection that will be initialized</typeparam>
public class EventRequester<TProjection> where TProjection : IUniquelyIdentifiable
{
    /// <summary>
    /// The URL of the service EventRequest endpoint
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Functions to get the foreign id for the aggregates required to populate one or more fields in the projection
    /// </summary>
    public Func<TProjection, Guid?>[] ForeignIdSelectors { get; }

    /// <summary>
    /// Constructor for EventRequester
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="foreignIdSelectors">Functions to get the foreign id for the aggregates required to populate one or more fields in the projection</param>
    public EventRequester(string url, params Func<TProjection, Guid?>[] foreignIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        ForeignIdSelectors = foreignIdSelectors ?? Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Constructor for EventRequester that accepts a mix of single and list foreign ID selectors
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id for the aggregates</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids for the aggregates</param>
    public EventRequester(string url, Func<TProjection, Guid?>[] singleIdSelectors, Func<TProjection, List<Guid?>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        SingleSelectors = singleIdSelectors ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        
        // We'll compute ForeignIdSelectors when needed, but for backward compatibility,
        // we'll store the single selectors directly
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Constructor for EventRequester that accepts list foreign ID selectors
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids for the aggregates</param>
    public EventRequester(string url, params Func<TProjection, List<Guid?>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        SingleSelectors = Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        
        // For backward compatibility, ForeignIdSelectors will be empty initially
        // They will be expanded when GetAllForeignIdSelectors is called
        ForeignIdSelectors = Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Constructor for EventRequester that accepts single Guid selectors (non-nullable)
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id (non-nullable) for the aggregates</param>
    public EventRequester(string url, params Func<TProjection, Guid>[] singleIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        SingleSelectors = singleIdSelectors?.Select(selector => new Func<TProjection, Guid?>(p => selector(p))).ToArray() ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Constructor for EventRequester that accepts list Guid selectors (non-nullable)
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids (non-nullable) for the aggregates</param>
    public EventRequester(string url, params Func<TProjection, List<Guid>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        SingleSelectors = Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors?.Select(selector => new Func<TProjection, List<Guid?>>(p => selector(p)?.Cast<Guid?>().ToList() ?? new List<Guid?>())).ToArray() ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Constructor for EventRequester that accepts both single and list Guid selectors (non-nullable)
    /// </summary>
    /// <param name="url">The URL of the service EventRequest endpoint. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id (non-nullable) for the aggregates</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids (non-nullable) for the aggregates</param>
    public EventRequester(string url, Func<TProjection, Guid>[] singleIdSelectors, Func<TProjection, List<Guid>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new NostifyException("URL of EventRequest endpoint is required");
        }

        Url = url;
        SingleSelectors = singleIdSelectors?.Select(selector => new Func<TProjection, Guid?>(p => selector(p))).ToArray() ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors?.Select(selector => new Func<TProjection, List<Guid?>>(p => selector(p)?.Cast<Guid?>().ToList() ?? new List<Guid?>())).ToArray() ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Single ID selectors
    /// </summary>
    public Func<TProjection, Guid?>[] SingleSelectors { get; private set; } = Array.Empty<Func<TProjection, Guid?>>();

    /// <summary>
    /// List ID selectors for cases where we need to expand lists
    /// </summary>
    public Func<TProjection, List<Guid?>>[] ListSelectors { get; private set; } = Array.Empty<Func<TProjection, List<Guid?>>>();

    /// <summary>
    /// Gets all foreign ID selectors as Func&lt;TProjection, Guid?&gt;[] by expanding list selectors
    /// </summary>
    /// <param name="projectionsToInit">List of projections to use for expanding list selectors</param>
    /// <returns>Array of all foreign ID selectors</returns>
    public Func<TProjection, Guid?>[] GetAllForeignIdSelectors(List<TProjection> projectionsToInit)
    {
        var allSelectors = new List<Func<TProjection, Guid?>>();
        
        // Add single selectors directly
        allSelectors.AddRange(SingleSelectors);
        
        // Transform list selectors using the same logic as TransformForeignIdSelectors
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
