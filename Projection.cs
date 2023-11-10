using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using nostify;
using Microsoft.Azure.Cosmos;

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
        ///Name of container the projection is stored in.  Each Projection must have its own unique container name
        ///</summary>
        ///<para>
        ///Hide using "new" keyword, ex: new public string containerName = "BankAccountDetails"
        ///</para>
        public static string containerName;

        ///<summary>
        ///Add data from external locations to Projection, use when adding new Projection or rebuilding existing one.
        ///</summary>
        ///<para>
        ///Should contain all queries to get any necessary values from Aggregates external to base Projection Aggreegate.
        ///</para>
        ///<param name="untilDate">Optional. Will build the Projection state up to and including this time, if no value provided returns projection of current state</param>
        public abstract Task Seed(DateTime? untilDate = null);
       

    }

}