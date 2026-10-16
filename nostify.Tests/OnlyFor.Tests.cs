using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using nostify;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Verifies event allowlist validation provided by <see cref="OnlyForAttribute"/>.
/// </summary>
public sealed class OnlyForAttributeTests
{
    [Fact]
    public void Constructor_WithSingleEvent_ShouldExposeEvent()
    {
        var attribute = new OnlyForAttribute("OnlyFor_Create");

        Assert.Equal(["OnlyFor_Create"], attribute.Events);
    }

    [Fact]
    public void Constructor_WithParamsEvents_ShouldExposeAllEvents()
    {
        var attribute = new OnlyForAttribute("OnlyFor_Create", "OnlyFor_Update");

        Assert.Equal(["OnlyFor_Create", "OnlyFor_Update"], attribute.Events);
    }

    [Fact]
    public void Constructor_WithEventTypes_ShouldResolveLogicalNames()
    {
        var attribute = new OnlyForAttribute(typeof(CreateTestEvent), typeof(UpdateTestEvent));

        Assert.Equal(["OnlyFor_Create", "OnlyFor_Update"], attribute.Events);
    }

    [Fact]
    public void Attribute_WithEventTypes_ShouldResolveLogicalNames()
    {
        var property = typeof(TypedOnlyForModel).GetProperty(nameof(TypedOnlyForModel.Value))!;
        var attribute = (OnlyForAttribute)Attribute.GetCustomAttribute(property, typeof(OnlyForAttribute))!;

        Assert.Equal(["OnlyFor_Create", "OnlyFor_Update"], attribute.Events);
    }

    [Fact]
    public void Constructor_WithNonEventType_ShouldThrow()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new OnlyForAttribute(typeof(string)));

        Assert.Contains(nameof(EventType), exception.Message);
    }

    [Fact]
    public void Commands_ShouldReferToEventsAndBeObsolete()
    {
        var attribute = new OnlyForAttribute("OnlyFor_Create");
        var obsolete = (ObsoleteAttribute)Attribute.GetCustomAttribute(
            typeof(OnlyForAttribute).GetProperty(nameof(OnlyForAttribute.Commands))!,
            typeof(ObsoleteAttribute))!;

#pragma warning disable CS0618 // Verify the compatibility alias before its next-major-version removal.
        Assert.Same(attribute.Events, attribute.Commands);
#pragma warning restore CS0618
        Assert.Contains("next major version", obsolete.Message);
        Assert.Contains(nameof(OnlyForAttribute.Events), obsolete.Message);
    }

    [Fact]
    public void IsValid_WithAllowedEvent_ShouldReturnSuccess()
    {
        var attribute = new OnlyForAttribute("OnlyFor_Create");
        ValidationContext context = CreateContext(CreateTestEvent.Instance);

        ValidationResult? result = attribute.GetValidationResult("value", context);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithDisallowedEvent_ShouldReturnMemberScopedError()
    {
        var attribute = new OnlyForAttribute("OnlyFor_Create");
        ValidationContext context = CreateContext(UpdateTestEvent.Instance);

        ValidationResult? result = attribute.GetValidationResult("value", context);

        Assert.NotNull(result);
        Assert.Contains("TestProperty", result.ErrorMessage);
        Assert.Contains("OnlyFor_Create", result.ErrorMessage);
        Assert.Equal(["TestProperty"], result.MemberNames);
    }

    [Fact]
    public void IsValid_WithLegacyCommand_ShouldPreserveCompatibility()
    {
        var attribute = new OnlyForAttribute("OnlyFor_Create");
        var context = new ValidationContext(new object()) { MemberName = "TestProperty" };
#pragma warning disable CS0618 // Verify compatibility with legacy command validation contexts.
        context.Items["command"] = new NostifyCommand("OnlyFor_Create");
#pragma warning restore CS0618

        ValidationResult? result = attribute.GetValidationResult("value", context);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_WithoutEventType_ShouldReturnValidationError()
    {
        var attribute = new OnlyForAttribute("OnlyFor_Create");
        var context = new ValidationContext(new object()) { MemberName = "TestProperty" };

        ValidationResult? result = attribute.GetValidationResult("value", context);

        Assert.NotNull(result);
        Assert.Contains("event type to be specified", result.ErrorMessage);
    }

    [Fact]
    public void IsValid_WithCustomErrorMessage_ShouldUseCustomMessage()
    {
        var attribute = new OnlyForAttribute("OnlyFor_Create") { ErrorMessage = "Custom only-for error" };
        ValidationContext context = CreateContext(UpdateTestEvent.Instance);

        ValidationResult? result = attribute.GetValidationResult("value", context);

        Assert.Equal("Custom only-for error", result!.ErrorMessage);
    }

    [Fact]
    public void ValidatePayload_WithDisallowedProperty_ShouldThrow()
    {
        var payload = new { RestrictedValue = "not allowed" };
        var evt = new Event(UpdateTestEvent.Instance, Guid.NewGuid(), payload);

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<OnlyForAggregate>());

        Assert.Contains(nameof(OnlyForAggregate.RestrictedValue), exception.Message);
        Assert.Contains("OnlyFor_Create", exception.Message);
    }

    [Fact]
    public void ValidatePayload_WithAllowedProperty_ShouldSucceed()
    {
        var payload = new { RestrictedValue = "allowed" };
        var evt = new Event(CreateTestEvent.Instance, Guid.NewGuid(), payload);

        IEvent result = evt.ValidatePayload<OnlyForAggregate>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithOmittedDisallowedProperty_ShouldSucceed()
    {
        var payload = new { UnrestrictedValue = "allowed" };
        var evt = new Event(UpdateTestEvent.Instance, Guid.NewGuid(), payload);

        IEvent result = evt.ValidatePayload<OnlyForAggregate>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithRequiredForAndOnlyFor_ShouldRequirePropertyForAllowedEvent()
    {
        var evt = new Event(CreateTestEvent.Instance, Guid.NewGuid(), new { });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<CombinedValidationAggregate>());

        Assert.Contains(nameof(CombinedValidationAggregate.RestrictedValue), exception.Message);
        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePayload_WithRequiredForAndOnlyFor_ShouldAcceptPropertyForAllowedEvent()
    {
        var payload = new { RestrictedValue = "allowed and required" };
        var evt = new Event(CreateTestEvent.Instance, Guid.NewGuid(), payload);

        IEvent result = evt.ValidatePayload<CombinedValidationAggregate>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithRequiredForAndOnlyFor_ShouldRejectPropertyForDifferentEvent()
    {
        var payload = new { RestrictedValue = "not allowed" };
        var evt = new Event(UpdateTestEvent.Instance, Guid.NewGuid(), payload);

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<CombinedValidationAggregate>());

        Assert.Contains(nameof(CombinedValidationAggregate.RestrictedValue), exception.Message);
        Assert.Contains("OnlyFor_Create", exception.Message);
    }

    [Fact]
    public void ValidatePayload_WithRequiredForAndOnlyFor_ShouldAllowOmissionForDifferentEvent()
    {
        var evt = new Event(UpdateTestEvent.Instance, Guid.NewGuid(), new { });

        IEvent result = evt.ValidatePayload<CombinedValidationAggregate>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void AttributeUsageAndInheritance_ShouldMatchValidationPattern()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(OnlyForAttribute),
            typeof(AttributeUsageAttribute))!;
        var attribute = new OnlyForAttribute("OnlyFor_Create");

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
        Assert.IsAssignableFrom<ValidationAttribute>(attribute);
        Assert.IsAssignableFrom<INostifyValidation>(attribute);
        Assert.False(typeof(OnlyForAttribute).GetProperty(nameof(OnlyForAttribute.Events))!.CanWrite);
        Assert.IsType<List<string>>(attribute.Events);
    }

    private static ValidationContext CreateContext(EventType eventType)
    {
        var context = new ValidationContext(new object()) { MemberName = "TestProperty" };
        context.Items["eventType"] = eventType;
        return context;
    }

    private sealed class TypedOnlyForModel
    {
        [OnlyFor(typeof(CreateTestEvent), typeof(UpdateTestEvent))]
        public string? Value { get; set; }
    }

    private sealed class OnlyForAggregate
    {
        [OnlyFor(typeof(CreateTestEvent))]
        public string? RestrictedValue { get; set; }

        public string? UnrestrictedValue { get; set; }
    }

    private sealed class CombinedValidationAggregate
    {
        [RequiredFor(typeof(CreateTestEvent))]
        [OnlyFor(typeof(CreateTestEvent))]
        public string? RestrictedValue { get; set; }
    }

    private sealed class CreateTestEvent : EventType
    {
        public static readonly CreateTestEvent Instance = new();

        public CreateTestEvent() : base("OnlyFor_Create") { }
    }

    private sealed class UpdateTestEvent : EventType
    {
        public static readonly UpdateTestEvent Instance = new();

        public UpdateTestEvent() : base("OnlyFor_Update") { }
    }
}
