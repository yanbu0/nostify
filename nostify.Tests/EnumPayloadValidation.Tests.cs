using System.ComponentModel.DataAnnotations;

namespace nostify.Tests;

/// <summary>
/// Verifies raw enum payload validation and event-aware attribute behavior for enum properties.
/// </summary>
public sealed class EnumPayloadValidationTests
{
    [Fact]
    public void ValidatePayload_WithUnknownEnumString_ShouldReturnStructuredValidationError()
    {
        Event evt = CreateEvent(new { status = "NotAStatus" });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<EnumPayload>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal("status", Assert.Single(result.MemberNames));
        Assert.Contains("not a valid declared value", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePayload_WithUndefinedNumericEnumValue_ShouldFail()
    {
        Event evt = CreateEvent(new { status = 99 });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<EnumPayload>());

        Assert.Equal("status", Assert.Single(Assert.Single(exception.ValidationMessages).MemberNames));
    }

    [Fact]
    public void ValidatePayload_WithNumericValueOutsideUnderlyingRange_ShouldFail()
    {
        Event evt = CreateEvent(new { byteStatus = 256 });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<EnumPayload>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal("byteStatus", Assert.Single(result.MemberNames));
        Assert.Contains(nameof(ByteStatus), result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatePayload_WithOmittedNonNullableEnum_ShouldRemainAValidPartialUpdate()
    {
        Event evt = CreateEvent(new { unrelatedValue = "partial update" });

        IEvent result = evt.ValidatePayload<EnumPayload>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithNullNullableEnum_ShouldSucceed()
    {
        Event evt = CreateEvent(new { nullableStatus = (string?)null });

        IEvent result = evt.ValidatePayload<EnumPayload>();

        Assert.Same(evt, result);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("completed")]
    [InlineData(1)]
    public void ValidatePayload_WithDeclaredEnumValue_ShouldSucceed(object status)
    {
        Event evt = CreateEvent(new { status });

        IEvent result = evt.ValidatePayload<EnumPayload>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithInvalidNestedEnum_ShouldReturnQualifiedPath()
    {
        Event evt = CreateEvent(new
        {
            nested = new { status = "NotAStatus" }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<EnumPayload>());

        Assert.Equal("nested.status", Assert.Single(Assert.Single(exception.ValidationMessages).MemberNames));
    }

    [Fact]
    public void ValidatePayload_WithInvalidEnumInCollection_ShouldReturnIndexedPath()
    {
        Event evt = CreateEvent(new
        {
            items = new[]
            {
                new { status = "Pending" },
                new { status = "NotAStatus" }
            }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<EnumPayload>());

        Assert.Equal("items[1].status", Assert.Single(Assert.Single(exception.ValidationMessages).MemberNames));
    }

    [Fact]
    public void ValidatePayload_WithOmittedRequiredForEnum_ShouldFail()
    {
        Event evt = CreateEvent(new { unrelatedValue = "missing required enum" });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<RequiredEnumPayload>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal("status", Assert.Single(result.MemberNames));
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePayload_WithExplicitDefaultRequiredForEnum_ShouldSucceed()
    {
        Event evt = CreateEvent(new { status = 0 });

        IEvent result = evt.ValidatePayload<RequiredEnumPayload>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithOnlyForEnumOnDisallowedEvent_ShouldFail()
    {
        Event evt = CreateEvent(new { status = "Pending" }, UpdateEnumEvent.Instance);

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<OnlyEnumPayload>());

        Assert.Equal("status", Assert.Single(Assert.Single(exception.ValidationMessages).MemberNames));
    }

    [Fact]
    public void ValidatePayload_WithOnlyForEnumOnAllowedEvent_ShouldSucceed()
    {
        Event evt = CreateEvent(new { status = "Pending" });

        IEvent result = evt.ValidatePayload<OnlyEnumPayload>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithPartialTogetherForEnumGroup_ShouldFail()
    {
        Event evt = CreateEvent(new { status = "Pending" });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<TogetherEnumPayload>());

        Assert.Contains(
            exception.ValidationMessages.SelectMany(result => result.MemberNames),
            memberName => memberName == "reason");
    }

    [Fact]
    public void ValidatePayload_WithCompleteTogetherForEnumGroup_ShouldSucceed()
    {
        Event evt = CreateEvent(new { status = "Completed", reason = "finished" });

        IEvent result = evt.ValidatePayload<TogetherEnumPayload>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithRequiredForEnumActivatingTogetherForGroup_ShouldRequireCompanion()
    {
        Event evt = CreateEvent(new { status = "Pending" });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<RequiredTogetherEnumPayload>());

        Assert.Contains(
            exception.ValidationMessages.SelectMany(result => result.MemberNames),
            memberName => memberName == "reason");
    }

    private static Event CreateEvent(object payload, EventType? eventType = null)
        => new(eventType ?? CreateEnumEvent.Instance, Guid.NewGuid(), payload);

    private enum Status
    {
        Unknown = 0,
        Pending = 1,
        Completed = 2
    }

    private enum ByteStatus : byte
    {
        Unknown = 0,
        Ready = 1
    }

    private sealed class EnumPayload
    {
        public Status status { get; set; }

        public ByteStatus byteStatus { get; set; }

        public Status? nullableStatus { get; set; }

        public NestedEnumPayload? nested { get; set; }

        public List<NestedEnumPayload>? items { get; set; }

        public string? unrelatedValue { get; set; }
    }

    private sealed class NestedEnumPayload
    {
        public Status status { get; set; }
    }

    private sealed class RequiredEnumPayload
    {
        [RequiredFor(typeof(CreateEnumEvent))]
        public Status status { get; set; }

        public string? unrelatedValue { get; set; }
    }

    private sealed class OnlyEnumPayload
    {
        [OnlyFor(typeof(CreateEnumEvent))]
        public Status status { get; set; }
    }

    private sealed class TogetherEnumPayload
    {
        [TogetherFor("statusDetails", typeof(CreateEnumEvent))]
        public Status status { get; set; }

        [TogetherFor("statusDetails", typeof(CreateEnumEvent))]
        public string? reason { get; set; }
    }

    private sealed class RequiredTogetherEnumPayload
    {
        [RequiredFor(typeof(CreateEnumEvent))]
        [TogetherFor("statusDetails", typeof(CreateEnumEvent))]
        public Status status { get; set; }

        [TogetherFor("statusDetails", typeof(CreateEnumEvent))]
        public string? reason { get; set; }
    }

    private sealed class CreateEnumEvent : EventType
    {
        public static readonly CreateEnumEvent Instance = new();

        private CreateEnumEvent()
            : base("EnumValidation_Create")
        {
        }
    }

    private sealed class UpdateEnumEvent : EventType
    {
        public static readonly UpdateEnumEvent Instance = new();

        private UpdateEnumEvent()
            : base("EnumValidation_Update")
        {
        }
    }
}
