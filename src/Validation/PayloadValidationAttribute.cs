using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.Xml;

namespace nostify;

/// <summary>
/// Attribute for validating payloads of a specific type.
/// </summary>
public class PayloadValidation : Attribute
{
    public Type ValidationType { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadValidation"/> class with the specified type to validate.
    /// </summary>
    /// <param name="typeToValidate">The type to validate, which must be assignable from <see cref="NostifyObject"/>.</param>
    public PayloadValidation(Type typeToValidate)
    {
        if (!typeToValidate.IsAssignableFrom(typeof(NostifyObject))){
            throw new NostifyException("Not a nostify type");
        }
        ValidationType = typeToValidate;
    } 
}