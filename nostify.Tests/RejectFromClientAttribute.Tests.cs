using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using nostify;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Verifies protection of server-managed properties from client event payloads.
/// </summary>
public sealed class RejectFromClientAttributeTests
{
    [Fact]
    public void Constructor_DefaultsToThrowingValidationError()
    {
        var attribute = new RejectFromClientAttribute();

        Assert.True(attribute.throwsError);
    }

    [Fact]
    public void Constructor_WithFalse_ShouldDisableValidationError()
    {
        var attribute = new RejectFromClientAttribute(throwsError: false);

        Assert.False(attribute.throwsError);
    }

    [Fact]
    public void AttributeUsage_ShouldTargetOneNonInheritedPropertyAttribute()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(RejectFromClientAttribute),
            typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
        Assert.IsAssignableFrom<Attribute>(new RejectFromClientAttribute());
    }

    [Fact]
    public void ValidatePayload_WithDefaultRejectedProperty_ShouldThrow()
    {
        var evt = CreateEvent(new { ServerValue = "client value" });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<ProtectedModel>());

        Assert.Contains(nameof(ProtectedModel.ServerValue), exception.Message);
        Assert.Contains("server-managed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePayload_WithoutRejectedProperty_ShouldSucceed()
    {
        var evt = CreateEvent(new { ClientValue = "allowed" });

        IEvent result = evt.ValidatePayload<ProtectedModel>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithNonThrowingRejectedProperty_ShouldRemovePropertyAndSucceed()
    {
        var evt = CreateEvent(new { IgnoredServerValue = "client value", ClientValue = "allowed" });

        IEvent result = evt.ValidatePayload<ProtectedModel>();
        JObject cleanedPayload = JObject.FromObject(evt.payload!);

        Assert.Same(evt, result);
        Assert.False(cleanedPayload.ContainsKey(nameof(ProtectedModel.IgnoredServerValue)));
        Assert.Equal("allowed", cleanedPayload.Value<string>(nameof(ProtectedModel.ClientValue)));
    }

    [Fact]
    public void ValidatePayload_WithNonThrowingRejectedRequiredProperty_ShouldIgnoreItsValidation()
    {
        var evt = CreateEvent(new { IgnoredRequiredServerValue = "client value" });

        IEvent result = evt.ValidatePayload<ProtectedModel>();
        JObject cleanedPayload = JObject.FromObject(evt.payload!);

        Assert.Same(evt, result);
        Assert.False(cleanedPayload.ContainsKey(nameof(ProtectedModel.IgnoredRequiredServerValue)));
    }

    [Fact]
    public void ValidatePayload_WithNonThrowingRejectedProperty_ShouldLogIgnoredProperty()
    {
        TextWriter originalError = Console.Error;
        using var errorOutput = new StringWriter();

        try
        {
            Console.SetError(errorOutput);
            var evt = CreateEvent(new { IgnoredServerValue = "client value" });

            evt.ValidatePayload<ProtectedModel>();
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("Ignored server-managed client payload property", errorOutput.ToString());
        Assert.Contains(nameof(ProtectedModel.IgnoredServerValue), errorOutput.ToString());
    }

    private static Event CreateEvent(object payload)
    {
        return new Event(TestEventType.Instance, Guid.NewGuid(), payload);
    }

    private sealed class ProtectedModel
    {
        [RejectFromClient]
        public string? ServerValue { get; set; }

        [RejectFromClient(throwsError: false)]
        public string? IgnoredServerValue { get; set; }

        [Required]
        [RejectFromClient(throwsError: false)]
        public string? IgnoredRequiredServerValue { get; set; }

        public string? ClientValue { get; set; }
    }

    private sealed class TestEventType : EventType
    {
        public static readonly TestEventType Instance = new();

        private TestEventType() : base("RejectFromClient_Test")
        {
        }
    }
}
