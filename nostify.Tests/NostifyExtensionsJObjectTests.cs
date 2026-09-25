using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace nostify.Tests;

public class NostifyExtensionsJObjectTests
{
    [Fact]
    public void CanConvertToGuid()
    {
        var id = Guid.NewGuid();
        var jObj = new JObject { { "id", id } };
        var obj = (object)jObj;
        var result = obj.TryGetValue<Guid>("id", out var value);
        Assert.True(result);
        Assert.Equal(id, value);
    }

    [Fact]
    public void CanConvertToListGuid()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var jObj = new JObject { { "id", new JArray { id1, id2 } } };
        var obj = (object)jObj;
        var result = obj.TryGetValue<List<Guid>>("id", out var value);
        Assert.True(result);
        Assert.Equal(2, value.Count);
        Assert.Equal(id1, value.First());
        Assert.Equal(id2, value.Last());
    }

    [Fact]
    public void TryConvertGuidToListGuidReturnsFalse()
    {
        var id = Guid.NewGuid();
        var jObj = new JObject { { "id", id } };
        var obj = (object)jObj;
        var result = obj.TryGetValue<List<Guid>>("id", out var value);
        Assert.False(result);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetValue_MissingProperty_ReturnsFalseAndDefaultValue()
    {
        var data = new JObject { ["name"] = "present" };

        bool result = data.TryGetValue<Guid>("id", out Guid value);

        Assert.False(result);
        Assert.Equal(Guid.Empty, value);
    }

    [Fact]
    public void ToGuid_ValidValue_ReturnsParsedGuid()
    {
        Guid expected = Guid.NewGuid();

        Guid result = expected.ToString().ToGuid();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToGuid_InvalidValue_ThrowsDescriptiveFormatException()
    {
        var exception = Assert.Throws<FormatException>(() => "not-a-guid".ToGuid());

        Assert.Equal("String is not a Guid", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task ReadFromRequestBodyAsync_NoObject_ThrowsDescriptiveException(string json)
    {
        await using var body = CreateBody(json);

        var exception = await Assert.ThrowsAsync<NostifyException>(() =>
            body.ReadFromRequestBodyAsync());

        Assert.Equal("Body contains no data", exception.Message);
    }

    [Fact]
    public async Task ReadFromRequestBodyAsync_UpdateWithoutId_ThrowsDescriptiveException()
    {
        await using var body = CreateBody("{\"name\":\"updated\"}");

        var exception = await Assert.ThrowsAsync<NostifyException>(() =>
            body.ReadFromRequestBodyAsync());

        Assert.Equal("No id value found.", exception.Message);
    }

    [Fact]
    public async Task ReadFromRequestBodyAsync_CreateWithoutId_ReturnsPayload()
    {
        await using var body = CreateBody("{\"name\":\"created\"}");

        dynamic result = await body.ReadFromRequestBodyAsync(isCreate: true);

        Assert.Equal("created", (string)result.name);
    }

    [Fact]
    public async Task ReadFromRequestBodyAsync_UpdateWithId_ReturnsPayload()
    {
        Guid id = Guid.NewGuid();
        await using var body = CreateBody($"{{\"id\":\"{id}\",\"name\":\"updated\"}}");

        dynamic result = await body.ReadFromRequestBodyAsync();

        Assert.Equal(id.ToString(), (string)result.id);
        Assert.Equal("updated", (string)result.name);
    }

    /// <summary>
    /// Creates a readable UTF-8 request-body stream for extension-method tests.
    /// </summary>
    private static MemoryStream CreateBody(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
}