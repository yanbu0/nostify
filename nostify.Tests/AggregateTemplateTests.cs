namespace nostify.Tests;

/// <summary>Guards durable current-state initialization in aggregate-producing templates.</summary>
public class AggregateTemplateTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string> CurrentStateInitTemplates => new()
    {
        Path.Combine("templates", "nostifyAggregate", "_ReplaceMe_", "Admin", "_ReplaceMe_CurrentStateInit.cs"),
        Path.Combine("templates", "nostify", "_ReplaceMe_", "Aggregates", "_ReplaceMe_", "Admin", "_ReplaceMe_CurrentStateInit.cs")
    };

    [Theory]
    [MemberData(nameof(CurrentStateInitTemplates))]
    public void CurrentStateInitTemplate_UsesDurableInitializer(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));

        Assert.Contains("DurableCurrentStateInitializer<_ReplaceMe_>", source);
        Assert.Contains("[DurableClient] DurableTaskClient client", source);
        Assert.Contains("_initializer.StartOrchestration", source);
        Assert.Contains("_initializer.CancelOrchestration", source);
        Assert.Contains("_initializer.OrchestrateInitAsync", source);
        Assert.Contains("_initializer.DeleteAllCurrentState", source);
        Assert.Contains("_initializer.GetAggregateIds", source);
        Assert.Contains("_initializer.ProcessBatch", source);
        Assert.DoesNotContain("RebuildCurrentStateContainerAsync", source);
    }

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
