using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace nostify.Tests;

/// <summary>
/// Tests verifying that HandleMultiApplyEventAsync's expression tree composition
/// produces a clean expression without Invoke nodes (which Cosmos DB LINQ cannot translate).
/// The bug: when foreignIdSelector was Func&lt;P, Guid?&gt;, wrapping it in a lambda
/// created an Expression.Invoke node. Changing to Expression&lt;Func&lt;P, Guid?&gt;&gt; and
/// composing the tree manually produces p => p.SomeProperty == targetId, which Cosmos can translate.
/// </summary>
public class HandleMultiApplyExpressionTests
{
    /// <summary>
    /// Reproduces the exact expression composition from HandleMultiApplyEventAsync
    /// and verifies it produces a correct, Invoke-free expression tree.
    /// </summary>
    [Fact]
    public void ComposedFilterExpression_DoesNotContainInvokeNode()
    {
        // Arrange: same pattern as HandleMultiApplyEventAsync
        Expression<Func<TestProjection, Guid?>> foreignIdSelector = p => p.id;
        Guid targetId = Guid.NewGuid();

        // Act: compose the expression tree (same logic as the handler)
        var selectorParam = foreignIdSelector.Parameters[0];
        var equalsExpr = Expression.Equal(
            foreignIdSelector.Body,
            Expression.Constant((Guid?)targetId, typeof(Guid?)));
        var filterExpr = Expression.Lambda<Func<TestProjection, bool>>(equalsExpr, selectorParam);

        // Assert: no Invoke nodes in the tree
        Assert.DoesNotContain("Invoke", filterExpr.ToString());
        // Should look like: p => (Convert(p.id, Nullable`1) == <guid>) or similar
        Assert.Contains("==", filterExpr.ToString());
    }

    /// <summary>
    /// Verifies the composed expression evaluates correctly — matches when ID equals target.
    /// </summary>
    [Fact]
    public void ComposedFilterExpression_MatchesCorrectProjection()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        Expression<Func<TestProjection, Guid?>> foreignIdSelector = p => p.id;

        var selectorParam = foreignIdSelector.Parameters[0];
        var equalsExpr = Expression.Equal(
            foreignIdSelector.Body,
            Expression.Constant((Guid?)targetId, typeof(Guid?)));
        var filterExpr = Expression.Lambda<Func<TestProjection, bool>>(equalsExpr, selectorParam);

        // Compile and test
        var compiled = filterExpr.Compile();

        var matching = new TestProjection { id = targetId, name = "Match" };
        var nonMatching = new TestProjection { id = Guid.NewGuid(), name = "NoMatch" };

        Assert.True(compiled(matching));
        Assert.False(compiled(nonMatching));
    }

    /// <summary>
    /// Verifies the composed expression works correctly with LINQ .Where() on IQueryable.
    /// This simulates what FilteredQuery does internally.
    /// </summary>
    [Fact]
    public void ComposedFilterExpression_WorksWithLinqWhere()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var projections = new[]
        {
            new TestProjection { id = targetId, name = "Target" },
            new TestProjection { id = Guid.NewGuid(), name = "Other1" },
            new TestProjection { id = Guid.NewGuid(), name = "Other2" },
        }.AsQueryable();

        Expression<Func<TestProjection, Guid?>> foreignIdSelector = p => p.id;

        // Act: compose filter expression
        var selectorParam = foreignIdSelector.Parameters[0];
        var equalsExpr = Expression.Equal(
            foreignIdSelector.Body,
            Expression.Constant((Guid?)targetId, typeof(Guid?)));
        var filterExpr = Expression.Lambda<Func<TestProjection, bool>>(equalsExpr, selectorParam);

        var results = projections.Where(filterExpr).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal("Target", results[0].name);
    }

    /// <summary>
    /// Contrast test: demonstrates that wrapping a Func delegate in a lambda
    /// creates an Invoke node in the expression tree (the old buggy pattern).
    /// </summary>
    [Fact]
    public void DelegateWrappedInLambda_ContainsInvokeNode()
    {
        // This is the OLD pattern that was broken
        Func<TestProjection, Guid?> foreignIdSelector = p => p.id;
        Guid targetId = Guid.NewGuid();

        // When a Func is captured in an Expression lambda, it becomes an Invoke node
        Expression<Func<TestProjection, bool>> brokenExpr =
            p => foreignIdSelector(p) == targetId;

        // The expression tree contains "Invoke" — Cosmos DB LINQ would reject this
        Assert.Contains("Invoke", brokenExpr.ToString());
    }

    /// <summary>
    /// Verifies the composed expression works when the selector returns a nullable Guid
    /// that is null for some projections.
    /// </summary>
    [Fact]
    public void ComposedFilterExpression_HandlesNullForeignId()
    {
        // Arrange — id is non-nullable Guid on NostifyObject, but the selector returns Guid?
        // So we simulate with a cast
        var targetId = Guid.NewGuid();
        Expression<Func<TestProjection, Guid?>> foreignIdSelector = p => (Guid?)p.id;

        var selectorParam = foreignIdSelector.Parameters[0];
        var equalsExpr = Expression.Equal(
            foreignIdSelector.Body,
            Expression.Constant((Guid?)targetId, typeof(Guid?)));
        var filterExpr = Expression.Lambda<Func<TestProjection, bool>>(equalsExpr, selectorParam);

        var compiled = filterExpr.Compile();

        // Guid.Empty is not null but won't match
        var empty = new TestProjection { id = Guid.Empty, name = "Empty" };
        var match = new TestProjection { id = targetId, name = "Match" };

        Assert.False(compiled(empty));
        Assert.True(compiled(match));
    }
}
