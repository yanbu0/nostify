using System;
using System.Collections.Generic;
using System.Linq;
using nostify;
using Xunit;

namespace nostify.Tests;

public class GrpcEventRequesterTests
{
    #region Constructor Tests - Nullable Guid Selectors

    [Fact]
    public void Constructor_NullableGuidSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        var address = "https://localhost:5001";
        Func<FactoryTestProjection, Guid?> selector1 = p => p.externalId;
        Func<FactoryTestProjection, Guid?> selector2 = p => p.anotherExternalId;

        // Act
        var requester = new GrpcEventRequester<FactoryTestProjection>(address, selector1, selector2);

        // Assert
        Assert.Equal(address, requester.Address);
        Assert.Equal(2, requester.ForeignIdSelectors.Length);
        Assert.Equal(selector1, requester.ForeignIdSelectors[0]);
        Assert.Equal(selector2, requester.ForeignIdSelectors[1]);
        Assert.Equal(2, requester.SingleSelectors.Length);
        Assert.Empty(requester.ListSelectors);
    }

    [Fact]
    public void Constructor_NullableGuidSelectors_NoSelectors()
    {
        // Arrange & Act
        var requester = new GrpcEventRequester<FactoryTestProjection>("https://localhost:5001", Array.Empty<Func<FactoryTestProjection, Guid?>>());

        // Assert
        Assert.Equal("https://localhost:5001", requester.Address);
        Assert.Empty(requester.ForeignIdSelectors);
        Assert.Empty(requester.SingleSelectors);
    }

    [Fact]
    public void Constructor_NullableGuidSelectors_ThrowsOnNullAddress()
    {
        Assert.Throws<NostifyException>(() =>
            new GrpcEventRequester<FactoryTestProjection>(null!, p => p.externalId));
    }

    [Fact]
    public void Constructor_NullableGuidSelectors_ThrowsOnEmptyAddress()
    {
        Assert.Throws<NostifyException>(() =>
            new GrpcEventRequester<FactoryTestProjection>("", p => p.externalId));
    }

    #endregion

    #region Constructor Tests - Non-Nullable Guid Selectors

    [Fact]
    public void Constructor_NonNullableGuidSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        var address = "https://localhost:5001";
        Func<FactoryTestProjection, Guid> selector1 = p => p.siteId;
        Func<FactoryTestProjection, Guid> selector2 = p => p.ownerId;

        // Act
        var requester = new GrpcEventRequester<FactoryTestProjection>(address, selector1, selector2);

        // Assert
        Assert.Equal(address, requester.Address);
        Assert.Equal(2, requester.SingleSelectors.Length);
        Assert.Equal(2, requester.ForeignIdSelectors.Length);
        Assert.Empty(requester.ListSelectors);
    }

    [Fact]
    public void Constructor_NonNullableGuidSelectors_ConvertsToNullable()
    {
        // Arrange
        var testId = Guid.NewGuid();
        var projection = new FactoryTestProjection { siteId = testId };
        Func<FactoryTestProjection, Guid> selector = p => p.siteId;

        // Act
        var requester = new GrpcEventRequester<FactoryTestProjection>("https://localhost:5001", selector);

        // Assert
        var result = requester.SingleSelectors[0](projection);
        Assert.Equal(testId, result);
    }

    [Fact]
    public void Constructor_NonNullableGuidSelectors_ThrowsOnNullAddress()
    {
        Assert.Throws<NostifyException>(() =>
            new GrpcEventRequester<FactoryTestProjection>(null!, new Func<FactoryTestProjection, Guid>[] { p => p.siteId }));
    }

    #endregion

    #region Constructor Tests - Nullable Guid List Selectors

    [Fact]
    public void Constructor_NullableGuidListSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        Func<FactoryTestProjection, List<Guid?>> selector = p => p.tagIds.Cast<Guid?>().ToList();

        // Act
        var requester = new GrpcEventRequester<FactoryTestProjection>("https://localhost:5001", selector);

        // Assert
        Assert.Equal("https://localhost:5001", requester.Address);
        Assert.Single(requester.ListSelectors);
        Assert.Empty(requester.SingleSelectors);
        Assert.Empty(requester.ForeignIdSelectors);
    }

    #endregion

    #region Constructor Tests - Non-Nullable Guid List Selectors

    [Fact]
    public void Constructor_NonNullableGuidListSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        Func<FactoryTestProjection, List<Guid>> selector = p => p.tagIds;

        // Act
        var requester = new GrpcEventRequester<FactoryTestProjection>("https://localhost:5001", selector);

        // Assert
        Assert.Equal("https://localhost:5001", requester.Address);
        Assert.Single(requester.ListSelectors);
        Assert.Empty(requester.SingleSelectors);
        Assert.Empty(requester.ForeignIdSelectors);
    }

    [Fact]
    public void Constructor_NonNullableGuidListSelectors_ConvertsToNullable()
    {
        // Arrange
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var projection = new FactoryTestProjection { tagIds = ids };
        Func<FactoryTestProjection, List<Guid>> selector = p => p.tagIds;

        // Act
        var requester = new GrpcEventRequester<FactoryTestProjection>("https://localhost:5001", selector);

        // Assert
        var result = requester.ListSelectors[0](projection);
        Assert.Equal(2, result.Count);
        Assert.Equal(ids[0], result[0]);
        Assert.Equal(ids[1], result[1]);
    }

    #endregion

    #region Constructor Tests - Mixed Selectors (Nullable)

    [Fact]
    public void Constructor_MixedNullableSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        Func<FactoryTestProjection, Guid?>[] singleSelectors = { p => p.externalId };
        Func<FactoryTestProjection, List<Guid?>>[] listSelectors = { p => p.tagIds.Cast<Guid?>().ToList() };

        // Act
        var requester = new GrpcEventRequester<FactoryTestProjection>("https://localhost:5001", singleSelectors, listSelectors);

        // Assert
        Assert.Equal("https://localhost:5001", requester.Address);
        Assert.Single(requester.SingleSelectors);
        Assert.Single(requester.ListSelectors);
        Assert.Single(requester.ForeignIdSelectors);
    }

    #endregion

    #region Constructor Tests - Mixed Selectors (Non-Nullable)

    [Fact]
    public void Constructor_MixedNonNullableSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        Func<FactoryTestProjection, Guid>[] singleSelectors = { p => p.siteId };
        Func<FactoryTestProjection, List<Guid>>[] listSelectors = { p => p.tagIds };

        // Act
        var requester = new GrpcEventRequester<FactoryTestProjection>("https://localhost:5001", singleSelectors, listSelectors);

        // Assert
        Assert.Equal("https://localhost:5001", requester.Address);
        Assert.Single(requester.SingleSelectors);
        Assert.Single(requester.ListSelectors);
        Assert.Single(requester.ForeignIdSelectors);
    }

    [Fact]
    public void Constructor_MixedNonNullableSelectors_ThrowsOnNullAddress()
    {
        Assert.Throws<NostifyException>(() =>
            new GrpcEventRequester<FactoryTestProjection>(null!,
                new Func<FactoryTestProjection, Guid>[] { p => p.siteId },
                new Func<FactoryTestProjection, List<Guid>>[] { p => p.tagIds }));
    }

    #endregion

    #region GetAllForeignIdSelectors Tests

    [Fact]
    public void GetAllForeignIdSelectors_WithSingleSelectors_ReturnsSameSelectors()
    {
        // Arrange
        var requester = new GrpcEventRequester<FactoryTestProjection>(
            "https://localhost:5001",
            (Func<FactoryTestProjection, Guid?>)(p => p.externalId));
        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection { id = Guid.NewGuid(), externalId = Guid.NewGuid() }
        };

        // Act
        var allSelectors = requester.GetAllForeignIdSelectors(projections);

        // Assert
        Assert.Single(allSelectors);
    }

    [Fact]
    public void GetAllForeignIdSelectors_WithListSelectors_ExpandsCorrectly()
    {
        // Arrange
        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                tagIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }
            }
        };
        var requester = new GrpcEventRequester<FactoryTestProjection>(
            "https://localhost:5001",
            (Func<FactoryTestProjection, List<Guid>>)(p => p.tagIds));

        // Act
        var allSelectors = requester.GetAllForeignIdSelectors(projections);

        // Assert
        Assert.Equal(3, allSelectors.Length); // 3 items from the list
    }

    [Fact]
    public void GetAllForeignIdSelectors_MixedSelectors_CombinesSingleAndList()
    {
        // Arrange
        var projections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                siteId = Guid.NewGuid(),
                tagIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
            }
        };

        Func<FactoryTestProjection, Guid?>[] singleSelectors = { p => (Guid?)p.siteId };
        Func<FactoryTestProjection, List<Guid?>>[] listSelectors = { p => p.tagIds.Cast<Guid?>().ToList() };

        var requester = new GrpcEventRequester<FactoryTestProjection>(
            "https://localhost:5001", singleSelectors, listSelectors);

        // Act
        var allSelectors = requester.GetAllForeignIdSelectors(projections);

        // Assert
        Assert.Equal(3, allSelectors.Length); // 1 single + 2 from list
    }

    #endregion
}
