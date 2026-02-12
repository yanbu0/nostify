using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using nostify;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;

namespace _ReplaceMe__Service.Tests;

public class Create__ReplaceMe__Command_Should
{
    private Mock<INostify> _nostifyMock;
    private Create_ReplaceMe_ _func;
    private Mock<HttpClient> _httpClientMock;
    private Mock<ILogger<Create_ReplaceMe_>> _loggerMock;
    private Mock<HttpRequestData> _httpReqMock;
    private Mock<FunctionContext> _functionContextMock;

    public Create__ReplaceMe__Command_Should()
    {
        _nostifyMock = new Mock<INostify>();
        _httpClientMock = new Mock<HttpClient>();
        _loggerMock = new Mock<ILogger<Create_ReplaceMe_>>();
        _func = new Create_ReplaceMe_(_httpClientMock.Object, _nostifyMock.Object, _loggerMock.Object);
        _httpReqMock = new Mock<HttpRequestData>();
        _functionContextMock = new Mock<FunctionContext>();
    }

    [Fact]
    public async Task Insert_Create_Event()
    {
        //Arrange
        _ReplaceMe_ test = new _ReplaceMe_();
        HttpRequestData testReq = MockHttpRequestData.Create(test);
        
        // Act
        var resp = await _func.Run(testReq, _functionContextMock.Object);

        // Assert
        Assert.True(resp != Guid.Empty);
    }


}
