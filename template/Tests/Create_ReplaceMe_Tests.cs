using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using Xunit;
using nostify;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service.Tests
{
    public class Create__ReplaceMe__Command_Should
    {
        private Mock<INostify> _nostifyMock;
        private Create_ReplaceMe_ _func;
        private Mock<HttpClient> _httpClientMock;
        private Mock<ILogger> _loggerMock;
        private Mock<HttpRequestData> _httpReqMock;

        public Create__ReplaceMe__Command_Should()
        {
            _nostifyMock = new Mock<INostify>();
            _httpClientMock = new Mock<HttpClient>();
            _func = new Create_ReplaceMe_(_httpClientMock.Object, _nostifyMock.Object);
            _loggerMock = new Mock<ILogger>();
            _httpReqMock = new Mock<HttpRequestData>();
        }

        [Fact]
        public async Task Insert_Create_PersistedEvent()
        {
            //Arrange
            _ReplaceMe_ test = new _ReplaceMe_();
            HttpRequestData testReq = MockHttpRequestData.Create(test);
            
            // //Act
            var resp = await _func.Run(testReq, _loggerMock.Object) as OkObjectResult;

            // //Assert
            Assert.NotNull(resp);
            Guid guidTest;
            Assert.True(Guid.TryParse(resp.Value.ToString(), out guidTest));
        }


    }
}