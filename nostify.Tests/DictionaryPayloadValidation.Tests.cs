using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json.Linq;

namespace nostify.Tests;

/// <summary>
/// Verifies recursive validation and cleaning for dictionary and open-ended payload values.
/// </summary>
public sealed class DictionaryPayloadValidationTests
{
    [Fact]
    public void ValidatePayload_WithMissingRequiredPropertyInDictionaryValue_ReturnsKeyQualifiedPath()
    {
        Event evt = CreateEvent(new
        {
            Values = new Dictionary<string, RequiredValue>
            {
                ["entry"] = new()
            }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<RequiredDictionaryPayload>());

        Assert.Equal("Values.entry.Name", Assert.Single(Assert.Single(exception.ValidationMessages).MemberNames));
    }

    [Fact]
    public void ValidatePayload_WithDisallowedPropertyInDictionaryValue_ReturnsKeyQualifiedPath()
    {
        Event evt = CreateEvent(
            new
            {
                Values = new Dictionary<string, OnlyValue>
                {
                    ["entry"] = new() { CreateOnly = "not allowed" }
                }
            },
            DeleteEventType.Instance);

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<OnlyDictionaryPayload>());

        Assert.Equal("Values.entry.CreateOnly", Assert.Single(Assert.Single(exception.ValidationMessages).MemberNames));
    }

    [Fact]
    public void ValidatePayload_WithPartialTogetherGroupInDictionaryValue_ReturnsKeyQualifiedPath()
    {
        Event evt = CreateEvent(new
        {
            Values = new Dictionary<string, object>
            {
                ["entry"] = new { Start = "present" }
            }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<TogetherDictionaryPayload>());

        Assert.Equal("Values.entry.End", Assert.Single(Assert.Single(exception.ValidationMessages).MemberNames));
    }

    [Theory]
    [InlineData(99)]
    [InlineData("NotAStatus")]
    public void ValidatePayload_WithInvalidEnumInDictionary_ReturnsStructuredError(object value)
    {
        Event evt = CreateEvent(new
        {
            Values = new Dictionary<string, object>
            {
                ["entry"] = value
            }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<EnumDictionaryPayload>());

        ValidationResult result = Assert.Single(exception.ValidationMessages);
        Assert.Equal("Values.entry", Assert.Single(result.MemberNames));
        Assert.Contains(nameof(Status), result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatePayload_WithInvalidNestedEnumInDictionary_ReturnsStructuredError()
    {
        Event evt = CreateEvent(new
        {
            Values = new Dictionary<string, object>
            {
                ["entry"] = new { Status = 99 }
            }
        });

        NostifyValidationException exception = Assert.Throws<NostifyValidationException>(
            () => evt.ValidatePayload<NestedEnumDictionaryPayload>());

        Assert.Equal("Values.entry.Status", Assert.Single(Assert.Single(exception.ValidationMessages).MemberNames));
    }

    [Fact]
    public void ValidatePayload_PreservesOpenEndedObjectAndJTokenContent()
    {
        JObject arbitraryObject = JObject.Parse("""{"x":1}""");
        Event evt = CreateEvent(new
        {
            Data = arbitraryObject,
            Token = JObject.Parse("""{"y":2}"""),
            Values = new Dictionary<string, object>
            {
                ["entry"] = JObject.Parse("""{"z":3}""")
            }
        });

        IEvent result = evt.ValidatePayload<OpenEndedPayload>();

        Assert.Same(evt, result);
        JObject payload = JObject.FromObject(evt.payload!);
        Assert.Equal(1, (int)payload["Data"]!["x"]!);
        Assert.Equal(2, (int)payload["Token"]!["y"]!);
        Assert.Equal(3, (int)payload["Values"]!["entry"]!["z"]!);
    }

    private static Event CreateEvent(object payload, EventType? eventType = null)
        => new(eventType ?? CreateEventType.Instance, Guid.NewGuid(), payload);

    private enum Status
    {
        Pending,
        Complete
    }

    private sealed class RequiredValue
    {
        [RequiredFor(typeof(CreateEventType))]
        public string? Name { get; set; }
    }

    private sealed class OnlyValue
    {
        [OnlyFor(typeof(CreateEventType))]
        public string? CreateOnly { get; set; }
    }

    private sealed class TogetherValue
    {
        [TogetherFor("pair", typeof(CreateEventType))]
        public string? Start { get; set; }

        [TogetherFor("pair", typeof(CreateEventType))]
        public string? End { get; set; }
    }

    private sealed class RequiredDictionaryPayload
    {
        public Dictionary<string, RequiredValue> Values { get; set; } = [];
    }

    private sealed class OnlyDictionaryPayload
    {
        public Dictionary<string, OnlyValue> Values { get; set; } = [];
    }

    private sealed class TogetherDictionaryPayload
    {
        public Dictionary<string, TogetherValue> Values { get; set; } = [];
    }

    private sealed class EnumDictionaryPayload
    {
        public Dictionary<string, Status> Values { get; set; } = [];
    }

    private sealed class EnumValue
    {
        public Status Status { get; set; }
    }

    private sealed class NestedEnumDictionaryPayload
    {
        public Dictionary<string, EnumValue> Values { get; set; } = [];
    }

    private sealed class OpenEndedPayload
    {
        public object? Data { get; set; }

        public JToken? Token { get; set; }

        public Dictionary<string, object> Values { get; set; } = [];
    }

    private sealed class CreateEventType : EventType
    {
        public static readonly CreateEventType Instance = new();

        private CreateEventType() : base("DictionaryValidation_Create")
        {
        }
    }

    private sealed class DeleteEventType : EventType
    {
        public static readonly DeleteEventType Instance = new();

        private DeleteEventType() : base("DictionaryValidation_Delete")
        {
        }
    }
}
