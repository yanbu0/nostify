using System;
using System.Collections.Generic;
using System.Linq;
using nostify;
using Xunit;

namespace nostify.Tests;

public class AsyncEventRequesterTests
{
    #region Constructor Tests - Nullable Guid Selectors

    [Fact]
    public void Constructor_NullableGuidSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        var serviceName = "InventoryService";
        Func<FactoryTestProjection, Guid?> selector1 = p => p.externalId;
        Func<FactoryTestProjection, Guid?> selector2 = p => p.anotherExternalId;

        // Act
        var requester = new AsyncEventRequester<FactoryTestProjection>(serviceName, selector1, selector2);

        // Assert
        Assert.Equal(serviceName, requester.ServiceName);
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
        var requester = new AsyncEventRequester<FactoryTestProjection>("TestService", Array.Empty<Func<FactoryTestProjection, Guid?>>());

        // Assert
        Assert.Equal("TestService", requester.ServiceName);
        Assert.Empty(requester.ForeignIdSelectors);
        Assert.Empty(requester.SingleSelectors);
    }

    #endregion

    #region Constructor Tests - Non-Nullable Guid Selectors

    [Fact]
    public void Constructor_NonNullableGuidSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        var serviceName = "SiteService";
        Func<FactoryTestProjection, Guid> selector1 = p => p.siteId;
        Func<FactoryTestProjection, Guid> selector2 = p => p.ownerId;

        // Act
        var requester = new AsyncEventRequester<FactoryTestProjection>(serviceName, selector1, selector2);

        // Assert
        Assert.Equal(serviceName, requester.ServiceName);
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
        var requester = new AsyncEventRequester<FactoryTestProjection>("TestService", selector);

        // Assert - SingleSelectors should return the same Guid wrapped as Guid?
        var result = requester.SingleSelectors[0](projection);
        Assert.Equal(testId, result);
    }

    #endregion

    #region Constructor Tests - Nullable Guid List Selectors

    [Fact]
    public void Constructor_NullableGuidListSelectors_SetsPropertiesCorrectly()
    {
        // Arrange
        Func<FactoryTestProjection, List<Guid?>> selector = p => p.tagIds.Cast<Guid?>().ToList();

        // Act
        var requester = new AsyncEventRequester<FactoryTestProjection>("TagService", selector);

        // Assert
        Assert.Equal("TagService", requester.ServiceName);
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
        var requester = new AsyncEventRequester<FactoryTestProjection>("TagService", selector);

        // Assert
        Assert.Equal("TagService", requester.ServiceName);
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
        var requester = new AsyncEventRequester<FactoryTestProjection>("TagService", selector);

        // Assert - ListSelectors should return Guid? list
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
        var requester = new AsyncEventRequester<FactoryTestProjection>("MixedService", singleSelectors, listSelectors);

        // Assert
        Assert.Equal("MixedService", requester.ServiceName);
        Assert.Single(requester.SingleSelectors);
        Assert.Single(requester.ListSelectors);
        Assert.Single(requester.ForeignIdSelectors); // Only single selectors go to ForeignIdSelectors
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
        var requester = new AsyncEventRequester<FactoryTestProjection>("MixedService", singleSelectors, listSelectors);

        // Assert
        Assert.Equal("MixedService", requester.ServiceName);
        Assert.Single(requester.SingleSelectors);
        Assert.Single(requester.ListSelectors);
        Assert.Single(requester.ForeignIdSelectors);
    }

    #endregion

    #region TopicName Tests

    [Fact]
    public void TopicName_ReturnsServiceNameWithSuffix()
    {
        // Arrange
        var serviceName = "InventoryService";
        var requester = new AsyncEventRequester<FactoryTestProjection>(serviceName, Array.Empty<Func<FactoryTestProjection, Guid?>>());

        // Act & Assert
        Assert.Equal("InventoryService_EventRequest", requester.TopicName);
    }

    [Fact]
    public void TopicName_DifferentServiceNames()
    {
        // Arrange & Act
        var requester1 = new AsyncEventRequester<FactoryTestProjection>("ServiceA", Array.Empty<Func<FactoryTestProjection, Guid?>>());
        var requester2 = new AsyncEventRequester<FactoryTestProjection>("ServiceB", Array.Empty<Func<FactoryTestProjection, Guid?>>());

        // Assert
        Assert.Equal("ServiceA_EventRequest", requester1.TopicName);
        Assert.Equal("ServiceB_EventRequest", requester2.TopicName);
    }

    [Fact]
    public void ResponseTopicName_ReturnsServiceNameWithResponseSuffix()
    {
        // Arrange
        var requester = new AsyncEventRequester<FactoryTestProjection>("InventoryService", Array.Empty<Func<FactoryTestProjection, Guid?>>());

        // Act & Assert
        Assert.Equal("InventoryService_EventRequestResponse", requester.ResponseTopicName);
    }

    [Fact]
    public void ResponseTopicName_DifferentServiceNames()
    {
        // Arrange & Act
        var requester1 = new AsyncEventRequester<FactoryTestProjection>("ServiceA", Array.Empty<Func<FactoryTestProjection, Guid?>>());
        var requester2 = new AsyncEventRequester<FactoryTestProjection>("ServiceB", Array.Empty<Func<FactoryTestProjection, Guid?>>());

        // Assert
        Assert.Equal("ServiceA_EventRequestResponse", requester1.ResponseTopicName);
        Assert.Equal("ServiceB_EventRequestResponse", requester2.ResponseTopicName);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Constructor_NullServiceName_ThrowsNostifyException()
    {
        // Arrange & Act & Assert
        Assert.Throws<NostifyException>(() =>
            new AsyncEventRequester<FactoryTestProjection>(null!, Array.Empty<Func<FactoryTestProjection, Guid?>>()));
    }

    [Fact]
    public void Constructor_EmptyServiceName_ThrowsNostifyException()
    {
        // Arrange & Act & Assert
        Assert.Throws<NostifyException>(() =>
            new AsyncEventRequester<FactoryTestProjection>("", Array.Empty<Func<FactoryTestProjection, Guid?>>()));
    }

    [Fact]
    public void Constructor_NonNullableOverload_NullServiceName_ThrowsNostifyException()
    {
        Assert.Throws<NostifyException>(() =>
            new AsyncEventRequester<FactoryTestProjection>(null!, Array.Empty<Func<FactoryTestProjection, Guid>>()));
    }

    [Fact]
    public void Constructor_ListNullableOverload_EmptyServiceName_ThrowsNostifyException()
    {
        Assert.Throws<NostifyException>(() =>
            new AsyncEventRequester<FactoryTestProjection>("", Array.Empty<Func<FactoryTestProjection, List<Guid?>>>()));
    }

    [Fact]
    public void Constructor_ListNonNullableOverload_NullServiceName_ThrowsNostifyException()
    {
        Assert.Throws<NostifyException>(() =>
            new AsyncEventRequester<FactoryTestProjection>(null!, Array.Empty<Func<FactoryTestProjection, List<Guid>>>()));
    }

    [Fact]
    public void Constructor_MixedNullableOverload_EmptyServiceName_ThrowsNostifyException()
    {
        Assert.Throws<NostifyException>(() =>
            new AsyncEventRequester<FactoryTestProjection>("",
                Array.Empty<Func<FactoryTestProjection, Guid?>>(),
                Array.Empty<Func<FactoryTestProjection, List<Guid?>>>()));
    }

    [Fact]
    public void Constructor_MixedNonNullableOverload_NullServiceName_ThrowsNostifyException()
    {
        Assert.Throws<NostifyException>(() =>
            new AsyncEventRequester<FactoryTestProjection>(null!,
                Array.Empty<Func<FactoryTestProjection, Guid>>(),
                Array.Empty<Func<FactoryTestProjection, List<Guid>>>()));
    }

    #endregion

    #region GetAllForeignIdSelectors Tests

    [Fact]
    public void GetAllForeignIdSelectors_WithOnlySingleSelectors_ReturnsSingleSelectors()
    {
        // Arrange
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            externalId = Guid.NewGuid(),
            anotherExternalId = Guid.NewGuid()
        };
        var projections = new List<FactoryTestProjection> { projection };

        var requester = new AsyncEventRequester<FactoryTestProjection>(
            "TestService",
            (Func<FactoryTestProjection, Guid?>)(p => p.externalId),
            (Func<FactoryTestProjection, Guid?>)(p => p.anotherExternalId));

        // Act
        var allSelectors = requester.GetAllForeignIdSelectors(projections);

        // Assert
        Assert.Equal(2, allSelectors.Length);
        Assert.Equal(projection.externalId, allSelectors[0](projection));
        Assert.Equal(projection.anotherExternalId, allSelectors[1](projection));
    }

    [Fact]
    public void GetAllForeignIdSelectors_WithListSelectors_ExpandsLists()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var projection = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            tagIds = new List<Guid> { id1, id2 }
        };
        var projections = new List<FactoryTestProjection> { projection };

        Func<FactoryTestProjection, Guid?>[] singleSelectors = { p => p.externalId };
        Func<FactoryTestProjection, List<Guid?>>[] listSelectors = { p => p.tagIds.Cast<Guid?>().ToList() };
        var requester = new AsyncEventRequester<FactoryTestProjection>("TestService", singleSelectors, listSelectors);

        // Act
        var allSelectors = requester.GetAllForeignIdSelectors(projections);

        // Assert - 1 single + 2 expanded from list = 3 total
        Assert.Equal(3, allSelectors.Length);
    }

    [Fact]
    public void GetAllForeignIdSelectors_WithNoSelectors_ReturnsEmpty()
    {
        // Arrange
        var requester = new AsyncEventRequester<FactoryTestProjection>(
            "TestService",
            Array.Empty<Func<FactoryTestProjection, Guid?>>());
        var projections = new List<FactoryTestProjection>();

        // Act
        var allSelectors = requester.GetAllForeignIdSelectors(projections);

        // Assert
        Assert.Empty(allSelectors);
    }

    [Fact]
    public void GetAllForeignIdSelectors_WithMultipleProjections_ExpandsAllLists()
    {
        // Arrange
        var projection1 = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            tagIds = new List<Guid> { Guid.NewGuid() }
        };
        var projection2 = new FactoryTestProjection
        {
            id = Guid.NewGuid(),
            tagIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
        };
        var projections = new List<FactoryTestProjection> { projection1, projection2 };

        Func<FactoryTestProjection, List<Guid?>>[] listSelectors = { p => p.tagIds.Cast<Guid?>().ToList() };
        var requester = new AsyncEventRequester<FactoryTestProjection>("TestService",
            Array.Empty<Func<FactoryTestProjection, Guid?>>(),
            listSelectors);

        // Act
        var allSelectors = requester.GetAllForeignIdSelectors(projections);

        // Assert - projection1 has 1 tag + projection2 has 2 tags = 3 total expanded
        Assert.Equal(3, allSelectors.Length);
    }

    #endregion
}
