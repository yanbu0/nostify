using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using Xunit;
using nostify;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ServiceName_Service.Tests;

public class Delete__ReplaceMe__Command_Should
{
    private Mock<INostify> _nostifyMock;
    private Delete_ReplaceMe_ _func;
    private Mock<HttpClient> _httpClientMock;
    private Mock<ILogger> _loggerMock;

    public Delete__ReplaceMe__Command_Should()
    {
        _nostifyMock = new Mock<INostify>();
        _httpClientMock = new Mock<HttpClient>();
        _func = new Delete_ReplaceMe_(_httpClientMock.Object, _nostifyMock.Object);
        _loggerMock = new Mock<ILogger>();
    }

    [Fact]
    public async Task Insert_Delete_Event()
    {
        //Arrange
        _ReplaceMe_ test = new _ReplaceMe_();
        HttpRequestData testReq = MockHttpRequestData.Create();
        
        // //Act
        var resp = await _func.Run(testReq, Guid.NewGuid().ToString(), _loggerMock.Object) as OkObjectResult;

        // //Assert
        Assert.NotNull(resp);
        Guid guidTest;
        Assert.True(Guid.TryParse(resp.Value.ToString(), out guidTest));
    }


}
