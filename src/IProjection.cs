
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;

namespace nostify;


/// <summary>
/// Projections must implement this interface and inheirit <c>NostifyObject</c>
/// </summary>
public interface IProjection
{
    ///<summary>
    ///Name of container the projection is stored in.  Each Projection must have its own unique container name per microservice.
    ///</summary>
    public static abstract string containerName { get; }

    // ///<summary>
    // ///Queries any neccessary external data to create a list of Events to update all of the projections in the param. Gets called in InitAsync.
    // ///Must return an Event for each Projection in the list which, when Apply is called, will update the Projection to the current state.
    // ///Must set <c>initialized</c> to true after each projection is updated.
    // ///</summary>
    // ///<param name="projectionsToInit">List of projections to query external data for. If empty, will update </param>
    // ///<param name="nostify">Nostify instance to use to get current state containers</param>
    // ///<param name="httpClient">HttpClient to use to query external data. Can be null if no events external to this service are needed.</param>
    // ///<param name="pointInTime">Point in time to query external data.  If null, will query current state. Use when pulling a previous point in time state of Projection.</param>
    // public abstract static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<IProjection> projectionsToInit, 
    //                                                         INostify nostify, 
    //                                                         HttpClient? httpClient = null, 
    //                                                         DateTime? pointInTime = null);

}