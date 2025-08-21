


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


        public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public override HttpHeadersCollection Headers { get; set; } = new HttpHeadersCollection();

        public override Stream Body { get; set; } = new MemoryStream();

        public override HttpCookies Cookies { get; }
    }
}