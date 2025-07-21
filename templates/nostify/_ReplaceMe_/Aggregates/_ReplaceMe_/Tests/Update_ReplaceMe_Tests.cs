using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using Xunit;
using nostify;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ReplaceMe__Service.Tests;

public class Update__ReplaceMe__Command_Should
{
    private Mock<INostify> _nostifyMock;
    private Update_ReplaceMe_ _func;
    private Mock<HttpClient> _httpClientMock;
    private Mock<ILogger> _loggerMock;

    public Update__ReplaceMe__Command_Should()
    {
        _nostifyMock = new Mock<INostify>();
        _httpClientMock = new Mock<HttpClient>();
        _func = new Update_ReplaceMe_(_httpClientMock.Object, _nostifyMock.Object);
        _loggerMock = new Mock<ILogger>();
    }

    [Fact]
    public async Task Insert_Update_Event()
    {
        //Arrange
        object update_ReplaceMe_ = new {
            id = Guid.NewGuid()
        };
        _ReplaceMe_ test = new _ReplaceMe_();
        HttpRequestData testReq = MockHttpRequestData.Create(update_ReplaceMe_);

        // Act
        var resp = await _func.Run(testReq, _loggerMock.Object);

        // Assert
        Assert.NotNull(resp);
        Guid guidTest;
        Assert.True(Guid.TryParse(resp.ToString(), out guidTest));
    }


}
