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
       

    }

}