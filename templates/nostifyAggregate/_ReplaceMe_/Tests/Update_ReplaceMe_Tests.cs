using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using nostify;
using Microsoft.Azure.Functions.Worker.Http;

namespace _ServiceName__Service.Tests;

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
        Guid newId = Guid.NewGuid();
        object update_ReplaceMe_ = new {
            id = newId
        };
        _ReplaceMe_ test = new _ReplaceMe_();
        HttpRequestData testReq = MockHttpRequestData.Create(update_ReplaceMe_);

        // Act
        var resp = await _func.Run(testReq, _loggerMock.Object);

        // Assert
        Assert.True(newId == resp);
    }


}
