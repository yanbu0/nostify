


using System.IO;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace nostify
{
    ///<summary>
    ///Helper class for MockHttpRequestData
    ///</summary>
    public class MockHttpResponseData : HttpResponseData
    {
        ///<summary>
        ///Constructor
        ///</summary>
        public MockHttpResponseData(FunctionContext functionContext) : base(functionContext)
        {
        }


        /// <inheritdoc />
        public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        /// <inheritdoc />
        public override HttpHeadersCollection Headers { get; set; } = new HttpHeadersCollection();

        /// <inheritdoc />
        public override Stream Body { get; set; } = new MemoryStream();

        /// <inheritdoc />
        /// <remarks>Cookie mutation is not required by this lightweight response test double.</remarks>
        public override HttpCookies Cookies { get; } = null!;
    }
}