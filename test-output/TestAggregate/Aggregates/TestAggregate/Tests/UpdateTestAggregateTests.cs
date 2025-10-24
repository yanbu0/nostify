using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using Xunit;
using nostify;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Cosmos.Linq;

namespace TestAggregate_Service.Tests;

public class Update_TestAggregate_Command_Should
{
    private Mock<INostify> _nostifyMock;
    private UpdateTestAggregate _func;
    private Mock<HttpClient> _httpClientMock;
    private Mock<ILogger> _loggerMock;

    public Update_TestAggregate_Command_Should()
    {
        _nostifyMock = new Mock<INostify>();
        _httpClientMock = new Mock<HttpClient>();
        _func = new UpdateTestAggregate(_httpClientMock.Object, _nostifyMock.Object);
        _loggerMock = new Mock<ILogger>();
    }

    [Fact]
    public async Task Insert_Update_Event()
    {
        //Arrange
        Guid newId = Guid.NewGuid();
        object updateTestAggregate = new {
            id = newId
        };
        TestAggregate test = new TestAggregate();
        HttpRequestData testReq = MockHttpRequestData.Create(updateTestAggregate);

        // Act
        var resp = await _func.Run(testReq, _loggerMock.Object);

        // Assert
        Assert.True(newId == resp);
    }


}
