using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using Xunit;
using nostify;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Azure.Functions.Worker.Http;

namespace TestAggregate_Service.Tests;

public class Delete_TestAggregate_Command_Should
{
    private Mock<INostify> _nostifyMock;
    private DeleteTestAggregate _func;
    private Mock<HttpClient> _httpClientMock;
    private Mock<ILogger> _loggerMock;

    public Delete_TestAggregate_Command_Should()
    {
        _nostifyMock = new Mock<INostify>();
        _httpClientMock = new Mock<HttpClient>();
        _func = new DeleteTestAggregate(_httpClientMock.Object, _nostifyMock.Object);
        _loggerMock = new Mock<ILogger>();
    }

    [Fact]
    public async Task Insert_Delete_Event()
    {
        //Arrange
        TestAggregate test = new TestAggregate();
        HttpRequestData testReq = MockHttpRequestData.Create();
        Guid newId = Guid.NewGuid();

        // Act
        var resp = await _func.Run(testReq, newId, _loggerMock.Object);

        // Assert
        Assert.True(newId == resp);
    }


}
