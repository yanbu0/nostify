using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Nostify.Analyzers;

namespace nostify.Tests;

/// <summary>
/// Verifies compile-time diagnostics for typed event-validation attribute arguments.
/// </summary>
public sealed class EventValidationAttributeAnalyzerTests
{
    [Fact]
    public async Task Analyzer_WithNonEventType_ReportsError()
    {
        // Arrange and act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync("""
            using nostify;

            public sealed class Model
            {
                [RequiredFor(typeof(string))]
                public string? Value { get; set; }
            }
            """);

        // Assert
        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(EventValidationAttributeAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("string", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyzer_WithEventTypes_ReportsNoDiagnostics()
    {
        // Arrange and act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync("""
            using nostify;

            public sealed class CreateEvent : EventType
            {
                public CreateEvent() : base("Create") { }
            }

            public sealed class UpdateEvent : EventType
            {
                public UpdateEvent() : base("Update") { }
            }

            public sealed class Model
            {
                [RequiredFor(typeof(CreateEvent), typeof(UpdateEvent))]
                public string? Value { get; set; }
            }
            """);

        // Assert
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyzer_WithExplicitTypeArray_ReportsEachInvalidElement()
    {
        // Arrange and act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync("""
            using System;
            using nostify;

            public sealed class CreateEvent : EventType
            {
                public CreateEvent() : base("Create") { }
            }

            public sealed class Model
            {
                [RequiredFor(new Type[] { typeof(CreateEvent), typeof(string), typeof(Model) })]
                public string? Value { get; set; }
            }
            """);

        // Assert
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal(EventValidationAttributeAnalyzer.DiagnosticId, diagnostic.Id));
    }

    [Fact]
    public async Task Analyzer_WithStringOverload_ReportsNoDiagnostics()
    {
        // Arrange and act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync("""
            using nostify;

            public sealed class Model
            {
                [RequiredFor("Create", "Update")]
                public string? Value { get; set; }
            }
            """);

        // Assert
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyzer_WithOnlyForNonEventType_ReportsError()
    {
        // Arrange and act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync("""
            using nostify;

            public sealed class Model
            {
                [OnlyFor(typeof(string))]
                public string? Value { get; set; }
            }
            """);

        // Assert
        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(EventValidationAttributeAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains(nameof(OnlyForAttribute), diagnostic.GetMessage());
        Assert.Contains("string", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyzer_WithOnlyForEventTypes_ReportsNoDiagnostics()
    {
        // Arrange and act
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync("""
            using nostify;

            public sealed class CreateEvent : EventType
            {
                public CreateEvent() : base("Create") { }
            }

            public sealed class Model
            {
                [OnlyFor(typeof(CreateEvent))]
                public string? Value { get; set; }
            }
            """);

        // Assert
        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        MetadataReference[] references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(RequiredForAttribute).Assembly.Location))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "RequiredForAnalyzerTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new EventValidationAttributeAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
