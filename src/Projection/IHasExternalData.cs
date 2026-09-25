
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;


namespace nostify;

/// <summary>
/// Defines how a projection retrieves external events required during initialization.
/// </summary>
/// <typeparam name="P">The projection type being initialized.</typeparam>
public interface IHasExternalData<P> where P : IProjection
{
    /// <summary>
    /// Queries external data and creates the events needed to update the supplied projections.
    /// </summary>
    /// <param name="projectionsToInit">The projections for which external events are requested. An empty list produces no projection-specific requests.</param>
    /// <param name="nostify">The Nostify instance used to access local event and current-state containers.</param>
    /// <param name="httpClient">The optional HTTP client for remote services; may be <see langword="null"/> when no HTTP requests are needed.</param>
    /// <param name="pointInTime">The optional inclusive historical cutoff; <see langword="null"/> requests current data.</param>
    /// <returns>The external events to apply during projection initialization.</returns>
    public abstract static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<P> projectionsToInit,
                                                            INostify nostify,
                                                            HttpClient? httpClient = null,
                                                            DateTime? pointInTime = null);
}