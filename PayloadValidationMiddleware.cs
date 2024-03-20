using System;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq;
using Microsoft.Azure.Cosmos;
using System.Xml;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using nostify.Attributes;

namespace nostify.Middleware;

public class PayloadValidationMiddleware
{
    private readonly RequestDelegate _next;
    public PayloadValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    public async Task InvokeAsync(HttpContext context)
    {
        // check for the attribute and skip everything else if it exists
        var endpoint = context.Features.Get<IEndpointFeature>()?.Endpoint;
        var attribute = endpoint?.Metadata.GetMetadata<PayloadValidation>();
        if(attribute != null)
        {
            //Get type and list of properties
            Type typeToValidate = attribute.ValidationType;
            var props = typeToValidate.GetProperties().Select(p => p.Name).ToList();

            //Get object from body and check of all props are in type
            var objectToValidate = await context.Request.Body.ReadFromRequestBodyAsync();
            foreach(var prop in objectToValidate.GetType().GetProperties())
            {
                if (!props.Contains(prop.Name))
                {
                    throw new NostifyException($"Invalid property '{prop.Name}'");
                }
            }

        }  
        // When finished, call the next delegate/middleware in the pipeline.
        await _next(context);
    }
}