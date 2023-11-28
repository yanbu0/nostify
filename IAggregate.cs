using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;

namespace nostify;

/// <summary>
/// Aggregates must implement this interface and inheirit <c>NostifyObject</c>
/// </summary>
public interface IAggregate
{
    ///<summary>
    ///Flag for deleted
    ///</summary>
    public bool isDeleted { get; set; }

    ///<summary>
    ///String value for aggregate name
    ///</summary>
    public static string aggregateType;


}


