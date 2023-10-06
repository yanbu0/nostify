using Microsoft.Azure.Documents;  
using Microsoft.Extensions.Logging;  
using Moq;  
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

namespace _ReplaceMe__Service.Tests
{
    public class Create__ReplaceMe__Command_Should
    {
        private Mock<Nostify> _nostifyMock;
        private Create_ReplaceMe_ _func;
        private Mock<HttpClient> _httpClientMock;
        private Mock<ILogger> _loggerMock;

        public Create__ReplaceMe__Command_Should()
        {
            _nostifyMock = new Mock<INostify>();
            _httpClientMock = new Mock<HttpClient>();
            _func = new Create_ReplaceMe_(_httpClientMock.Object, _nostifyMock.Object);
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public async Task Insert_Create_PersistedEvent()
        {
            //Arrange
            _ReplaceMe_ test = new _ReplaceMe_();

            //Act
            var resp = await _func.Run(test, _loggerMock.Object);

            //Assert
            Assert.NotNull(resp as OkObjectResult);
            Guid guidTest;
            Assert.True(Guid.TryParse(resp.ToString(), out guidTest));
        }


    }
}