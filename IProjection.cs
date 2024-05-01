
using System.Net.Http;
using System.Threading.Tasks;

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

    ///<summary>
    ///Returns an Event to Apply() to the Projection when the root Aggregate is initially created.
    ///<para>
    ///Should contain all queries to get any necessary values from Aggregates external to base Projection.
    ///</para>
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    public Task<Event> SeedExternalDataAsync(INostify nostify, HttpClient? httpClient = null);     


    ///<summary>
    ///Recreate container for this Projection.  Will requery all needed data from all services.
    ///<para>
    ///Must contain all queries to get any necessary values from Aggregates external to base Projection.  Should save using bulk update pattern.
    ///</para>
    ///</summary>
    ///<param name="nostify">Reference to the Nostify singleton.</param>
    ///<param name="httpClient">Reference to an HttpClient instance.</param>
    public static abstract Task InitContainerAsync(INostify nostify, HttpClient? httpClient = null);   
}