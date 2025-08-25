using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace nostify;

/// <summary>
/// Provides extension methods for initializing and managing projections.
/// </summary>
public static class ProjectionExtensions
{
    ///<summary>
    ///Initialize this instance of the Projection.  Will requery all needed external data.
    ///</summary>
    public static async Task<P> InitAsync<P>(this P self, INostify nostify, HttpClient? httpClient = null)
        where P : NostifyObject, IProjection, IHasExternalData<P>, new()
    {
        P? initProj = (await nostify.ProjectionInitializer.InitAsync(new List<P>() { self }, nostify, httpClient)).FirstOrDefault();
        return initProj;
    }
}


/// <summary>
/// Projections must implement this interface and inherit <c>NostifyObject</c>
/// </summary>
public interface IProjection
{

    ///<summary>
    ///If Projection is initialized or not. Set to false by default, should be set to true after <c>InitAsync()</c> is called.
    ///</summary>
    public bool initialized { get; set; }

    ///<summary>
    ///Name of container the projection is stored in.  Each Projection must have its own unique container name per microservice.
    ///</summary>
    public static abstract string containerName { get; }

}