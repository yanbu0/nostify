using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using nostify;
using Microsoft.Azure.Cosmos;
using System.Net.Http;

namespace nostify
{
    ///<summary>
    ///Base class for defining Projections
    ///</summary>
    public abstract class Projection : NostifyObject
    {
        ///<summary>
        ///Base constructor
        ///</summary>
        public Projection(){
        }

        ///<summary>
        ///Name of container the projection is stored in.  Each Projection must have its own unique container name per microservice.
        ///</summary>
        ///<para>
        ///Hide using "new" keyword, ex: new public string containerName = "BankAccountDetails"
        ///</para>
        public static string containerName;

        ///<summary>
        ///Returns an Event to Apply() to the Projection when the root Aggregate is initially created.
        ///</summary>
        ///<para>
        ///Should contain all queries to get any necessary values from Aggregates external to base Projection.
        ///</para>
        ///<param name="nostify">Reference to the Nostify singleton.</param>
        ///<param name="httpClient">Reference to an HttpClient instance.</param>
        public abstract Task<Event> Seed(Nostify nostify, HttpClient? httpClient = null);     


        ///<summary>
        ///Recreate container for this Projection.  Will requery all needed data from all services.
        ///</summary>
        ///<para>
        ///Must contain all queries to get any necessary values from Aggregates external to base Projection.  Should save using bulk update pattern.
        ///</para>
        ///<param name="nostify">Reference to the Nostify singleton.</param>
        ///<param name="httpClient">Reference to an HttpClient instance.</param>
        public abstract Task InitContainer(Nostify nostify, HttpClient? httpClient = null);     


    }

}