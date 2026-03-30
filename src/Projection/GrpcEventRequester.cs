using System;
using System.Collections.Generic;
using System.Linq;

namespace nostify;

/// <summary>
/// Represents a request configuration for fetching events from an external service via gRPC.
/// Mirrors <see cref="EventRequester{TProjection}"/> (HTTP) and <see cref="AsyncEventRequester{TProjection}"/> (Kafka)
/// but uses a gRPC endpoint address instead of a URL or Kafka service name.
/// </summary>
/// <typeparam name="TProjection">The type of projection that will be initialized</typeparam>
public class GrpcEventRequester<TProjection> where TProjection : IUniquelyIdentifiable
{
    /// <summary>
    /// The gRPC endpoint address (e.g. "https://localhost:5001").
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Functions to get the foreign id for the aggregates required to populate one or more fields in the projection.
    /// </summary>
    public Func<TProjection, Guid?>[] ForeignIdSelectors { get; }

    /// <summary>
    /// Single ID selectors.
    /// </summary>
    public Func<TProjection, Guid?>[] SingleSelectors { get; private set; } = Array.Empty<Func<TProjection, Guid?>>();

    /// <summary>
    /// List ID selectors for cases where we need to expand lists.
    /// </summary>
    public Func<TProjection, List<Guid?>>[] ListSelectors { get; private set; } = Array.Empty<Func<TProjection, List<Guid?>>>();

    /// <summary>
    /// Constructor for GrpcEventRequester with nullable Guid selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address. Must not be null or empty.</param>
    /// <param name="foreignIdSelectors">Functions to get the foreign id for the aggregates required to populate one or more fields in the projection</param>
    public GrpcEventRequester(string address, params Func<TProjection, Guid?>[] foreignIdSelectors)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new NostifyException("gRPC endpoint address is required for GrpcEventRequester");
        }

        Address = address;
        ForeignIdSelectors = foreignIdSelectors ?? Array.Empty<Func<TProjection, Guid?>>();
        SingleSelectors = ForeignIdSelectors;
    }

    /// <summary>
    /// Constructor for GrpcEventRequester with non-nullable Guid selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id (non-nullable) for the aggregates</param>
    public GrpcEventRequester(string address, params Func<TProjection, Guid>[] singleIdSelectors)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new NostifyException("gRPC endpoint address is required for GrpcEventRequester");
        }

        Address = address;
        SingleSelectors = singleIdSelectors?.Select(selector => new Func<TProjection, Guid?>(p => selector(p))).ToArray() ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Constructor for GrpcEventRequester with nullable Guid list selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address. Must not be null or empty.</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids for the aggregates</param>
    public GrpcEventRequester(string address, params Func<TProjection, List<Guid?>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new NostifyException("gRPC endpoint address is required for GrpcEventRequester");
        }

        Address = address;
        SingleSelectors = Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Constructor for GrpcEventRequester with non-nullable Guid list selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address. Must not be null or empty.</param>
    /// <param name="listIdSelectors">Functions that return a list of non-nullable foreign ids for the aggregates</param>
    public GrpcEventRequester(string address, params Func<TProjection, List<Guid>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new NostifyException("gRPC endpoint address is required for GrpcEventRequester");
        }

        Address = address;
        SingleSelectors = Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors?.Select(selector => new Func<TProjection, List<Guid?>>(p => selector(p).Select(g => (Guid?)g).ToList())).ToArray()
            ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = Array.Empty<Func<TProjection, Guid?>>();
    }

    /// <summary>
    /// Constructor for GrpcEventRequester that accepts a mix of single and list foreign ID selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single foreign id for the aggregates</param>
    /// <param name="listIdSelectors">Functions that return a list of foreign ids for the aggregates</param>
    public GrpcEventRequester(string address, Func<TProjection, Guid?>[] singleIdSelectors, Func<TProjection, List<Guid?>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new NostifyException("gRPC endpoint address is required for GrpcEventRequester");
        }

        Address = address;
        SingleSelectors = singleIdSelectors ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Constructor for GrpcEventRequester that accepts a mix of single non-nullable and list non-nullable selectors.
    /// </summary>
    /// <param name="address">The gRPC endpoint address. Must not be null or empty.</param>
    /// <param name="singleIdSelectors">Functions that return a single non-nullable foreign id</param>
    /// <param name="listIdSelectors">Functions that return a list of non-nullable foreign ids</param>
    public GrpcEventRequester(string address, Func<TProjection, Guid>[] singleIdSelectors, Func<TProjection, List<Guid>>[] listIdSelectors)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new NostifyException("gRPC endpoint address is required for GrpcEventRequester");
        }

        Address = address;
        SingleSelectors = singleIdSelectors?.Select(selector => new Func<TProjection, Guid?>(p => selector(p))).ToArray() ?? Array.Empty<Func<TProjection, Guid?>>();
        ListSelectors = listIdSelectors?.Select(selector => new Func<TProjection, List<Guid?>>(p => selector(p).Select(g => (Guid?)g).ToList())).ToArray()
            ?? Array.Empty<Func<TProjection, List<Guid?>>>();
        ForeignIdSelectors = SingleSelectors;
    }

    /// <summary>
    /// Gets all foreign ID selectors by combining single selectors with expanded list selectors.
    /// List selectors are expanded per-projection into individual single-ID selectors.
    /// </summary>
    /// <param name="projections">The projections to expand list selectors against</param>
    /// <returns>Array of all foreign ID selectors</returns>
    public Func<TProjection, Guid?>[] GetAllForeignIdSelectors(List<TProjection> projections)
    {
        if (!ListSelectors.Any())
        {
            return SingleSelectors;
        }

        // Expand list selectors into single selectors per projection per list item
        var expandedSelectors = projections
            .SelectMany(p => ListSelectors.SelectMany(selector => selector(p)),
                (p, guidValue) => new { ProjectionId = p.id, Guid = guidValue })
            .Where(x => x.Guid.HasValue)
            .Select(x => new Func<TProjection, Guid?>(p => p.id == x.ProjectionId ? x.Guid : null))
            .ToArray();

        return SingleSelectors.Concat(expandedSelectors).ToArray();
    }
}
