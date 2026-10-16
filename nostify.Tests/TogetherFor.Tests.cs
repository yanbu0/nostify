using System.ComponentModel.DataAnnotations;
using nostify;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Verifies grouped payload-presence validation performed by <see cref="TogetherForAttribute"/>.
/// </summary>
public sealed class TogetherForAttributeTests
{
    [Fact]
    public void Constructor_WithStringEvents_ExposesGroupAndEvents()
    {
        var attribute = new TogetherForAttribute("address", "TogetherFor_Create", "TogetherFor_Update");

        Assert.Equal("address", attribute.UniqueGroupName);
        Assert.Equal(["TogetherFor_Create", "TogetherFor_Update"], attribute.Events);
        Assert.IsAssignableFrom<ValidationAttribute>(attribute);
        Assert.IsAssignableFrom<INostifyValidation>(attribute);
    }

    [Fact]
    public void Constructor_WithEventTypes_ResolvesConfiguredNames()
    {
        var attribute = new TogetherForAttribute(
            "address",
            typeof(CreateTogetherEvent),
            typeof(UpdateTogetherEvent));

        Assert.Equal(["TogetherFor_Create", "TogetherFor_Update"], attribute.Events);
    }

    [Fact]
    public void Constructor_WithInvalidEventType_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new TogetherForAttribute("address", typeof(string)));

        Assert.Contains(nameof(EventType), exception.Message);
    }

    [Fact]
    public void ValidatePayload_WithNoGroupMembers_AllowsOptionalGroup()
    {
        Event evt = CreateEvent(CreateTogetherEvent.Instance, new { Notes = "unchanged" });

        IEvent result = evt.ValidatePayload<GroupedModel>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithAllGroupMembers_Succeeds()
    {
        Event evt = CreateEvent(CreateTogetherEvent.Instance, new
        {
            Start = (string?)null,
            End = "2026-10-16"
        });

        IEvent result = evt.ValidatePayload<GroupedModel>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithOneGroupMember_RequiresEveryOtherMember()
    {
        Event evt = CreateEvent(CreateTogetherEvent.Instance, new { Start = "2026-10-15" });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<GroupedModel>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal(nameof(GroupedModel.End), Assert.Single(result.MemberNames));
        Assert.Contains("schedule", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatePayload_ForUnconfiguredEvent_DoesNotRequireGroup()
    {
        Event evt = CreateEvent(DeleteTogetherEvent.Instance, new { Start = "2026-10-15" });

        IEvent result = evt.ValidatePayload<GroupedModel>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithTwoIndependentGroups_ValidatesOnlyActivatedGroup()
    {
        Event evt = CreateEvent(CreateTogetherEvent.Instance, new { Latitude = 42.0 });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<GroupedModel>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal(nameof(GroupedModel.Longitude), Assert.Single(result.MemberNames));
        Assert.DoesNotContain(nameof(GroupedModel.Start), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(GroupedModel.End), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatePayload_WithRequiredForOnGroupMember_RequiresEntireGroup()
    {
        Event evt = CreateEvent(CreateTogetherEvent.Instance, new { });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<RequiredGroupedModel>());

        string[] members = exception.ValidationMessages
            .SelectMany(result => result.MemberNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(member => member, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([nameof(RequiredGroupedModel.End), nameof(RequiredGroupedModel.Start)], members);
    }

    [Fact]
    public void ValidatePayload_WithRequiredForAndCompleteGroup_Succeeds()
    {
        Event evt = CreateEvent(CreateTogetherEvent.Instance, new
        {
            Start = "2026-10-15",
            End = "2026-10-16"
        });

        IEvent result = evt.ValidatePayload<RequiredGroupedModel>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithNestedPartialGroup_ReturnsQualifiedMemberPath()
    {
        Event evt = CreateEvent(CreateTogetherEvent.Instance, new
        {
            Window = new { Start = "2026-10-15" }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<NestedGroupedModel>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal("Window.End", Assert.Single(result.MemberNames));
    }

    [Fact]
    public void AttributeUsage_AllowsMultipleGroupsOnOneProperty()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(TogetherForAttribute),
            typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    private static Event CreateEvent(EventType eventType, object payload)
        => new(eventType, Guid.NewGuid(), payload);

    private sealed class GroupedModel
    {
        [TogetherFor("schedule", "TogetherFor_Create", "TogetherFor_Update")]
        public string? Start { get; set; }

        [TogetherFor("schedule", "TogetherFor_Create", "TogetherFor_Update")]
        public string? End { get; set; }

        [TogetherFor("location", "TogetherFor_Create")]
        public double? Latitude { get; set; }

        [TogetherFor("location", "TogetherFor_Create")]
        public double? Longitude { get; set; }

        public string? Notes { get; set; }
    }

    private sealed class RequiredGroupedModel
    {
        [RequiredFor("TogetherFor_Create")]
        [TogetherFor("schedule", "TogetherFor_Create")]
        public string? Start { get; set; }

        [TogetherFor("schedule", "TogetherFor_Create")]
        public string? End { get; set; }
    }

    private sealed class NestedGroupedModel
    {
        public GroupedWindow? Window { get; set; }
    }

    private sealed class GroupedWindow
    {
        [TogetherFor("schedule", "TogetherFor_Create")]
        public string? Start { get; set; }

        [TogetherFor("schedule", "TogetherFor_Create")]
        public string? End { get; set; }
    }

    private sealed class CreateTogetherEvent : EventType
    {
        public static readonly CreateTogetherEvent Instance = new();

        public CreateTogetherEvent() : base("TogetherFor_Create") { }
    }

    private sealed class UpdateTogetherEvent : EventType
    {
        public UpdateTogetherEvent() : base("TogetherFor_Update") { }
    }

    private sealed class DeleteTogetherEvent : EventType
    {
        public static readonly DeleteTogetherEvent Instance = new();

        public DeleteTogetherEvent() : base("TogetherFor_Delete") { }
    }
}
