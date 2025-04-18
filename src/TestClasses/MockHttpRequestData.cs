using System.IO;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json;

namespace nostify
{
    ///<summary>
    ///Use for testing HttpTrigger Azure Functions
    ///</summary>
    public static class MockHttpRequestData
    { 
        ///<summary>
        ///Creates HttpRequestData object with empty body.
        ///</summary>
        public static HttpRequestData Create()
        {
            return Create<string>("");
        }   
        
        ///<summary>
        ///Creates HttpRequestData object, serializing the parameter object into the body
        ///</summary>
        ///<param name="requestData">The object you want to inject into the HttpRequestData body for testing</param>
        public static HttpRequestData Create<T>(T requestData) where T : class
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddFunctionsWorkerDefaults();

            var serializedData = JsonConvert.SerializeObject(requestData);
            var bodyDataStream = new MemoryStream(Encoding.UTF8.GetBytes(serializedData));

            var context = new Mock<FunctionContext>();
            context.SetupProperty(context => context.InstanceServices, serviceCollection.BuildServiceProvider());

            var request = new Mock<HttpRequestData>(context.Object);
            request.Setup(r => r.Body).Returns(bodyDataStream);
            request.Setup(r => r.CreateResponse()).Returns(new MockHttpResponseData(context.Object));

            return request.Object;
        }
    }
}