using System.Xml.Linq;

namespace nostify.Tests;

/// <summary>
/// Prevents package and generated-template dependency versions from drifting between releases.
/// </summary>
public sealed class ReleaseVersionTests
{
    private const string ExpectedVersion = "5.5.0";
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string> TemplateProjectPaths => new()
    {
        Path.Combine("templates", "nostify", "_ReplaceMe_", "_ReplaceMe__Service.csproj"),
        Path.Combine("templates", "nostifyAggregate", "_ReplaceMe_.csproj"),
        Path.Combine("templates", "nostifyProjection", "_ProjectionName_.csproj"),
        Path.Combine("templates", "nostifyGrpc", "_GrpcServerName_", "_GrpcServerName_.csproj")
    };

    public static TheoryData<string, string> BulkCommandTemplatePaths => new()
    {
        { Path.Combine("templates", "nostify", "_ReplaceMe_", "Aggregates", "_ReplaceMe_", "CommandHandlers", "BulkCreate_ReplaceMe_.cs"), "GetBulkCreateResultsAsync" },
        { Path.Combine("templates", "nostify", "_ReplaceMe_", "Aggregates", "_ReplaceMe_", "CommandHandlers", "BulkUpdate_ReplaceMe_.cs"), "GetBulkUpdateResultsAsync" },
        { Path.Combine("templates", "nostify", "_ReplaceMe_", "Aggregates", "_ReplaceMe_", "CommandHandlers", "BulkDelete_ReplaceMe_.cs"), "GetBulkDeleteResultsAsync" },
        { Path.Combine("templates", "nostifyAggregate", "_ReplaceMe_", "CommandHandlers", "BulkCreate_ReplaceMe_.cs"), "GetBulkCreateResultsAsync" },
        { Path.Combine("templates", "nostifyAggregate", "_ReplaceMe_", "CommandHandlers", "BulkUpdate_ReplaceMe_.cs"), "GetBulkUpdateResultsAsync" },
        { Path.Combine("templates", "nostifyAggregate", "_ReplaceMe_", "CommandHandlers", "BulkDelete_ReplaceMe_.cs"), "GetBulkDeleteResultsAsync" }
    };

    [Fact]
    public void PackageProject_UsesReleaseVersion()
    {
        // Arrange
        XDocument project = LoadProject("nostify.csproj");

        // Act
        string? version = project.Root?
            .Elements("PropertyGroup")
            .Elements("Version")
            .Select(element => element.Value)
            .SingleOrDefault();

        // Assert
        Assert.Equal(ExpectedVersion, version);
    }

    [Theory]
    [MemberData(nameof(TemplateProjectPaths))]
    public void TemplateProject_ReferencesReleaseVersion(string relativePath)
    {
        // Arrange
        XDocument project = LoadProject(relativePath);

        // Act
        XElement packageReference = Assert.Single(
            project.Descendants("PackageReference"),
            element => string.Equals(
                (string?)element.Attribute("Include"),
                "nostify",
                StringComparison.OrdinalIgnoreCase));

        // Assert
        Assert.Equal(ExpectedVersion, (string?)packageReference.Attribute("Version"));
    }

    [Fact]
    public void PackageProject_ExcludesGeneratedTemplateArtifacts()
    {
        // Arrange
        XDocument project = LoadProject("nostify.csproj");
        string generatedArtifacts = @"templates\**\bin\**;templates\**\obj\**";

        // Act
        XElement contentRemoval = Assert.Single(
            project.Descendants("Content"),
            element => string.Equals((string?)element.Attribute("Remove"), generatedArtifacts, StringComparison.Ordinal));
        XElement noneRemoval = Assert.Single(
            project.Descendants("None"),
            element => string.Equals((string?)element.Attribute("Remove"), generatedArtifacts, StringComparison.Ordinal));

        // Assert
        Assert.NotNull(contentRemoval);
        Assert.NotNull(noneRemoval);
    }

    [Fact]
    public void GitIgnore_ExcludesGeneratedTemplateArtifacts()
    {
        // Arrange and act
        string gitIgnore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));

        // Assert
        Assert.Contains("/templates/**/[Bb]in/", gitIgnore, StringComparison.Ordinal);
        Assert.Contains("/templates/**/[Oo]bj/", gitIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_ContainsCurrentReleaseNotes()
    {
        // Arrange and act
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));

        // Assert
        Assert.Contains($"- {ExpectedVersion}", readme, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BulkCommandTemplatePaths))]
    public void BulkCommandTemplate_ReturnsDetailedResults(string relativePath, string expectedMethod)
    {
        string template = File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));

        Assert.Contains("Task<IReadOnlyList<BulkPersistEventResult>> Run(", template, StringComparison.Ordinal);
        Assert.Contains($"DefaultCommandHandler.{expectedMethod}", template, StringComparison.Ordinal);
        Assert.DoesNotContain("CountBulk", template, StringComparison.Ordinal);
    }

    private static XDocument LoadProject(string relativePath)
        => XDocument.Load(Path.Combine(RepositoryRoot, relativePath));

    /// <summary>Walks upward from the test output until the package project is found.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "nostify.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the nostify repository root.");
    }
}
