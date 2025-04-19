
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;


namespace nostify;

/// <summary>
/// Projection must implement this interface to query external data to update the projection.
/// </summary>
public interface IHasExternalData<P> where P : IProjection
{
    ///<summary>
    ///Queries any necessary external data to create a list of Events to update all of the projections in the param. Gets called in InitAsync.
    ///</summary>
    ///<param name="projectionsToInit">List of projections to query external data for. If empty, will update </param>
    ///<param name="nostify">Nostify instance to use to get current state containers</param>
    ///<param name="httpClient">HttpClient to use to query external data. Can be null if no events external to this service are needed.</param>
    ///<param name="pointInTime">Point in time to query external data.  If null, will query current state. Use when pulling a previous point in time state of Projection.</param>
    public abstract static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<P> projectionsToInit, 
                                                            INostify nostify, 
                                                            HttpClient? httpClient = null,
                                                            DateTime? pointInTime = null);
}