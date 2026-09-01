using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.Collections.Concurrent;
using nostify.Attributes;

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
    /// <summary>
    /// Cached MethodInfo for NostifyExtensions.GetValue to avoid repeated reflection
    /// lookups on the NostifyExtensions type for each property update.
    /// </summary>
    private static readonly MethodInfo _getValueMethodInfo = typeof(NostifyExtensions)
        .GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static);

    /// <summary>
    /// Cache of writable properties for each NostifyObject-derived type T,
    /// keyed by property name for fast lookup.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> _propertyMapCache = new();

    /// <summary>
    /// Get or build a dictionary of writable properties for type T keyed by property name.
    /// This is the core reflection cache used by UpdateProperties and UpdateProperty.
    /// </summary>
    private static Dictionary<string, PropertyInfo> GetPropertyMap<T>() where T : NostifyObject
    {
        return _propertyMapCache.GetOrAdd(typeof(T), t =>
        {
            return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetSetMethod() != null)
                .ToDictionary(p => p.Name, p => p);
        });
    }

    /// <summary>
    /// Internal helper that performs a single property update using cached reflection
    /// metadata to minimize overhead. All public UpdateProperties overloads delegate
    /// into this method.
    /// </summary>
    private void UpdatePropertyInternal<T>(string propertyToSet, string propertyToGetValueFrom, JObject jPayload, Dictionary<string, PropertyInfo> propertyMap) where T : NostifyObject
    {
        if (!propertyMap.TryGetValue(propertyToSet, out var propToUpdate))
        {
            // Property does not exist on T; behavior matches original implementation (no-op).
            return;
        }

        // Reuse cached MethodInfo for GetValue and only construct the closed generic method
        // for the specific property type we are updating.
        var getValueRef = _getValueMethodInfo.MakeGenericMethod(propToUpdate.PropertyType);
        var valueToSet = getValueRef.Invoke(null, new object[] { jPayload, propertyToGetValueFrom });

        // Use the PropertyInfo we already have instead of querying typeof(T) again.
        propToUpdate.SetValue(this, valueToSet);
    }

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
    ///Applies event to this Aggregate or Projection.
    ///
    /// Dispatch order:
    /// 1. If any methods on the concrete type are decorated with <see cref="ApplyEventsAttribute"/>
    ///    for the event's <see cref="EventType"/>, they are invoked first.
    /// 2. If no attribute-based handler exists, falls back to the existing dynamic
    ///    overload-based dispatch: <c>Apply((dynamic)eventToApply.eventType, eventToApply)</c>.
    ///
    /// This allows aggregates and projections to opt into attribute-based handling
    /// without breaking existing overload patterns.
    ///</summary>
    public void Apply(IEvent eventToApply)
    {
        if (!TryApplyWithAttributes(eventToApply))
        {
            // Fallback to existing dynamic overload dispatch
            Apply((dynamic)eventToApply.eventType, eventToApply);
        }
    }

    /// <summary>
    /// Applies an event to this object using attribute-based dispatch if possible.
    /// </summary>
    /// <param name="eventToApply">The event instance to apply.</param>
    /// <returns>
    /// <c>true</c> if an attribute-based handler was found and invoked; otherwise <c>false</c>.
    /// </returns>
    protected virtual bool TryApplyWithAttributes(IEvent eventToApply)
    {
        if (eventToApply == null)
        {
            throw new ArgumentNullException(nameof(eventToApply));
        }

        var eventType = eventToApply.eventType;
        if (eventType == null)
        {
            // If there is no event type, we cannot perform attribute-based routing.
            return false;
        }

        // Resolve the concrete type of this NostifyObject (aggregate or projection).
        var targetType = GetType();

        // Build or retrieve the handler map for this type.
        var handlerMap = ApplyEventsHandlerCache.GetOrBuildHandlerMap(targetType);
        if (!handlerMap.TryGetValue(eventType, out var handler))
        {
            // No attribute-based handler for this event type on this object.
            return false;
        }

        // Invoke the handler. We expect methods to accept a single IEvent parameter.
        handler(this, eventToApply);
        return true;
    }

    ///<summary>
    ///Applies event to this Aggregate or Projection based on its event type.
    /// Implementors should provide overloads like <c>Apply(SpecificEventType, IEvent)</c>
    /// to participate in the dynamic dispatch fallback.
    ///</summary>
    protected abstract void Apply(EventType eventType, IEvent eventToApply);

    ///<summary>
    ///Updates properties of Aggregate or Projection
    ///</summary>
    ///<param name="payload">Must be payload from Event, name of property in payload must match property name in T</param>
    public void UpdateProperties<T>(object payload) where T : NostifyObject
    {
        // Convert the payload to a JObject once and reuse for all property updates
        var jPayload = JObject.FromObject(payload);
        var payloadProps = jPayload.Children<JProperty>();

        // Use cached reflection metadata for writable properties of T
        var propertyMap = GetPropertyMap<T>();

        foreach (JProperty prop in payloadProps)
        {
            // Default behavior: map payload property to projection/aggregate property by name
            UpdatePropertyInternal<T>(prop.Name, prop.Name, jPayload, propertyMap);
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
        // Convert the payload to a JObject once and reuse for all property updates
        var jPayload = JObject.FromObject(payload);
        var payloadProps = jPayload.Children<JProperty>();

        // Use cached reflection metadata for writable properties of T
        var propertyMap = GetPropertyMap<T>();

        foreach (JProperty prop in payloadProps)
        {
            // Determine whether this payload property should be applied
            bool doUpdate = !strict || propertyPairs.ContainsKey(prop.Name);
            if (doUpdate)
            {
                // If a mapping exists, use the mapped target property name, otherwise fall back to the same name
                string propToSet = propertyPairs.ContainsKey(prop.Name) ? propertyPairs[prop.Name] : prop.Name;
                UpdatePropertyInternal<T>(propToSet, prop.Name, jPayload, propertyMap);
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
        // For callers that still pass a List<PropertyInfo> (for example legacy code paths),
        // honor that list and perform a one-off update using the optimized internal helper.
        if (thisNostifyObjectProps != null)
        {
            var propertyMap = thisNostifyObjectProps
                .Where(p => p.GetSetMethod() != null)
                .ToDictionary(p => p.Name, p => p);

            UpdatePropertyInternal<T>(propertyToSet, propertyToGetValueFrom, jPayload, propertyMap);
            return;
        }

        // If no List<PropertyInfo> is supplied, fall back to the cached property map for T.
        var cachedPropertyMap = GetPropertyMap<T>();
        UpdatePropertyInternal<T>(propertyToSet, propertyToGetValueFrom, jPayload, cachedPropertyMap);
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
        // Convert the payload to a JObject once and reuse for all property updates
        JObject jObject = JObject.FromObject(payload);

        // Use cached reflection metadata for writable properties of T
        var propertyMap = GetPropertyMap<T>();

        foreach (PropertyCheck propertyCheck in propertyCheckValues)
        {
            // Only apply mappings where the projectionIdPropertyValue matches the aggregate root id (if provided)
            if (!propertyCheck.projectionIdPropertyValue.HasValue || eventAggregateRootId == propertyCheck.projectionIdPropertyValue.Value)
            {
                JToken? jt = jObject[propertyCheck.eventPropertyName];
                if (jt != null)
                {
                    // Conditional mapping: projectionPropertyName is the target, eventPropertyName is the source
                    UpdatePropertyInternal<T>(propertyCheck.projectionPropertyName, propertyCheck.eventPropertyName, jObject, propertyMap);
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
    /// Constructor for PropertyCheck. Must be instantiated inside the Projection during Apply to get the correct projectionIdPropertyValue.
    /// </summary>
    /// <param name="projectionIdPropertyValue">The Guid ID value to match against the IEvent aggregateRootId</param>
    /// <param name="eventPropertyName">Source property name in IEvent payload</param>
    /// <param name="projectionPropertyName">Target property name in Aggregate/Projection</param>
    public PropertyCheck(Guid? projectionIdPropertyValue, string eventPropertyName, string projectionPropertyName)
    {
        this.eventPropertyName = eventPropertyName;
        this.projectionPropertyName = projectionPropertyName;
        this.projectionIdPropertyValue = projectionIdPropertyValue;
    }

    public string eventPropertyName { get; set; } 
    public string projectionPropertyName { get; set; } 
    public Guid? projectionIdPropertyValue { get; set; }
}