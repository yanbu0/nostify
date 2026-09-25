using Microsoft.Extensions.Logging;
using Moq;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace nostify.Tests;

public class NostifyValidationExceptionHandlerTests
{
    [Fact]
    public void HandleValidationException_WithNullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NostifyValidationExceptionHandler.HandleValidationException(null!));
    }

    [Fact]
    public void HandleValidationException_ReturnsStructuredResponseAndLogs()
    {
        // Enable warning logging so the source-generated logging delegate executes.
        var logger = CreateEnabledLogger();
        var exception = new NostifyValidationException(
        [
            new ValidationResult("Name is required", ["Name"]),
            new ValidationResult("Invalid object")
        ]);

        ValidationErrorResponse response =
            NostifyValidationExceptionHandler.HandleValidationException(exception, logger.Object);

        Assert.Equal("Validation failed", response.Message);
        Assert.Equal("Name is required", Assert.Single(response.Errors["Name"]));
        Assert.Equal(2, response.Details?.Length);
        Assert.Equal("Name is required", response.Details?[0].ErrorMessage);
        Assert.Equal(["Name"], response.Details?[0].MemberNames);
        Assert.Equal("Invalid object", response.Details?[1].ErrorMessage);
        Assert.Empty(response.Details?[1].MemberNames ?? []);
        VerifyWarningLogged(logger);
    }

    [Fact]
    public void HandleValidationResults_WithNoResults_ReturnsNoErrorsResponse()
    {
        ValidationErrorResponse nullResponse =
            NostifyValidationExceptionHandler.HandleValidationResults(null!);
        ValidationErrorResponse emptyResponse =
            NostifyValidationExceptionHandler.HandleValidationResults([]);

        Assert.Equal("No validation errors found", nullResponse.Message);
        Assert.Empty(nullResponse.Errors);
        Assert.Equal("No validation errors found", emptyResponse.Message);
        Assert.Empty(emptyResponse.Errors);
    }

    [Fact]
    public void HandleValidationResults_WithErrors_DelegatesToExceptionHandler()
    {
        var logger = CreateEnabledLogger();

        ValidationErrorResponse response = NostifyValidationExceptionHandler.HandleValidationResults(
            [new ValidationResult("Bad value", ["Value"])],
            logger.Object);

        Assert.Equal("Bad value", Assert.Single(response.Errors["Value"]));
        VerifyWarningLogged(logger);
    }

    [Fact]
    public void ToJson_SupportsIndentedAndCompactCamelCaseOutput()
    {
        var response = new ValidationErrorResponse
        {
            Message = "Validation failed",
            Errors = new Dictionary<string, List<string>>
            {
                ["Name"] = ["Name is required"]
            }
        };

        string indented = NostifyValidationExceptionHandler.ToJson(response);
        string compact = NostifyValidationExceptionHandler.ToJson(response, false);

        Assert.Contains(Environment.NewLine, indented, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine, compact, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(compact);
        Assert.Equal("Validation failed", document.RootElement.GetProperty("message").GetString());
        Assert.Equal(
            "Name is required",
            document.RootElement.GetProperty("errors").GetProperty("Name")[0].GetString());
    }

    [Fact]
    public void ToJson_WithNullResponse_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NostifyValidationExceptionHandler.ToJson(null!));
    }

    [Fact]
    public void CreateSimpleErrorMessage_HandlesMissingAndMixedMessages()
    {
        Assert.Equal(
            "No validation errors found",
            NostifyValidationExceptionHandler.CreateSimpleErrorMessage(null!));
        Assert.Equal(
            "No validation errors found",
            NostifyValidationExceptionHandler.CreateSimpleErrorMessage([]));

        string message = NostifyValidationExceptionHandler.CreateSimpleErrorMessage(
        [
            new ValidationResult("First"),
            new ValidationResult(null),
            new ValidationResult(string.Empty),
            new ValidationResult("Second")
        ]);

        Assert.Equal("First Second", message);
    }

    [Fact]
    public void ValidateObjectAndGetErrorResponse_WithNullObject_ReturnsObjectErrorAndLogs()
    {
        var logger = CreateEnabledLogger();

        ValidationErrorResponse? response =
            NostifyValidationExceptionHandler.ValidateObjectAndGetErrorResponse(null!, logger.Object);

        Assert.NotNull(response);
        Assert.Equal("Object cannot be null", Assert.Single(response.Errors["Object"]));
        VerifyWarningLogged(logger);
    }

    [Fact]
    public void ValidateObjectAndGetErrorResponse_ReturnsErrorsOnlyWhenInvalid()
    {
        var logger = CreateEnabledLogger();

        ValidationErrorResponse? invalidResponse =
            NostifyValidationExceptionHandler.ValidateObjectAndGetErrorResponse(
                new RequiredModel(),
                logger.Object);
        ValidationErrorResponse? validResponse =
            NostifyValidationExceptionHandler.ValidateObjectAndGetErrorResponse(
                new RequiredModel { Name = "valid" },
                logger.Object);

        Assert.NotNull(invalidResponse);
        Assert.Equal("Name is required", Assert.Single(invalidResponse.Errors["Name"]));
        Assert.Null(validResponse);
        VerifyWarningLogged(logger);
    }

    [Fact]
    public void ValidateObjectForCommand_WithNullObject_ReturnsObjectErrorAndLogs()
    {
        var logger = CreateEnabledLogger();

        ValidationErrorResponse? response =
            NostifyValidationExceptionHandler.ValidateObjectForCommandAndGetErrorResponse(
                null!,
                "Create",
                logger.Object);

        Assert.NotNull(response);
        Assert.Equal("Object cannot be null", Assert.Single(response.Errors["Object"]));
        VerifyWarningLogged(logger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateObjectForCommand_WithMissingCommand_ReturnsEventTypeError(string? commandName)
    {
        var logger = CreateEnabledLogger();

        ValidationErrorResponse? response =
            NostifyValidationExceptionHandler.ValidateObjectForCommandAndGetErrorResponse(
                new CommandRequiredModel(),
                commandName!,
                logger.Object);

        Assert.NotNull(response);
        Assert.Equal(
            "Event type name cannot be null or empty",
            Assert.Single(response.Errors["Event Type"]));
        VerifyWarningLogged(logger);
    }

    [Fact]
    public void ValidateObjectForCommand_AppliesOnlyMatchingRequiredForAttribute()
    {
        var logger = CreateEnabledLogger();
        var model = new CommandRequiredModel();

        ValidationErrorResponse? createResponse =
            NostifyValidationExceptionHandler.ValidateObjectForCommandAndGetErrorResponse(
                model,
                "Create",
                logger.Object);
        ValidationErrorResponse? updateResponse =
            NostifyValidationExceptionHandler.ValidateObjectForCommandAndGetErrorResponse(
                model,
                "Update",
                logger.Object);
        model.Name = "valid";
        ValidationErrorResponse? validCreateResponse =
            NostifyValidationExceptionHandler.ValidateObjectForCommandAndGetErrorResponse(
                model,
                "Create",
                logger.Object);

        Assert.NotNull(createResponse);
        Assert.Contains(
            "required for the event type 'Create'",
            Assert.Single(createResponse.Errors["Name"]),
            StringComparison.Ordinal);
        Assert.Null(updateResponse);
        Assert.Null(validCreateResponse);
        VerifyWarningLogged(logger);
    }

    private static Mock<ILogger> CreateEnabledLogger()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(candidate => candidate.IsEnabled(LogLevel.Warning)).Returns(true);
        return logger;
    }

    private static void VerifyWarningLogged(Mock<ILogger> logger)
    {
        logger.Verify(
            candidate => candidate.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private sealed class RequiredModel
    {
        [Required(ErrorMessage = "Name is required")]
        public string? Name { get; set; }
    }

    private sealed class CommandRequiredModel
    {
        [RequiredFor("Create")]
        public string? Name { get; set; }
    }
}
