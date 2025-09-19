using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nostify;
using Xunit;

namespace nostify.Tests;

public class TransformForeignIdSelectorsTests
{
    private readonly MethodInfo _transformMethod;

    public TransformForeignIdSelectorsTests()
    {
        // Get the private static method via reflection
        _transformMethod = typeof(ExternalDataEvent).GetMethod(
            "TransformForeignIdSelectors",
            BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException("TransformForeignIdSelectors method not found");
    }

    private Func<TProjection, Guid?>[] CallTransformForeignIdSelectors<TProjection>(
        List<TProjection> projectionsToInit,
        Func<TProjection, List<Guid?>>[] foreignIdSelectorsList)
        where TProjection : IUniquelyIdentifiable
    {
        var genericMethod = _transformMethod.MakeGenericMethod(typeof(TProjection));
        var result = genericMethod.Invoke(null, new object[] { projectionsToInit, foreignIdSelectorsList });
        return (result as Func<TProjection, Guid?>[]) ?? throw new InvalidOperationException("TransformForeignIdSelectors returned null or wrong type");
    }

    [Fact]
    public void TransformForeignIdSelectors_WithBasicData_ReturnsCorrectTransformation()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform { id = guid1, RelatedIds = new List<Guid?> { guid2, guid3 } },
            new TestProjectionForTransform { id = Guid.NewGuid(), RelatedIds = new List<Guid?> { guid1 } }
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Length); // Should have 3 individual selectors (guid1, guid2, guid3)

        // Test that each transformed selector returns the correct GUID for the correct projection
        var projection1 = projections[0];
        var projection2 = projections[1];

        // For projection1, selectors should return guid2 and guid3 (but not guid1)
        var projection1Results = result.Select(selector => selector(projection1)).Where(g => g.HasValue).ToList();
        Assert.Contains(guid2, projection1Results);
        Assert.Contains(guid3, projection1Results);
        Assert.DoesNotContain(guid1, projection1Results);

        // For projection2, selectors should return guid1 (but not guid2 or guid3)
        var projection2Results = result.Select(selector => selector(projection2)).Where(g => g.HasValue).ToList();
        Assert.Contains(guid1, projection2Results);
        Assert.DoesNotContain(guid2, projection2Results);
        Assert.DoesNotContain(guid3, projection2Results);
    }

    [Fact]
    public void TransformForeignIdSelectors_WithEmptyLists_ReturnsEmptyResult()
    {
        // Arrange
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform { id = Guid.NewGuid(), RelatedIds = new List<Guid?>() },
            new TestProjectionForTransform { id = Guid.NewGuid(), RelatedIds = new List<Guid?>() }
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // No GUIDs in any lists, so no selectors should be generated
    }

    [Fact]
    public void TransformForeignIdSelectors_WithNullValuesInLists_FiltersOutNulls()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform { id = Guid.NewGuid(), RelatedIds = new List<Guid?> { guid1, null, guid2, null } }
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        // The method creates a selector for every item in the list, including nulls
        // So we should have 4 selectors total (even though 2 are null)
        Assert.Equal(4, result.Length);

        var projection = projections[0];
        var nonNullResults = result.Select(selector => selector(projection)).Where(g => g.HasValue).ToList();
        
        // Only the non-null GUIDs should be returned when selectors are applied
        Assert.Contains(guid1, nonNullResults);
        Assert.Contains(guid2, nonNullResults);
        Assert.Equal(2, nonNullResults.Count);
    }

    [Fact]
    public void TransformForeignIdSelectors_WithDuplicateGuids_DeduplicatesCorrectly()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform { id = Guid.NewGuid(), RelatedIds = new List<Guid?> { guid1, guid2 } },
            new TestProjectionForTransform { id = Guid.NewGuid(), RelatedIds = new List<Guid?> { guid1, guid2 } } // Same GUIDs
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        // Should have 4 selectors total (2 projections × 2 GUIDs each), even though GUIDs are duplicated
        Assert.Equal(4, result.Length);

        // Each projection should get its own set of selectors
        var projection1 = projections[0];
        var projection2 = projections[1];

        var projection1Results = result.Select(selector => selector(projection1)).Where(g => g.HasValue).ToList();
        var projection2Results = result.Select(selector => selector(projection2)).Where(g => g.HasValue).ToList();

        // Both projections should return both GUIDs
        Assert.Contains(guid1, projection1Results);
        Assert.Contains(guid2, projection1Results);
        Assert.Contains(guid1, projection2Results);
        Assert.Contains(guid2, projection2Results);
    }

    [Fact]
    public void TransformForeignIdSelectors_WithMultipleSelectors_CombinesCorrectly()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        var guid4 = Guid.NewGuid();
        
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform 
            { 
                id = Guid.NewGuid(), 
                RelatedIds = new List<Guid?> { guid1, guid2 },
                DependentIds = new List<Guid?> { guid3, guid4 }
            }
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds,
            p => p.DependentIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.Length); // 2 from RelatedIds + 2 from DependentIds

        var projection = projections[0];
        var allResults = result.Select(selector => selector(projection)).Where(g => g.HasValue).ToList();

        Assert.Contains(guid1, allResults);
        Assert.Contains(guid2, allResults);
        Assert.Contains(guid3, allResults);
        Assert.Contains(guid4, allResults);
        Assert.Equal(4, allResults.Count);
    }

    [Fact]
    public void TransformForeignIdSelectors_WithProjectionHavingNoMatchingIds_ReturnsNullForThatProjection()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform { id = guid1, RelatedIds = new List<Guid?> { guid2 } },
            new TestProjectionForTransform { id = guid3, RelatedIds = new List<Guid?>() } // Empty list
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result); // Only one selector for guid2

        var projection1 = projections[0];
        var projection2 = projections[1];

        var projection1Results = result.Select(selector => selector(projection1)).Where(g => g.HasValue).ToList();
        var projection2Results = result.Select(selector => selector(projection2)).Where(g => g.HasValue).ToList();

        // First projection should return guid2
        Assert.Contains(guid2, projection1Results);
        
        // Second projection should return nothing (all selectors return null for it)
        Assert.Empty(projection2Results);
    }

    [Fact]
    public void TransformForeignIdSelectors_WithMixedListSizes_HandlesCorrectly()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        var guid4 = Guid.NewGuid();
        var guid5 = Guid.NewGuid();
        
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform { id = guid1, RelatedIds = new List<Guid?> { guid2 } }, // 1 item
            new TestProjectionForTransform { id = guid3, RelatedIds = new List<Guid?> { guid4, guid5 } } // 2 items
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Length); // 1 + 2 = 3 total selectors

        var projection1 = projections[0];
        var projection2 = projections[1];

        var projection1Results = result.Select(selector => selector(projection1)).Where(g => g.HasValue).ToList();
        var projection2Results = result.Select(selector => selector(projection2)).Where(g => g.HasValue).ToList();

        // First projection should return guid2 only
        Assert.Single(projection1Results);
        Assert.Contains(guid2, projection1Results);

        // Second projection should return guid4 and guid5
        Assert.Equal(2, projection2Results.Count);
        Assert.Contains(guid4, projection2Results);
        Assert.Contains(guid5, projection2Results);
    }

    [Fact]
    public void TransformForeignIdSelectors_WithNoProjections_ReturnsEmptyResult()
    {
        // Arrange
        var projections = new List<TestProjectionForTransform>();
        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void TransformForeignIdSelectors_WithNoSelectors_ReturnsEmptyResult()
    {
        // Arrange
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform { id = Guid.NewGuid(), RelatedIds = new List<Guid?> { Guid.NewGuid() } }
        };
        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[] { };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void TransformForeignIdSelectors_EnsuresCorrectProjectionToGuidMapping()
    {
        // Arrange - Test that each selector only returns a GUID for the correct projection
        var projectionId1 = Guid.NewGuid();
        var projectionId2 = Guid.NewGuid();
        var foreignGuid1 = Guid.NewGuid();
        var foreignGuid2 = Guid.NewGuid();
        
        var projections = new List<TestProjectionForTransform>
        {
            new TestProjectionForTransform { id = projectionId1, RelatedIds = new List<Guid?> { foreignGuid1 } },
            new TestProjectionForTransform { id = projectionId2, RelatedIds = new List<Guid?> { foreignGuid2 } }
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.Equal(2, result.Length);

        var projection1 = projections[0];
        var projection2 = projections[1];

        // Each selector should only return a GUID for one specific projection
        foreach (var selector in result)
        {
            var result1 = selector(projection1);
            var result2 = selector(projection2);

            // Exactly one of these should be non-null, but not both
            Assert.True((result1.HasValue && !result2.HasValue) || (!result1.HasValue && result2.HasValue));
        }

        // Verify the correct mappings
        var allResults1 = result.Select(s => s(projection1)).Where(g => g.HasValue).ToList();
        var allResults2 = result.Select(s => s(projection2)).Where(g => g.HasValue).ToList();

        Assert.Single(allResults1);
        Assert.Single(allResults2);
        Assert.Contains(foreignGuid1, allResults1);
        Assert.Contains(foreignGuid2, allResults2);
    }

    [Fact]
    public void TransformForeignIdSelectors_WithComplexScenario_HandlesAllEdgeCases()
    {
        // Arrange - Complex scenario with multiple edge cases combined
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        var guid4 = Guid.NewGuid();
        
        var projections = new List<TestProjectionForTransform>
        {
            // Projection with duplicates and nulls
            new TestProjectionForTransform 
            { 
                id = Guid.NewGuid(), 
                RelatedIds = new List<Guid?> { guid1, null, guid1, guid2 }, // Duplicates and nulls
                DependentIds = new List<Guid?> { guid3 }
            },
            // Projection with empty list
            new TestProjectionForTransform 
            { 
                id = Guid.NewGuid(), 
                RelatedIds = new List<Guid?>(), // Empty
                DependentIds = new List<Guid?> { guid4, null } // One valid, one null
            },
            // Projection with all nulls
            new TestProjectionForTransform 
            { 
                id = Guid.NewGuid(), 
                RelatedIds = new List<Guid?> { null, null }, // All nulls
                DependentIds = new List<Guid?>() // Empty
            }
        };

        var selectors = new Func<TestProjectionForTransform, List<Guid?>>[]
        {
            p => p.RelatedIds,
            p => p.DependentIds
        };

        // Act
        var result = CallTransformForeignIdSelectors(projections, selectors);

        // Assert
        Assert.NotNull(result);
        // Calculate expected count:
        // Projection 1: RelatedIds has 4 items + DependentIds has 1 item = 5 selectors
        // Projection 2: RelatedIds has 0 items + DependentIds has 2 items = 2 selectors  
        // Projection 3: RelatedIds has 2 items + DependentIds has 0 items = 2 selectors
        // Total: 5 + 2 + 2 = 9 selectors
        Assert.Equal(9, result.Length);

        // Verify each projection gets the correct GUIDs
        var projection1 = projections[0];
        var projection2 = projections[1];
        var projection3 = projections[2];

        var results1 = result.Select(s => s(projection1)).Where(g => g.HasValue).ToList();
        var results2 = result.Select(s => s(projection2)).Where(g => g.HasValue).ToList();
        var results3 = result.Select(s => s(projection3)).Where(g => g.HasValue).ToList();

        // First projection should get guid1 (twice), guid2, and guid3 = 4 non-null results
        Assert.Equal(4, results1.Count);
        Assert.Equal(2, results1.Count(g => g == guid1)); // guid1 appears twice
        Assert.Contains(guid2, results1);
        Assert.Contains(guid3, results1);

        // Second projection should get only guid4
        Assert.Single(results2);
        Assert.Contains(guid4, results2);

        // Third projection should get nothing (all nulls/empty)
        Assert.Empty(results3);
    }
}

// Test model for TransformForeignIdSelectors tests
public class TestProjectionForTransform : IUniquelyIdentifiable
{
    public Guid id { get; set; } = Guid.NewGuid();
    public List<Guid?> RelatedIds { get; set; } = new List<Guid?>();
    public List<Guid?> DependentIds { get; set; } = new List<Guid?>();
}