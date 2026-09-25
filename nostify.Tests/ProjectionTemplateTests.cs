using System.Xml.Linq;

namespace nostify.Tests;

/// <summary>
/// Guards the projection template's durable initialization wiring. These tests read the
/// packaged template sources directly because placeholder identifiers are intentionally
/// not compilable inside the test assembly.
/// </summary>
public class ProjectionTemplateTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void InitTemplate_UsesDurableProjectionInitializer()
    {
        var source = File.ReadAllText(TemplatePath("_ProjectionName_", "Admin", "_ProjectionName_Init.cs"));

        Assert.Contains("DurableProjectionInitializer<_ProjectionName_, _ReplaceMe_>", source);
        Assert.Contains("[DurableClient] DurableTaskClient client", source);
        Assert.Contains("_initializer.StartOrchestration", source);
        Assert.Contains("_initializer.CancelOrchestration", source);
        Assert.Contains("_initializer.OrchestrateInitAsync", source);
        Assert.Contains("_initializer.DeleteAllProjections", source);
        Assert.Contains("_initializer.GetDistinctTenantIds", source);
        Assert.Contains("_initializer.GetIdsForTenant", source);
        Assert.Contains("_initializer.ProcessBatch", source);
        Assert.DoesNotContain("InitContainerAsync", source);
    }

    [Fact]
    public void AdminDirectory_HasOnlyOneProjectionInitTemplate()
    {
        var adminDirectory = TemplatePath("_ProjectionName_", "Admin");
        var initTemplates = Directory.GetFiles(adminDirectory, "*Init.cs", SearchOption.TopDirectoryOnly);

        var initTemplate = Assert.Single(initTemplates);
        Assert.Equal("_ProjectionName_Init.cs", Path.GetFileName(initTemplate));
    }

    [Fact]
    public void ProjectionTemplate_DependsOnNostifyPackageThatProvidesDurableTypes()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot, "templates", "nostifyProjection", "_ProjectionName_.csproj"));
        var packageReferences = project.Descendants("PackageReference").ToList();

        Assert.Contains(packageReferences, element =>
            string.Equals((string?)element.Attribute("Include"), "nostify", StringComparison.OrdinalIgnoreCase));
    }

    private static string TemplatePath(params string[] segments)
        => Path.Combine(new[] { RepositoryRoot, "templates", "nostifyProjection" }.Concat(segments).ToArray());

    /// <summary>Walks upward from the test output until the repository project is found.</summary>
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
