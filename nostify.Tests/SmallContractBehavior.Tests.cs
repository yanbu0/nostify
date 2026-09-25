using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Covers small, deterministic result and serialization contracts.
/// </summary>
public sealed class SmallContractBehaviorTests
{
    private static readonly PartitionKey TestPartitionKey = new("tenant-1");

    [Fact]
    public void PatchItemResult_Success_ReportsOnlySuccessfulState()
    {
        PatchItemResult result = PatchItemResult.SuccessResult("item-1", TestPartitionKey);

        Assert.Equal("item-1", result.id);
        Assert.Equal(HttpStatusCode.OK, result.statusCode);
        Assert.Empty(result.exceptionMessage);
        Assert.True(result.PatchedSuccessfully);
        Assert.False(result.NotFound);
        Assert.False(result.IsException);
        Assert.Null(result.capturedDispatchInfo);
    }

    [Fact]
    public void PatchItemResult_NotFound_ReportsOnlyNotFoundState()
    {
        PatchItemResult result = PatchItemResult.NotFoundResult("item-1", TestPartitionKey);

        Assert.Equal(HttpStatusCode.NotFound, result.statusCode);
        Assert.False(result.PatchedSuccessfully);
        Assert.True(result.NotFound);
        Assert.False(result.IsException);
    }

    [Fact]
    public void PatchItemResult_RegularException_PreservesMessageWithoutDispatchInfo()
    {
        var exception = new InvalidOperationException("patch failed");

        PatchItemResult result = PatchItemResult.ExceptionResult("item-1", TestPartitionKey, exception);

        Assert.Equal(HttpStatusCode.InternalServerError, result.statusCode);
        Assert.Equal(exception.Message, result.exceptionMessage);
        Assert.True(result.IsException);
        Assert.Null(result.capturedDispatchInfo);
    }

    [Fact]
    public void PatchItemResult_CosmosException_PreservesOriginalException()
    {
        var exception = new CosmosException(
            "rate limited",
            HttpStatusCode.TooManyRequests,
            subStatusCode: 0,
            activityId: "activity-1",
            requestCharge: 1.5);

        PatchItemResult result = PatchItemResult.ExceptionResult("item-1", TestPartitionKey, exception);

        Assert.Equal(HttpStatusCode.TooManyRequests, result.statusCode);
        Assert.Contains("rate limited", result.exceptionMessage, StringComparison.Ordinal);
        Assert.True(result.IsException);
        Assert.NotNull(result.capturedDispatchInfo);
        Assert.Same(exception, result.capturedDispatchInfo.SourceException);
    }

    [Theory]
    [InlineData("", "tenant-1", "id cannot be null or empty")]
    [InlineData("item-1", "", "partitionId cannot be null or empty")]
    public void PatchItemResult_InvalidOperation_ReportsMissingIdentifiers(
        string id,
        string partitionId,
        string expectedMessage)
    {
        PatchItemResult result = PatchItemResult.InvalidOperationResult(
            (id, partitionId, [PatchOperation.Set("/name", "updated")]));

        Assert.Equal(HttpStatusCode.BadRequest, result.statusCode);
        Assert.Equal(expectedMessage, result.exceptionMessage);
        Assert.True(result.IsException);
    }

    [Fact]
    public void PatchItemResult_InvalidOperation_ReportsMissingOperations()
    {
        PatchItemResult result = PatchItemResult.InvalidOperationResult(
            ("item-1", "tenant-1", []));

        Assert.Equal("No patch operations to perform", result.exceptionMessage);
    }

    [Fact]
    public void PatchItemResult_InvalidOperation_WithInputs_ReportsUnknownReason()
    {
        PatchItemResult result = PatchItemResult.InvalidOperationResult(
            ("item-1", "tenant-1", [PatchOperation.Set("/name", "updated")]));

        Assert.Equal("Invalid patch operation (reason unknown)", result.exceptionMessage);
    }

    [Fact]
    public void SagaStepListConverter_EmptyArray_RoundTrips()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ListISagaStepJsonConverter());
        List<ISagaStep> steps = [];

        string json = JsonSerializer.Serialize(steps, options);
        List<ISagaStep>? deserialized = JsonSerializer.Deserialize<List<ISagaStep>>(json, options);

        Assert.Equal("[]", json);
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized);
    }

    [Fact]
    public void SagaStepListConverter_NonSagaStep_ThrowsClearJsonException()
    {
        var converter = new ListISagaStepJsonConverter();
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        JsonException exception = Assert.Throws<JsonException>(
            () => converter.Write(writer, [new AlternateSagaStep()], new JsonSerializerOptions()));

        Assert.Contains("must be of type SagaStep", exception.Message, StringComparison.Ordinal);
    }

    private sealed class AlternateSagaStep : ISagaStep
    {
        public int order { get; set; }
        public IEvent stepEvent { get; set; } = new Event();
        public IEvent? rollbackEvent { get; set; }
        public SagaStepStatus status { get; set; }
        public object? successData { get; set; }
        public object? rollbackData { get; set; }
        public DateTime? executionStart { get; set; }
        public DateTime? executionComplete { get; set; }
        public DateTime? rollbackStart { get; set; }
        public DateTime? rollbackComplete { get; set; }
        public Guid aggregateRootId => stepEvent.aggregateRootId;

        public Task StartAsync(INostify nostify) => Task.CompletedTask;

        public Task RollbackAsync(INostify nostify) => Task.CompletedTask;

        public void Complete(object? successData = null)
        {
            this.successData = successData;
        }

        public void CompleteRollback(object? rollbackData = null)
        {
            this.rollbackData = rollbackData;
        }
    }
}
