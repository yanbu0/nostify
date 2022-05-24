using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;

namespace nostify
{
    ///<summary>
    ///Base class for defining Aggregates
    ///</summary>
    public abstract class Aggregate : NostifyObject
    {
        ///<summary>
        ///Base constructor
        ///</summary>
        public Aggregate(){
            id = Guid.NewGuid();
            isDeleted = false;
        }

        ///<summary>
        ///Flag for deleted
        ///</summary>
        public bool isDeleted { get; set; }

        ///<summary>
        ///String value for aggregate name
        ///</summary>
        ///<para>
        ///Hide using "new" keyword if desired in derived class, ex: new public string aggregateType = "BankAccount"
        ///</para>
        public string aggregateType = "AggregateRoot";


    }

}
