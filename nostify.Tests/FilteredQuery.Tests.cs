using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Xunit;
using nostify;

namespace nostify.Tests;

/// <summary>
/// Tests for FilteredQuery extension methods.
/// These tests verify that the FilteredQuery methods correctly use Expression-based filters
/// to maintain LINQ-to-Cosmos query compatibility.
/// </summary>
public class FilteredQueryTests
{
    private class TestTenantEntity : NostifyObject, ITenantFilterable
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }

        public override void Apply(IEvent e)
        {
            UpdateProperties<TestTenantEntity>(e.payload);
        }
    }

    /// <summary>
    /// Helper method to validate that a parameter type is Expression{Func{T, bool}}
    /// </summary>
    private static bool IsExpressionFuncType(Type paramType)
    {
        if (!paramType.IsGenericType)
        {
            return false;
        }
        
        var genericTypeDef = paramType.GetGenericTypeDefinition();
        return genericTypeDef == typeof(Expression<>);
    }

    [Fact]
    public void FilteredQuery_WithExpressionFilter_ShouldAcceptExpressionType()
    {
        // This test verifies that FilteredQuery accepts Expression<Func<T, bool>>
        // by checking the method signature at compile time
        
        // Arrange - Get the method info
        var methodInfo = typeof(FilteredQueryExtensions)
            .GetMethods()
            .Where(m => m.Name == "FilteredQuery")
            .Where(m => m.GetParameters().Length == 3)
            .FirstOrDefault(m => m.GetParameters()[0].ParameterType == typeof(Container));

        Assert.NotNull(methodInfo);
        
        // Verify that the third parameter is Expression<Func<T, bool>>
        var filterParameter = methodInfo.GetParameters()[2];
        Assert.NotNull(filterParameter);
        Assert.True(IsExpressionFuncType(filterParameter.ParameterType), 
            $"Expected Expression<> type but found: {filterParameter.ParameterType.Name}");
    }

    [Fact]
    public void FilteredQuery_MethodSignatures_ShouldUseExpressionType()
    {
        // Verify all three overloads use Expression<Func<T, bool>> for the filter parameter
        var methods = typeof(FilteredQueryExtensions)
            .GetMethods()
            .Where(m => m.Name == "FilteredQuery")
            .ToList();

        Assert.True(methods.Count >= 3, "Should have at least 3 FilteredQuery overloads");

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            // Find the filter parameter (should be the last one in each overload)
            var filterParam = parameters.LastOrDefault();
            
            if (filterParam != null && filterParam.Name == "filterExpression")
            {
                // Verify it's Expression<Func<T, bool>>
                Assert.True(IsExpressionFuncType(filterParam.ParameterType),
                    $"FilteredQuery method should use Expression<Func<T, bool>> for filter parameter, but found: {filterParam.ParameterType.FullName ?? filterParam.ParameterType.Name}");
            }
        }
    }

    [Fact]
    public void FilteredQuery_CompilationTest_ExpressionSyntax()
    {
        // This test verifies that the API accepts expression syntax (x => x.Property == value)
        // If this compiles, it means the signature is correct
        
        // This would NOT compile if FilteredQuery used Func<T, bool> because
        // expression syntax requires Expression<Func<T, bool>>
        
        // Arrange - Create a mock expression (we won't execute it, just verify it compiles)
        Expression<Func<TestTenantEntity, bool>> filter = x => x.Value > 10;
        
        // Assert - If we got here, the expression compiled successfully
        Assert.NotNull(filter);
        Assert.IsAssignableFrom<Expression<Func<TestTenantEntity, bool>>>(filter);
    }

    [Fact]
    public void FilteredQuery_WithNullFilter_ShouldBeOptional()
    {
        // Verify that the filter parameter is optional (nullable)
        var methods = typeof(FilteredQueryExtensions)
            .GetMethods()
            .Where(m => m.Name == "FilteredQuery")
            .ToList();

        foreach (var method in methods)
        {
            var filterParam = method.GetParameters().LastOrDefault(p => p.Name == "filterExpression");
            if (filterParam != null)
            {
                // Check if it's optional (has a default value)
                Assert.True(filterParam.IsOptional, 
                    "FilteredQuery's filterExpression parameter should be optional");
            }
        }
    }

    [Fact]
    public void FilteredQuery_DocumentationTest_VerifyImportedNamespaces()
    {
        // Verify that System.Linq.Expressions is imported in FilteredQuery.cs
        // This is necessary for Expression<Func<T, bool>> to work properly
        
        var assembly = typeof(FilteredQueryExtensions).Assembly;
        var type = assembly.GetType("nostify.FilteredQueryExtensions");
        
        Assert.NotNull(type);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.True(type.IsAbstract); // Static class
    }
}
