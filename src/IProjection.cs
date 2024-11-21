
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
/// Interface for the name of the container the projection is stored in.
/// </summary>
public interface IContainerName
{
    ///<summary>
    ///Name of container the projection is stored in.  Each Projection must have its own unique container name per microservice.
    ///</summary>
    public static abstract string containerName { get; }
}

/// <summary>
/// Projection must implement this interface to query external data to update the projection.
/// </summary>
public interface IHasExternalData<P> where P : IProjection<P>, IContainerName
{
    

    ///<summary>
    ///Queries any neccessary external data to create a list of Events to update all of the projections in the param. Gets called in InitAsync.
    ///Must return an Event for each Projection in the list which, when Apply is called, will update the Projection to the current state.
    ///Must set <c>initialized</c> to true after each projection is updated.
    ///</summary>
    ///<param name="projectionsToInit">List of projections to query external data for. If empty, will update </param>
    ///<param name="nostify">Nostify instance to use to get current state containers</param>
    ///<param name="httpClient">HttpClient to use to query external data. Can be null if no events external to this service are needed.</param>
    ///<param name="loopSize">Number of items to query at a time.  Defaults to 1000.</param>
    ///<param name="pointInTime">Point in time to query external data.  If null, will query current state. Use when pulling a previous point in time state of Projection.</param>
    public abstract static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<P> projectionsToInit, 
                                                            INostify nostify, 
                                                            HttpClient? httpClient = null,
                                                            int loopSize = 1000,
                                                            DateTime? pointInTime = null);
}


/// <summary>
/// Projections must implement this interface and inheirit <c>NostifyObject</c>
/// </summary>
public interface IProjection<P>
{

    /// <summary>
    /// Time to live in seconds, default is -1 which means never expire.  Can be set to any positive integer to bulk delete from container using spare RUs.
    /// Container must have TTL enabled for the delete to work.
    /// </summary>
    public int ttl { get; set; }    

    ///<summary>
    ///If Projection is initialized or not. Set to false by default, should be set to true after <c>InitAsync()</c> is called.
    ///</summary>
    public bool initialized { get; set; }

    ///<summary>
    ///Initialize this instance of the Projection.  Will requery all needed external data.
    ///</summary>
    public abstract Task<P> InitAsync(INostify nostify, HttpClient? httpClient = null);

    ///<summary>
    ///Initialize the Projection with the specified id.  Will requery all needed data from all services.
    ///<returns>Testing</returns>
    ///</summary>
    public abstract static Task<List<P>> InitAsync(Guid id, INostify nostify, HttpClient? httpClient = null);

    ///<summary>
    ///Initialize the Projections with the specified ids.  Will requery all needed data from all services.
    ///</summary>
    ///<para>
    ///Must contain all queries to get any necessary values from Aggregates external to base Projection.  Should save using bulk update pattern.
    ///</para>
    public abstract static Task<List<P>> InitAsync(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null);

    /// <summary>
    /// Initializes a list of projections asynchronously. Will requery all needed data from all external services, set <c>initialized = true</c> and update projection container. 
    /// </summary>
    /// <param name="projectionsToInit">List of projections to initialize.</param>
    /// <param name="nostify">Reference to the Nostify singleton.</param>
    /// <param name="httpClient">Optional HttpClient instance for making HTTP requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of initialized projections of type T.</returns>
    public abstract static Task<List<P>> InitAsync(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null);

    ///<summary>
    ///Recreate container for this Projection.  
    ///Will delete container and recreate it then will query the specified base Aggregate where isDeleted == false and populate all matching properties in the projection. 
    ///<para>
    ///Will loop through all items in the container in batches of <c>loopSize</c> and call InitAsync on each batch to query all needed Events from all external services and update projection container.
    ///</para>
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    ///<param name="partitionKeyPath">Path to the partition key.  Defaults to "/tenantId".</param>
    ///<param name="loopSize">Number of items to init at a time.  Defaults to 1000.</param>
    public abstract static Task InitContainerAsync(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000);

    ///<summary>
    ///Init all non-initialized projections in the container.  Will requery all needed data from all external services by calling InitAsync  
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    public abstract static Task InitAllUninitialized(INostify nostify, HttpClient? httpClient = null);

}