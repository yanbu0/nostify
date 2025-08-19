using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;

namespace nostify;

public interface IApplyable
{
    public abstract void Apply(Event eventToApply);
}

///<summary>
///Internal class inherited by Aggregate and Projection
///</summary>
public abstract class NostifyObject : ITenantFilterable, IUniquelyIdentifiable, IApplyable
{
    ///<summary>
    ///This type should never be directly instantiated
    ///</summary>
    protected internal NostifyObject(){
    }

    /// <summary>
    /// Time to live in seconds, default is -1 which means never expire.  Can be set to any positive integer to bulk delete from container using spare RUs.
    /// Container must have TTL enabled for the delete to work.
    /// </summary>
    public int ttl { get; set; } = -1;  
    
    ///<summary>
    ///Id of tenant of logged in user
    ///</summary>
    public Guid tenantId { get; set; }

    
    ///<summary>
    ///Unique value for Aggregate
    ///</summary>
    public Guid id { get; set; }

    
    ///<summary>
    ///Applies event to this Aggregate or Projection
    ///</summary>
    public abstract void Apply(Event eventToApply);

    ///<summary>
    ///Updates properties of Aggregate or Projection
    ///</summary>
    ///<param name="payload">Must be payload from Event, name of property in payload must match property name in T</param>
    public void UpdateProperties<T>(object payload) where T : NostifyObject
    {
        var nosObjProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod() != null)
            .ToList();
        var jPayload = JObject.FromObject(payload);
        var payloadProps = jPayload.Children<JProperty>();

        foreach (JProperty prop in payloadProps)
        {
            UpdateProperty<T>(prop.Name, prop.Name, jPayload, nosObjProps);
        }
    }

    ///<summary>
    ///Updates properties of Aggregate or Projection based off of a dictionary of property pairs. Use when property names in payload do not match property names in T.
    ///<example>
    ///<br/>
    ///Example below will set the ExampleProjection.exampleName property to the value of the payload.name property:
    ///<code>
    ///Dictionary&lt;string, string&gt; propertyPairs = new Dictionary&lt;string, string&gt;{
    ///    {"name", "exampleName"}
    ///};
    ///this.UpdateProperties&lt;ExampleProjection&gt;(eventToApply.payload, propertyPairs, true);
    ///</code>
    ///</example>
    ///</summary>
    ///<param name="payload">Must be payload from Event, name of property in payload must be set to match a property in the propertyPairs dictionary, or must match property name in T if strict is turned off</param>
    ///<param name="propertyPairs">Dictionary of property pairs. Key is property name in payload to get value from, Value is property name in T to set value to. Example: {"name", "inventoryGroupName"}</param>
    ///<param name="strict">If true, only properties in the propertyPairs dictionary will be updated, if false, will also automatically match up properties by their name. The propertyPair dictionary will take precedence.</param>
    public void UpdateProperties<T>(object payload, Dictionary<string,string> propertyPairs, bool strict = false) where T : NostifyObject
    {
        var nosObjProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToList();
        var jPayload = JObject.FromObject(payload);
        var payloadProps = jPayload.Children<JProperty>();

        foreach (JProperty prop in payloadProps)
        {
            bool doUpdate = !strict || propertyPairs.ContainsKey(prop.Name);
            if (doUpdate)
            {
                string propToSet = propertyPairs.ContainsKey(prop.Name) ? propertyPairs[prop.Name] : prop.Name;
                UpdateProperty<T>(propToSet, prop.Name, jPayload, nosObjProps);
            }
        }

    }

    ///<summary>
    ///Updates a property of Aggregate or Projection based off of another property in the payload
    ///</summary>
    ///<param name="propertyToSet">Name of property to set</param>
    ///<param name="propertyToGetValueFrom">Name of property to get value from in the payload</param>
    ///<param name="payload">Payload from Event</param>
    ///<param name="thisNostifyObjectProps">Optional. List of properties of this object. Set this if you are looping through a list to avoid calling GetProperties() multiple times.</param>
    public void UpdateProperty<T>(string propertyToSet, string propertyToGetValueFrom, object payload, List<PropertyInfo> thisNostifyObjectProps = null) where T : NostifyObject
    {
        var jPayload = JObject.FromObject(payload);
        UpdateProperty<T>(propertyToSet, propertyToGetValueFrom, jPayload, thisNostifyObjectProps);
    }

    ///<summary>
    ///Updates a property of Aggregate or Projection based off of another property in the payload
    ///</summary>
    ///<param name="propertyToSet">Name of property to set</param>
    ///<param name="propertyToGetValueFrom">Name of property to get value from in the payload</param>
    ///<param name="jPayload">JObject of payload</param>
    ///<param name="thisNostifyObjectProps">Optional. List of properties of this object. Set this if you are looping through a list to avoid calling GetProperties() multiple times.</param>
    public void UpdateProperty<T>(string propertyToSet, string propertyToGetValueFrom, JObject jPayload, List<PropertyInfo> thisNostifyObjectProps = null) where T : NostifyObject
    {
        var nosObjProps = thisNostifyObjectProps ?? typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToList();
        PropertyInfo propToUpdate = nosObjProps.Where(p => p.Name == propertyToSet).SingleOrDefault();
        if (propToUpdate != null)
        {
            var eg = typeof(NostifyExtensions).GetMethod("GetValue");
            var getValueRef = eg.MakeGenericMethod(propToUpdate.PropertyType);
            var valueToSet = getValueRef.Invoke(null, new object[] {jPayload, propertyToGetValueFrom });
            typeof(T).GetProperty(propToUpdate.Name).SetValue(this, valueToSet);
        }
    }
}
