
using System;

namespace nostify;

///<summary>
///Implementing class has a unique identifier
///</summary>

public interface IUniquelyIdentifiable
{
    ///<summary>
    ///Unique identifier for the implementing class
    ///</summary>
    public Guid id { get; set; }
}