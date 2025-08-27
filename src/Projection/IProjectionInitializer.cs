using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Azure.Cosmos;

namespace nostify;

/// <summary>
/// Interface for the ProjectionInitializer class, providing methods to initialize projections and manage projection containers.
/// </summary>
public interface IProjectionInitializer
{
    /// <summary>
    /// Initializes the Projection with the specified id. Will requery all needed data from all services.
    /// </summary>
    Task<List<P>> InitAsync<P, A>(Guid id, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
        where A : IAggregate
        where P : NostifyObject, IProjection, IHasExternalData<P>, new();

    /// <summary>
    /// Initializes the Projections with the specified ids. Will requery all needed data from all services.
    /// </summary>
    Task<List<P>> InitAsync<P, A>(List<Guid> idsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
        where A : IAggregate
        where P : NostifyObject, IProjection, IHasExternalData<P>, new();

    /// <summary>
    /// Initializes a list of projections asynchronously. Will requery all needed data from all external services, set <c>initialized = true</c>, and update the projection container.
    /// </summary>
    Task<List<P>> InitAsync<P>(List<P> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
        where P : NostifyObject, IProjection, IHasExternalData<P>, new();

    /// <summary>
    /// Recreates the container for this Projection. Deletes the container, recreates it, and queries the specified base Aggregate where isDeleted == false.
    /// </summary>
    Task InitContainerAsync<P, A>(INostify nostify, HttpClient? httpClient = null, string partitionKeyPath = "/tenantId", int loopSize = 1000, DateTime? pointInTime = null)
        where A : IAggregate
        where P : NostifyObject, IProjection, IHasExternalData<P>, new();

    /// <summary>
    /// Initializes all non-initialized projections in the container. Will requery all needed data from all external services by calling InitAsync.
    /// </summary>
    Task InitAllUninitialized<P>(INostify nostify, HttpClient? httpClient = null, int maxloopSize = 10, DateTime? pointInTime = null)
        where P : NostifyObject, IProjection, IHasExternalData<P>, new();
}
