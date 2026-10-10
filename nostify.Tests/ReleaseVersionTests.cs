using System.Xml.Linq;

namespace nostify.Tests;

/// <summary>
/// Prevents package and generated-template dependency versions from drifting between releases.
/// </summary>
public sealed class ReleaseVersionTests
{
    private const string ExpectedVersion = "5.2.2";
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string> TemplateProjectPaths => new()
    {
        Path.Combine("templates", "nostify", "_ReplaceMe_", "_ReplaceMe__Service.csproj"),
        Path.Combine("templates", "nostifyAggregate", "_ReplaceMe_.csproj"),
        Path.Combine("templates", "nostifyProjection", "_ProjectionName_.csproj"),
        Path.Combine("templates", "nostifyGrpc", "_GrpcServerName_", "_GrpcServerName_.csproj")
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
