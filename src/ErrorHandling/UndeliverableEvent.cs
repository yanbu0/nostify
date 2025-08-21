using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;

namespace nostify;

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
    ///<param name="undeliverableEvent">The event that failed to process</param>
    public UndeliverableEvent(string functionName, string errorMessage, Event undeliverableEvent)
    {
        this.functionName = functionName;
        this.errorMessage = errorMessage;
        this.undeliverableEvent = undeliverableEvent;
        this.id = Guid.NewGuid();
        this.aggregateRootId = undeliverableEvent.aggregateRootId;
    }

    ///<summary>
    ///Id of undeliverable event
    ///</summary>
    public Guid id { get; set; }

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
    public Event undeliverableEvent { get; set; }

    ///<summary>
    ///Id of the aggregate the event that failed was for
    ///</summary>
    public Guid aggregateRootId { get; set; }



}


