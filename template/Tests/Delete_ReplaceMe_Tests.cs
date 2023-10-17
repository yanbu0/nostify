using Microsoft.Azure.Documents;  
using Microsoft.Extensions.Logging;  
using Moq;  
using Moq.Protected;
using Newtonsoft.Json;  
using System.Collections.Generic;  
using System.IO;  
using System.Threading.Tasks;  
using Xunit;  
using nostify;
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Azure.Cosmos;

namespace _ReplaceMe__Service.Tests
{
    public class Delete__ReplaceMe__Command_Should
    {
        private Mock<INostify> _nostifyMock;
        private Create_ReplaceMe_ _func;
        private Mock<HttpClient> _httpClientMock;
        private Mock<ILogger> _loggerMock;

        public Delete__ReplaceMe__Command_Should()
        {
            _nostifyMock = new Mock<INostify>();
            _httpClientMock = new Mock<HttpClient>();
            _func = new Create_ReplaceMe_(_httpClientMock.Object, _nostifyMock.Object);
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public async Task Insert_Delete_PersistedEvent()
        {
            //Arrange
            _ReplaceMe_ test = new _ReplaceMe_();
            
            // //Act
            var resp = await _func.Run(test, _loggerMock.Object) as OkObjectResult;

            // //Assert
            Assert.NotNull(resp);
            Guid guidTest;
            Assert.True(Guid.TryParse(resp.Value.ToString(), out guidTest));
        }


    }
}