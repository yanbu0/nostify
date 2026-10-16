using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nostify.Analyzers;

/// <summary>
/// Validates type arguments supplied to event-aware validation attributes at compile time.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventValidationAttributeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID reported when a validation attribute type argument is not an EventType.
    /// </summary>
    public const string DiagnosticId = "NOSTIFY_EVENTTYPE_CHECK";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Validation attribute type must derive from EventType",
        "Type '{0}' supplied to {1} must derive from nostify.EventType",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Typed event-validation attribute arguments must identify concrete nostify.EventType definitions.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(RegisterCompilationActions);
    }

    private static void RegisterCompilationActions(CompilationStartAnalysisContext context)
    {
        INamedTypeSymbol? requiredForAttribute = context.Compilation.GetTypeByMetadataName("nostify.RequiredForAttribute");
        INamedTypeSymbol? onlyForAttribute = context.Compilation.GetTypeByMetadataName("nostify.OnlyForAttribute");
        INamedTypeSymbol? togetherForAttribute = context.Compilation.GetTypeByMetadataName("nostify.TogetherForAttribute");
        INamedTypeSymbol? eventType = context.Compilation.GetTypeByMetadataName("nostify.EventType");
        INamedTypeSymbol? systemType = context.Compilation.GetTypeByMetadataName("System.Type");
        if ((requiredForAttribute is null && onlyForAttribute is null && togetherForAttribute is null) ||
            eventType is null ||
            systemType is null)
        {
            return;
        }

        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeAttribute(
                syntaxContext,
                requiredForAttribute,
                onlyForAttribute,
                togetherForAttribute,
                eventType,
                systemType),
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? requiredForAttribute,
        INamedTypeSymbol? onlyForAttribute,
        INamedTypeSymbol? togetherForAttribute,
        INamedTypeSymbol eventType,
        INamedTypeSymbol systemType)
    {
        var attribute = (AttributeSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol constructor ||
            (!SymbolEqualityComparer.Default.Equals(constructor.ContainingType, requiredForAttribute) &&
             !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, onlyForAttribute) &&
             !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, togetherForAttribute)) ||
            !constructor.Parameters.Any(parameter =>
                SymbolEqualityComparer.Default.Equals(parameter.Type, systemType) ||
                (parameter.Type is IArrayTypeSymbol arrayType &&
                 SymbolEqualityComparer.Default.Equals(arrayType.ElementType, systemType))) ||
            attribute.ArgumentList is null)
        {
            return;
        }

        // String arguments such as TogetherFor's group name are ignored by AnalyzeArgument;
        // every typeof expression in a typed overload is checked, including explicit arrays.
        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            AnalyzeArgument(context, argument, eventType, constructor.ContainingType.Name);
        }
    }

    private static void AnalyzeArgument(
        SyntaxNodeAnalysisContext context,
        AttributeArgumentSyntax argument,
        INamedTypeSymbol eventType,
        string attributeName)
    {
        if (argument.Expression is ArrayCreationExpressionSyntax arrayCreation && arrayCreation.Initializer is not null)
        {
            foreach (ExpressionSyntax expression in arrayCreation.Initializer.Expressions)
            {
                AnalyzeTypeExpression(context, expression, eventType, attributeName);
            }

            return;
        }

        if (argument.Expression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
        {
            foreach (ExpressionSyntax expression in implicitArrayCreation.Initializer.Expressions)
            {
                AnalyzeTypeExpression(context, expression, eventType, attributeName);
            }

            return;
        }

        AnalyzeTypeExpression(context, argument.Expression, eventType, attributeName);
    }

    private static void AnalyzeTypeExpression(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression,
        INamedTypeSymbol eventType,
        string attributeName)
    {
        if (expression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return;
        }

        ITypeSymbol? suppliedType = context.SemanticModel.GetTypeInfo(typeOfExpression.Type, context.CancellationToken).Type;
        if (suppliedType is null || InheritsFrom(suppliedType, eventType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            typeOfExpression.Type.GetLocation(),
            suppliedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            attributeName));
    }

    private static bool InheritsFrom(ITypeSymbol suppliedType, INamedTypeSymbol eventType)
    {
        for (ITypeSymbol? current = suppliedType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, eventType))
            {
                return true;
            }
        }

        return false;
    }
}
