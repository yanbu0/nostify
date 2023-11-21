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
        ///Add data from external locations to this Projection, use when creating a single Projection or rehydrating it.
        ///</summary>
        ///<para>
        ///Should contain all queries to get any necessary values from Aggregates external to base Projection.
        ///</para>
        ///<param name="untilDate">Optional. Will build the Projection state up to and including this time, if no value provided returns projection of current state</param>
        public abstract Task Seed(DateTime? untilDate = null);     


        ///<summary>
        ///Recreate container for this Projection.  Will requery all needed data from all services.
        ///</summary>
        ///<para>
        ///Must contain all queries to get any necessary values from Aggregates external to base Projection.  Should save using bulk update pattern.
        ///</para>
        ///<param name="nostify">Reference to the Nostify singleton.</param>
        ///<param name="httpClient">Reference to an HttpClient instance.</param>
        public abstract Task InitContainer(Nostify nostify, HttpClient httpClient = null);     


    }

}