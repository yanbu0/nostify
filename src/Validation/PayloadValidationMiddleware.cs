using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace nostify
{

    /// <summary>
    /// Middleware for validating payloads in HTTP requests.
    /// </summary>
    public class PayloadValidationMiddleware
    {
        private readonly RequestDelegate _next;
        /// <summary>
        /// Initializes a new instance of the <see cref="PayloadValidationMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the HTTP request pipeline.</param>
        public PayloadValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }
        /// <summary>
        /// Invokes the middleware to validate the payload in the HTTP request.
        /// </summary>
        /// <param name="context">The HTTP context of the current request.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            // check for the attribute and skip everything else if it exists
            var endpoint = context.Features.Get<IEndpointFeature>()?.Endpoint;
            var attribute = endpoint?.Metadata.GetMetadata<PayloadValidation>();
            if (attribute != null)
            {
                //Get type and list of properties
                Type typeToValidate = attribute.ValidationType;
                var props = typeToValidate.GetProperties().Select(p => p.Name).ToList();

                //Get object from body and check of all props are in type
                var objectToValidate = await context.Request.Body.ReadFromRequestBodyAsync();
                foreach (var prop in objectToValidate.GetType().GetProperties())
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
}