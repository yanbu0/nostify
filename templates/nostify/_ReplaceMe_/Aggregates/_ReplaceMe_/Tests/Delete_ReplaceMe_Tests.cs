using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using nostify;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;

namespace _ReplaceMe__Service.Tests;

public class Delete__ReplaceMe__Command_Should
{
    private Mock<INostify> _nostifyMock;
    private Delete_ReplaceMe_ _func;
    private Mock<HttpClient> _httpClientMock;
    private Mock<ILogger> _loggerMock;
    private Mock<FunctionContext> _functionContextMock;

    public Delete__ReplaceMe__Command_Should()
    {
        _nostifyMock = new Mock<INostify>();
        _httpClientMock = new Mock<HttpClient>();
        _func = new Delete_ReplaceMe_(_httpClientMock.Object, _nostifyMock.Object);
        _loggerMock = new Mock<ILogger>();
        _functionContextMock = new Mock<FunctionContext>();
    }

    [Fact]
    public async Task Insert_Delete_Event()
    {
        //Arrange
        _ReplaceMe_ test = new _ReplaceMe_();
        HttpRequestData testReq = MockHttpRequestData.Create();
        Guid newId = Guid.NewGuid();

        // Act
        var resp = await _func.Run(testReq, _functionContextMock.Object, newId, _loggerMock.Object);

        // Assert
        Assert.True(newId == resp);
    }


}
