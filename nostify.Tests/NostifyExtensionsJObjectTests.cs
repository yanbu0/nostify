using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;
using Moq;
using nostify;
using System.ComponentModel.DataAnnotations;


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
}