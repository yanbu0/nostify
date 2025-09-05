using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;

namespace nostify;

public interface IApplyable
{
    public abstract void Apply(IEvent eventToApply);
}

///<summary>
///Internal class inherited by Aggregate and Projection
///</summary>
public abstract class NostifyObject : ITenantFilterable, IUniquelyIdentifiable, IApplyable
{
    ///<summary>
    ///This type should never be directly instantiated
    ///</summary>
    protected internal NostifyObject()
    {
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
    public abstract void Apply(IEvent eventToApply);

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
    public void UpdateProperties<T>(object payload, Dictionary<string, string> propertyPairs, bool strict = false) where T : NostifyObject
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
            var valueToSet = getValueRef.Invoke(null, new object[] { jPayload, propertyToGetValueFrom });
            typeof(T).GetProperty(propToUpdate.Name).SetValue(this, valueToSet);
        }
    }

    ///<summary>
    ///Updates properties of an Aggregate or Projection using conditional property mapping based on ID matching. 
    ///This method is designed for scenarios where a projection has multiple properties of the same type (e.g., multiple user IDs)
    ///and you need to selectively update only the properties associated with a specific aggregate root ID.
    ///For each PropertyCheck, if the eventAggregateRootId matches the projectionIdPropertyValue,
    ///the method will update the target projection property with the value from the corresponding event payload property.
    ///<example>
    ///<br/>
    ///Example: A projection with multiple user properties where you want to update
    ///only the properties associated with the user whose ID matches the event's aggregate root ID:
    ///<code>
    ///List&lt;PropertyCheck&gt; propertyChecks = new List&lt;PropertyCheck&gt;
    ///{
    ///    new PropertyCheck(this.primaryUserId, "name", "primaryUserName"),
    ///    new PropertyCheck(this.primaryUserId, "email", "primaryUserEmail"),
    ///    new PropertyCheck(this.secondaryUserId, "name", "secondaryUserName"),
    ///    new PropertyCheck(this.secondaryUserId, "email", "secondaryUserEmail")
    ///};
    ///this.UpdateProperties&lt;ExampleProjection&gt;(eventToApply.aggregateRootId, eventToApply.payload, propertyChecks);
    ///</code>
    ///If eventToApply.aggregateRootId matches this.primaryUserId, only primaryUserName and primaryUserEmail will be updated.
    ///</example>
    ///</summary>
    /// <param name="eventAggregateRootId">The aggregate root ID from the event to match against PropertyCheck ID values</param>
    ///<param name="payload">The event payload containing the property values to update with</param>
    ///<param name="propertyCheckValues">List of PropertyCheck objects defining the conditional mapping rules</param>
    public void UpdateProperties<T>(Guid eventAggregateRootId, object payload, List<PropertyCheck> propertyCheckValues) where T : NostifyObject
    {
        List<PropertyInfo> thisNostifyObjectProps = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public).ToList();
        JObject jObject = JObject.FromObject(payload);

        foreach (PropertyCheck propertyCheck in propertyCheckValues)
        {
            if (eventAggregateRootId == propertyCheck.projectionIdPropertyValue)
            {
                JToken? jt = jObject[propertyCheck.eventPropertyName];
                if (jt != null)
                {
                    UpdateProperty<T>(propertyCheck.projectionPropertyName, propertyCheck.eventPropertyName, jObject, thisNostifyObjectProps);
                }
            }
        }
    }

}

/// <summary>
/// Contains the values needed to update an aggregate or projection when the event name is not the same as the object name 
/// and there may be more than one property of the same type.
/// </summary>
public class PropertyCheck
{
    /// <summary>
    /// Constructor for PropertyCheck
    /// </summary>
    /// <param name="projectionIdPropertyValue">The Guid ID value to match against the IEvent aggregateRootId</param>
    /// <param name="eventPropertyName">Source property name in IEvent payload</param>
    /// <param name="projectionPropertyName">Target property name in Aggregate/Projection</param>
    public PropertyCheck(Guid projectionIdPropertyValue, string eventPropertyName, string projectionPropertyName)
    {
        this.eventPropertyName = eventPropertyName;
        this.projectionPropertyName = projectionPropertyName;
        this.projectionIdPropertyValue = projectionIdPropertyValue;
    }

    public string eventPropertyName { get; set; } 
    public string projectionPropertyName { get; set; } 
    public Guid projectionIdPropertyValue { get; set; }
}