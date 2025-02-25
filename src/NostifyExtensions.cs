using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Data;

namespace nostify
{
    ///<summary>
    ///Extension methods for Nostify
    ///</summary>
    public static class NostifyExtensions
    {
        ///<summary>
        ///Outputs value of a property from an object, if it exists
        ///</summary>
        public static bool TryGetValue<T>(this object data, string propertyName, out T value)
        {
            JObject jObj = JObject.FromObject(data);
            return jObj.TryGetValue(propertyName, out value);
        }

        ///<summary>
        ///Outputs value of a property from a JObject, if it exists
        ///</summary>
        public static bool TryGetValue<T>(this JObject data, string propertyName, out T value)
        {
            List<JToken> jToken = data.Children<JProperty>()
                        .Where(p => p.Name == propertyName)
                        .Select(u => u.Value)
                        .ToList();

            if (jToken.Count == 0)
            {
                value = default(T);
                return false;
            }
            else 
            {
                try
                {
                    value = jToken.First().ToObject<T>();
                    return true;
                }
                catch (Exception)
                {
                    value = default(T);
                    return false;
                }
            }
        }

        ///<summary>
        ///Gets a typed value from JObject by property name
        ///</summary>
        public static T GetValue<T>(this JObject data, string propertyName)
        {
            JToken jToken = data.Children<JProperty>()
                        .Where(p => p.Name == propertyName)
                        .Select(u => u.Value)
                        .Single();

            T retVal = jToken.ToObject<T>();

            return retVal;
        }

        ///<summary>
        ///Helps with creating a partition key from string when there are conflicting class names
        ///</summary>
        public static PartitionKey ToPartitionKey(this string value)
        {
            return new PartitionKey(value);
        }

        ///<summary>
        ///Helps with creating a partition key from string when there are conflicting class names
        ///</summary>
        public static PartitionKey ToPartitionKey(this Guid value)
        {
            return new PartitionKey(value.ToString());
        }

        ///<summary>
        ///Converts string to Guid if it can
        ///</summary>
        public static Guid ToGuid(this string value)
        {
            return Guid.TryParse(value, out Guid guid) ? guid : throw new FormatException("String is not a Guid");
        }

        
        ///<summary>
        ///Reads from Stream into <c>dynamic</c>
        ///<para>
        /// Use to read the results from <c>HttpRequestData.Body</c> into an object that can update a Projection or Aggregate by calling <c>Apply()</c>.  Will throw error if no data.
        /// </para>
        ///</summary>
        ///<param name="body">HttpRequestData.Body Stream to read from</param>
        ///<param name="isCreate">Set to true if this should be a Create command.  Will throw error if no "id" property is found or id = null in resulting object.</param>
        ///<returns>dynamic object representing the payload of an <c>Event</c></returns>
        public static async Task<dynamic> ReadFromRequestBodyAsync(this Stream body, bool isCreate = false)
        {
            //Read body, throw error if null
            dynamic updateObj = JsonConvert.DeserializeObject<dynamic>(await new StreamReader(body).ReadToEndAsync()) ?? throw new NostifyException("Body contains no data");
            
            //Check for "id" property, throw error if not exists.  Ignore if isCreate is true since create objects don't have ids yet.
            if (!isCreate && updateObj.id == null)
            {
                throw new NostifyException("No id value found.");
            }

            return updateObj;
        }

        
    }
}