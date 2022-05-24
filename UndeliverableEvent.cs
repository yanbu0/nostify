using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;

namespace nostify
{
    ///<summary>
    ///Captures an event that failed to deliver
    ///</summary>
    public class UndeliverableEvent
    {
        ///<summary>
        ///Construct an UndeliverableEvent
        ///</summary>
        ///<param name="functionName">Name of function that failed, should be able to trace failure back to Azure function</param>
        ///<param name="errorMessage">Error message to capture</param>
        ///<param name="persistedEvent">The event that failed to process</param>
        public UndeliverableEvent(string functionName, string errorMessage, PersistedEvent persistedEvent){
            this.functionName = functionName;
            this.errorMessage = errorMessage;
            this.persistedEvent = persistedEvent;
        }

        ///<summary>
        ///The name of the function that failed
        ///</summary>
        public string functionName { get; set; }

        ///<summary>
        ///The error message
        ///</summary>
        public string errorMessage { get; set; }

        ///<summary>
        ///The event the failure occurred on.  If null then deserializing the document failed.
        ///</summary>
        public PersistedEvent persistedEvent { get; set; }

       

    }

}
