
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;

namespace nostify
{
    ///<summary>
    ///Represents events in event store
    ///</summary>
    public class PersistedEvent
    {

        ///<summary>
        ///Constructor for PeristedEvent, use when creating object to save to event store
        ///</summary>
        ///<param name="command">Command to persist</param>
        ///<param name="aggregateRootId">Id of the root aggregate to perform the command on.  Must be a Guid string</param>
        ///<param name="payload">Properties to update or the id of the Aggregate to delete.</param>
        public PersistedEvent(NostifyCommand command, string aggregateRootId, object payload)
        {
            if (!Guid.TryParse(aggregateRootId, out var guidTest)){
                throw new ArgumentException("String is not parsable to a Guid");
            }

            SetUp(command,aggregateRootId,payload);
        }

        ///<summary>
        ///Constructor for PeristedEvent, use when creating object to save to event store
        ///</summary>
        ///<param name="command">Command to persist</param>
        ///<param name="aggregateRootId">Id of the root aggregate to perform the command on.</param>
        ///<param name="payload">Properties to update or the id of the Aggregate to delete.</param>
        public PersistedEvent(NostifyCommand command, Guid aggregateRootId, object payload)
        {
            SetUp(command,aggregateRootId.ToString(),payload);
        }
        
        private void SetUp(NostifyCommand command, string partitionKey, object payload)
        {
            this.partitionKey = partitionKey;
            this.payload = payload;
            this.id = Guid.NewGuid();
            this.command = command;
        }

        ///<summary>
        ///Empty constructor for PeristedEvent, used when querying from db
        ///</summary>
        public PersistedEvent() { }

        ///<summary>
        ///Id of event
        ///</summary>
        public Guid id { get; set; }

        ///<summary>
        ///Command to perform, defined in Aggregate implementation
        ///</summary>
        public NostifyCommand command { get; set; }  //This is an object because otherwise newtonsoft.json pukes creating an NostifyCommand

        ///<summary>
        ///Key of the Aggregate to perform the event on
        ///</summary>
        ///<para>
        ///<strong>The series of events for an Aggregate should have the same key.</strong>
        ///</para>
        public string partitionKey { get; set; }
        
        ///<summary>
        ///Internal use only
        ///</summary>
        public int schemaVersion = 1; //Update to reflect schema changes in Persisted Event

        ///<summary>
        ///Object containing properties of Aggregate to perform the command on
        ///</summary>
        ///<para>
        ///Properties must be the exact same name to have updates applied.
        ///</para>
        ///<para>
        ///Delete command should contain solelly the id value of the Aggregate to delete.
        ///</para>
        public object payload { get; set; }

        
    }
}