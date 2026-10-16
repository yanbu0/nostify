using System.ComponentModel.DataAnnotations;

namespace nostify.Tests;

/// <summary>
/// Verifies recursive validation of nested event payload object graphs.
/// </summary>
public sealed class RecursivePayloadValidationTests
{
    [Fact]
    public void ValidatePayload_WithInvalidNestedObject_ShouldReturnQualifiedMemberPath()
    {
        var evt = CreateEvent(new
        {
            profile = new
            {
                address = new { postalCode = "invalid" }
            }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<RootPayload>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal("profile.address.postalCode", Assert.Single(result.MemberNames));
        Assert.Contains("five digits", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePayload_WithInvalidCollectionElements_ShouldValidateEveryElement()
    {
        var evt = CreateEvent(new
        {
            payloadListThingy = new[]
            {
                new { name = (string?)"valid", eventValue = "present" },
                new { name = (string?)null, eventValue = "present" },
                new { name = (string?)null, eventValue = "present" }
            }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<RootPayload>());

        string[] memberNames = exception.ValidationMessages
            .SelectMany(result => result.MemberNames)
            .OrderBy(memberName => memberName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["payloadListThingy[1].name", "payloadListThingy[2].name"],
            memberNames);
    }

    [Fact]
    public void ValidatePayload_WithValidDeepObjectGraph_ShouldSucceed()
    {
        var evt = CreateEvent(new
        {
            profile = new
            {
                address = new { postalCode = "12345" }
            },
            payloadListThingy = new[]
            {
                new { name = "first", eventValue = "present" },
                new { name = "second", eventValue = "present" }
            }
        });

        IEvent result = evt.ValidatePayload<RootPayload>();

        Assert.Same(evt, result);
    }

    [Fact]
    public void ValidatePayload_WithNestedRequiredFor_ShouldReceiveEventContext()
    {
        var evt = CreateEvent(new
        {
            payloadListThingy = new[]
            {
                new { name = "valid" }
            }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<RootPayload>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal("payloadListThingy[0].eventValue", Assert.Single(result.MemberNames));
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePayload_WithOmittedDefaultInitializedNestedObject_ShouldNotValidateIt()
    {
        var evt = CreateEvent(new { unrelatedValue = "partial update" });

        IEvent result = evt.ValidatePayload<RootPayload>();

        Assert.Same(evt, result);
    }

    private static Event CreateEvent(object payload)
        => new(RecursiveValidationEventType.Instance, Guid.NewGuid(), payload);

    private sealed class RootPayload
    {
        public ProfilePayload profile { get; set; } = new();

        public List<ListItemPayload>? payloadListThingy { get; set; }

        public string? unrelatedValue { get; set; }
    }

    private sealed class ProfilePayload
    {
        public AddressPayload address { get; set; } = new();
    }

    private sealed class AddressPayload
    {
        [RegularExpression(@"^\d{5}$", ErrorMessage = "Postal code must contain five digits")]
        public string? postalCode { get; set; }
    }

    private sealed class ListItemPayload
    {
        [Required(ErrorMessage = "Name is required")]
        public string? name { get; set; }

        [RequiredFor("RecursiveValidation_Test", ErrorMessage = "Event value is required")]
        public string? eventValue { get; set; }
    }

    private sealed class RecursiveValidationEventType : EventType
    {
        public static readonly RecursiveValidationEventType Instance = new();

        private RecursiveValidationEventType()
            : base("RecursiveValidation_Test")
        {
        }
    }
}
