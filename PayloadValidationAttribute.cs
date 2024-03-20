using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.Xml;

namespace nostify.Attributes;

public class PayloadValidation : Attribute
{
    public Type ValidationType { get; set; }

    public PayloadValidation(Type typeToValidate)
    {
        if (!typeToValidate.IsAssignableFrom(typeof(NostifyObject))){
            throw new NostifyException("Not a nostify type");
        }
        ValidationType = typeToValidate;
    } 
}