using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using nostify.Attributes;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.Schemas;

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
    
    ///<summary>
    ///Id of tenant of logged in user
    ///</summary>
    public Guid tenantId { get; set; }

    
    ///<summary>
    ///Unique value for Aggregate
    ///</summary>
    [RequiredForCreate]
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
        var nosObjProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToList();
        var jPayload = JObject.FromObject(payload);
        var payloadProps = jPayload.Children<JProperty>();

        foreach (JProperty prop in payloadProps)
        {
            PropertyInfo propToUpdate = nosObjProps.Where(p => p.Name == prop.Name).SingleOrDefault();
            if (propToUpdate != null){
                var eg = typeof(NostifyExtensions).GetMethod("GetValue");
                var getValueRef = eg.MakeGenericMethod(propToUpdate.PropertyType);
                var valueToSet = getValueRef.Invoke(null, new object[] {jPayload, propToUpdate.Name });
                typeof(T).GetProperty(propToUpdate.Name).SetValue(this, valueToSet);
            }
        }
    }
}
