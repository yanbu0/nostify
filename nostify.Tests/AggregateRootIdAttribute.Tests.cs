using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using nostify;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Verifies aggregate identity validation for event payloads.
/// </summary>
public sealed class AggregateRootIdAttributeTests
{
    [Fact]
    public void IsValid_WithExactAggregateRootId_ShouldReturnSuccess()
    {
        Guid aggregateRootId = Guid.NewGuid();
        var attribute = new AggregateRootIdAttribute();
        ValidationContext context = CreateContext(aggregateRootId);

        ValidationResult? result = attribute.GetValidationResult(aggregateRootId, context);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithDifferentAggregateRootId_ShouldReturnMemberScopedError()
    {
        Guid aggregateRootId = Guid.NewGuid();
        var attribute = new AggregateRootIdAttribute();
        ValidationContext context = CreateContext(aggregateRootId);

        ValidationResult? result = attribute.GetValidationResult(Guid.NewGuid(), context);

        Assert.NotNull(result);
        Assert.Contains("id", result.ErrorMessage);
        Assert.Contains(aggregateRootId.ToString(), result.ErrorMessage);
        Assert.Equal(["id"], result.MemberNames);
    }

    [Fact]
    public void IsValid_WithoutAggregateRootIdContext_ShouldReturnSuccess()
    {
        var attribute = new AggregateRootIdAttribute();
        var context = new ValidationContext(new object()) { MemberName = "id" };

        ValidationResult? result = attribute.GetValidationResult(Guid.NewGuid(), context);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithDifferentIdAndCustomErrorMessage_ShouldUseCustomMessage()
    {
        var attribute = new AggregateRootIdAttribute { ErrorMessage = "Identity cannot change." };
        ValidationContext context = CreateContext(Guid.NewGuid());

        ValidationResult? result = attribute.GetValidationResult(Guid.NewGuid(), context);

        Assert.Equal("Identity cannot change.", result!.ErrorMessage);
    }

    [Fact]
    public void AttributeUsage_ShouldTargetOneNonInheritedPropertyAttribute()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AggregateRootIdAttribute),
            typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
        Assert.IsAssignableFrom<ValidationAttribute>(new AggregateRootIdAttribute());
    }

    [Fact]
    public void NostifyObjectId_ShouldHaveAggregateRootIdAttribute()
    {
        object[] attributes = typeof(NostifyObject)
            .GetProperty(nameof(NostifyObject.id))!
            .GetCustomAttributes(typeof(AggregateRootIdAttribute), inherit: false);

        Assert.Single(attributes);
    }

    [Fact]
    public void ValidatePayload_WithMatchingId_ShouldSucceed()
    {
        Guid aggregateRootId = Guid.NewGuid();
        var evt = new Event(TestEventType.Instance, aggregateRootId, new { id = aggregateRootId });

        IEvent result = evt.ValidatePayload<TestAggregate>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithDifferentId_ShouldRejectIdentityChange()
    {
        Guid aggregateRootId = Guid.NewGuid();
        Guid differentId = Guid.NewGuid();
        var evt = new Event(TestEventType.Instance, aggregateRootId, new { id = differentId });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<TestAggregate>());

        Assert.Contains(nameof(NostifyObject.id), exception.Message);
        Assert.Contains(aggregateRootId.ToString(), exception.Message);
    }

    [Fact]
    public void ValidatePayload_WithOmittedId_ShouldAllowPartialUpdate()
    {
        Guid aggregateRootId = Guid.NewGuid();
        var evt = new Event(TestEventType.Instance, aggregateRootId, new { name = "updated" });

        IEvent result = evt.ValidatePayload<TestAggregate>();

        Assert.Same(evt, result);
    }

    private static ValidationContext CreateContext(Guid aggregateRootId)
    {
        var context = new ValidationContext(new object()) { MemberName = "id" };
        context.Items["aggregateRootId"] = aggregateRootId;
        return context;
    }

    private sealed class TestEventType : EventType
    {
        public static readonly TestEventType Instance = new();

        private TestEventType() : base("AggregateRootId_Test")
        {
        }
    }
}
