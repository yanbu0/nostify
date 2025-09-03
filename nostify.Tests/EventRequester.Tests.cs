using System;
using System.Collections.Generic;
using System.Linq;
using nostify;
using Xunit;

namespace nostify.Tests;

public class EventRequesterTests
{
    [Fact]
    public void EventRequester_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid?> selector1 = p => p.siteId;
        Func<TestProjectionForExternalData, Guid?> selector2 = p => p.ownerId;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector1, selector2);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(2, eventRequest.ForeignIdSelectors.Length);
        Assert.Equal(selector1, eventRequest.ForeignIdSelectors[0]);
        Assert.Equal(selector2, eventRequest.ForeignIdSelectors[1]);
    }

    [Fact]
    public void EventRequester_Constructor_ThrowsExceptionForNullUrl()
    {
        // Arrange & Act & Assert
        Assert.Throws<NostifyException>(() => new EventRequester<TestProjectionForExternalData>(null!, Array.Empty<Func<TestProjectionForExternalData, Guid?>>()));
    }

    [Fact]
    public void EventRequester_Constructor_ThrowsExceptionForEmptyUrl()
    {
        // Arrange & Act & Assert
        Assert.Throws<NostifyException>(() => new EventRequester<TestProjectionForExternalData>("", Array.Empty<Func<TestProjectionForExternalData, Guid?>>()));
    }

    [Fact]
    public void EventRequester_Constructor_HandlesNoSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, Array.Empty<Func<TestProjectionForExternalData, Guid?>>());

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.ForeignIdSelectors);
    }

    [Fact]
    public void EventRequester_Constructor_HandlesMultipleForeignIdSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid?> selector1 = p => p.siteId;
        Func<TestProjectionForExternalData, Guid?> selector2 = p => p.ownerId;
        Func<TestProjectionForExternalData, Guid?> selector3 = p => p.id;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector1, selector2, selector3);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(3, eventRequest.ForeignIdSelectors.Length);
        Assert.Equal(selector1, eventRequest.ForeignIdSelectors[0]);
        Assert.Equal(selector2, eventRequest.ForeignIdSelectors[1]);
        Assert.Equal(selector3, eventRequest.ForeignIdSelectors[2]);
    }

    [Fact]
    public void EventRequester_MixedConstructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid?>[] singleSelectors = {
            p => p.siteId,
            p => p.ownerId
        };
        Func<TestProjectionForExternalData, List<Guid?>>[] listSelectors = {
            p => new List<Guid?> { p.id, Guid.NewGuid() }
        };

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, singleSelectors, listSelectors);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(2, eventRequest.SingleSelectors.Length); // Two single selectors
        Assert.Single(eventRequest.ListSelectors);   // One list selector
        Assert.Equal(2, eventRequest.ForeignIdSelectors.Length); // Should be single selectors only initially
    }

    [Fact]
    public void EventRequester_MixedConstructor_GetAllForeignIdSelectors_ExpandsCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        var testProjections = new List<TestProjectionForExternalData>
        {
            new TestProjectionForExternalData { id = Guid.NewGuid(), siteId = Guid.NewGuid(), ownerId = Guid.NewGuid() }
        };
        
        Func<TestProjectionForExternalData, Guid?>[] singleSelectors = {
            p => p.siteId
        };
        Func<TestProjectionForExternalData, List<Guid?>>[] listSelectors = {
            p => new List<Guid?> { p.id, p.ownerId }
        };

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, singleSelectors, listSelectors);
        var allSelectors = eventRequest.GetAllForeignIdSelectors(testProjections);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(3, allSelectors.Length); // 1 single selector + 2 from list selector expansion
    }

    [Fact]
    public void EventRequester_MixedConstructor_ThrowsExceptionForNullUrl()
    {
        // Arrange
        Func<TestProjectionForExternalData, Guid?>[] singleSelectors = { p => p.siteId };
        Func<TestProjectionForExternalData, List<Guid?>>[] listSelectors = { p => new List<Guid?> { p.id } };

        // Act & Assert
        Assert.Throws<NostifyException>(() => 
            new EventRequester<TestProjectionForExternalData>(null!, singleSelectors, listSelectors));
    }

    [Fact]
    public void EventRequester_ListConstructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, List<Guid?>>[] listSelectors = {
            p => new List<Guid?> { p.id, p.siteId },
            p => new List<Guid?> { p.ownerId }
        };

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, listSelectors);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors); // Should be empty for list-only constructor
        Assert.Equal(2, eventRequest.ListSelectors.Length); // Two list selectors
        Assert.Empty(eventRequest.ForeignIdSelectors); // Should be empty initially
    }

    [Fact]
    public void EventRequester_ListConstructorUsingParams_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url,
            p => new List<Guid?> { p.id, p.siteId },
            p => p.listOfForeignIds?.Cast<Guid?>().ToList() ?? new List<Guid?>());

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors); // Should be empty for list-only constructor
        Assert.Equal(2, eventRequest.ListSelectors.Length); // Two list selectors
        Assert.Empty(eventRequest.ForeignIdSelectors); // Should be empty initially
    }

    [Fact]
    public void EventRequester_ListConstructor_GetAllForeignIdSelectors_ExpandsCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        var testProjections = new List<TestProjectionForExternalData>
        {
            new TestProjectionForExternalData { id = Guid.NewGuid(), siteId = Guid.NewGuid(), ownerId = Guid.NewGuid() }
        };
        
        Func<TestProjectionForExternalData, List<Guid?>>[] listSelectors = {
            p => new List<Guid?> { p.id, p.siteId }, // 2 IDs from first selector
            p => new List<Guid?> { p.ownerId }       // 1 ID from second selector
        };

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, listSelectors);
        var allSelectors = eventRequest.GetAllForeignIdSelectors(testProjections);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(3, allSelectors.Length); // Should expand to 3 individual selectors (2 + 1)
    }

    [Fact]
    public void EventRequester_ListConstructor_ThrowsExceptionForNullUrl()
    {
        // Arrange
        Func<TestProjectionForExternalData, List<Guid?>>[] listSelectors = { p => new List<Guid?> { p.id } };

        // Act & Assert
    }

    [Fact]
    public void EventRequester_ParamsConstructor_HandlesNullSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, (Func<TestProjectionForExternalData, Guid?>[])null!);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.ForeignIdSelectors);
    }

    [Fact]
    public void EventRequester_ParamsConstructor_HandlesSingleSelector()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid?> selector = p => p.siteId;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Single(eventRequest.ForeignIdSelectors);
        Assert.Equal(selector, eventRequest.ForeignIdSelectors[0]);
    }

    [Fact]
    public void EventRequester_ListParamsConstructor_HandlesSingleListSelector()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, List<Guid?>> selector = p => new List<Guid?> { p.id, p.siteId };

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors);
        Assert.Single(eventRequest.ListSelectors);
        Assert.Equal(selector, eventRequest.ListSelectors[0]);
        Assert.Empty(eventRequest.ForeignIdSelectors); // Should be empty initially
    }

    [Fact]
    public void EventRequester_ListParamsConstructor_HandlesNullSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, (Func<TestProjectionForExternalData, List<Guid?>>[]?)null!);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors);
        Assert.Empty(eventRequest.ListSelectors);
        Assert.Empty(eventRequest.ForeignIdSelectors);
    }

    [Fact]
    public void EventRequester_ListParamsConstructor_HandlesMultipleListSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, List<Guid?>> selector1 = p => new List<Guid?> { p.id, p.siteId };
        Func<TestProjectionForExternalData, List<Guid?>> selector2 = p => new List<Guid?> { p.ownerId };
        Func<TestProjectionForExternalData, List<Guid?>> selector3 = p => p.listOfForeignIds?.Cast<Guid?>().ToList() ?? new List<Guid?>();

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector1, selector2, selector3);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors);
        Assert.Equal(3, eventRequest.ListSelectors.Length);
        Assert.Equal(selector1, eventRequest.ListSelectors[0]);
        Assert.Equal(selector2, eventRequest.ListSelectors[1]);
        Assert.Equal(selector3, eventRequest.ListSelectors[2]);
        Assert.Empty(eventRequest.ForeignIdSelectors); // Should be empty initially
    }

    [Fact]
    public void EventRequester_MixedConstructor_HandlesNullSingleSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, List<Guid?>>[] listSelectors = { p => new List<Guid?> { p.id } };

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, (Func<TestProjectionForExternalData, Guid?>[]?)null!, listSelectors);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors);
        Assert.Single(eventRequest.ListSelectors);
        Assert.Empty(eventRequest.ForeignIdSelectors); // Should be empty when no single selectors
    }

    [Fact]
    public void EventRequester_MixedConstructor_HandlesNullListSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid?>[] singleSelectors = { p => p.siteId };

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, singleSelectors, (Func<TestProjectionForExternalData, List<Guid?>>[]?)null!);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Single(eventRequest.SingleSelectors);
        Assert.Empty(eventRequest.ListSelectors);
        Assert.Single(eventRequest.ForeignIdSelectors); // Should contain single selectors
    }

    [Fact]
    public void EventRequester_MixedConstructor_HandlesBothNullSelectors()
    {
        // Arrange
        var url = "https://test.com/api/events";

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, (Func<TestProjectionForExternalData, Guid?>[]?)null!, (Func<TestProjectionForExternalData, List<Guid?>>[]?)null!);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors);
        Assert.Empty(eventRequest.ListSelectors);
        Assert.Empty(eventRequest.ForeignIdSelectors);
    }

    [Fact]
    public void EventRequester_MixedConstructor_ThrowsExceptionForEmptyUrl()
    {
        // Arrange
        Func<TestProjectionForExternalData, Guid?>[] singleSelectors = { p => p.siteId };
        Func<TestProjectionForExternalData, List<Guid?>>[] listSelectors = { p => new List<Guid?> { p.id } };

        // Act & Assert
        Assert.Throws<NostifyException>(() => 
            new EventRequester<TestProjectionForExternalData>("", singleSelectors, listSelectors));
    }

    [Fact]
    public void EventRequester_ListParamsConstructor_ThrowsExceptionForEmptyUrl()
    {
        // Arrange
        Func<TestProjectionForExternalData, List<Guid?>> listSelector = p => new List<Guid?> { p.id };

        // Act & Assert
        Assert.Throws<NostifyException>(() => 
            new EventRequester<TestProjectionForExternalData>("", listSelector));
    }

    [Fact]
    public void EventRequester_SingleGuidConstructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        var testGuid = Guid.NewGuid();
        Func<TestProjectionForExternalData, Guid> selector = p => testGuid;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Single(eventRequest.SingleSelectors);
        Assert.Empty(eventRequest.ListSelectors);
        Assert.Single(eventRequest.ForeignIdSelectors);
        
        // Test that the selector returns the correct guid
        var testProjection = new TestProjectionForExternalData();
        var result = eventRequest.SingleSelectors[0](testProjection);
        Assert.Equal(testGuid, result);
    }

    [Fact]
    public void EventRequester_SingleGuidConstructor_ThrowsExceptionForEmptyUrl()
    {
        // Arrange
        Func<TestProjectionForExternalData, Guid> selector = p => Guid.NewGuid();

        // Act & Assert
        Assert.Throws<NostifyException>(() => new EventRequester<TestProjectionForExternalData>("", selector));
    }

    [Fact]
    public void EventRequester_ListGuidConstructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        var testGuids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        Func<TestProjectionForExternalData, List<Guid>> selector = p => testGuids;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors);
        Assert.Single(eventRequest.ListSelectors);
        Assert.Empty(eventRequest.ForeignIdSelectors);
        
        // Test that the list selector returns the correct guids
        var testProjection = new TestProjectionForExternalData();
        var result = eventRequest.ListSelectors[0](testProjection);
        Assert.Equal(3, result.Count);
        Assert.Equal(testGuids[0], result[0]);
        Assert.Equal(testGuids[1], result[1]);
        Assert.Equal(testGuids[2], result[2]);
    }

    [Fact]
    public void EventRequester_ListGuidConstructor_ThrowsExceptionForEmptyUrl()
    {
        // Arrange
        Func<TestProjectionForExternalData, List<Guid>> selector = p => new List<Guid> { Guid.NewGuid() };

        // Act & Assert
        Assert.Throws<NostifyException>(() => new EventRequester<TestProjectionForExternalData>("", selector));
    }

    [Fact]
    public void EventRequester_ListGuidConstructor_HandlesNullList()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, List<Guid>> selector = p => null!;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, selector);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors);
        Assert.Single(eventRequest.ListSelectors);
        
        // Test that the list selector returns empty list for null input
        var testProjection = new TestProjectionForExternalData();
        var result = eventRequest.ListSelectors[0](testProjection);
        Assert.Empty(result);
    }

    [Fact]
    public void EventRequester_MixedGuidConstructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var url = "https://test.com/api/events";
        var singleGuid1 = Guid.NewGuid();
        var singleGuid2 = Guid.NewGuid();
        var listGuids1 = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var listGuids2 = new List<Guid> { Guid.NewGuid() };
        
        var singleSelectors = new Func<TestProjectionForExternalData, Guid>[] {
            p => singleGuid1,
            p => singleGuid2
        };
        var listSelectors = new Func<TestProjectionForExternalData, List<Guid>>[] { 
            p => listGuids1,
            p => listGuids2
        };

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, singleSelectors, listSelectors);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Equal(2, eventRequest.SingleSelectors.Length);
        Assert.Equal(2, eventRequest.ListSelectors.Length);
        Assert.Equal(2, eventRequest.ForeignIdSelectors.Length);
        
        // Test single selectors
        var testProjection = new TestProjectionForExternalData();
        Assert.Equal(singleGuid1, eventRequest.SingleSelectors[0](testProjection));
        Assert.Equal(singleGuid2, eventRequest.SingleSelectors[1](testProjection));
        
        // Test list selectors
        var listResult1 = eventRequest.ListSelectors[0](testProjection);
        Assert.Equal(2, listResult1.Count);
        Assert.Equal(listGuids1[0], listResult1[0]);
        Assert.Equal(listGuids1[1], listResult1[1]);
        
        var listResult2 = eventRequest.ListSelectors[1](testProjection);
        Assert.Single(listResult2);
        Assert.Equal(listGuids2[0], listResult2[0]);
    }

    [Fact]
    public void EventRequester_MixedGuidConstructor_ThrowsExceptionForEmptyUrl()
    {
        // Arrange
        var singleSelectors = new Func<TestProjectionForExternalData, Guid>[] { p => Guid.NewGuid() };
        var listSelectors = new Func<TestProjectionForExternalData, List<Guid>>[] { p => new List<Guid> { Guid.NewGuid() } };

        // Act & Assert
        Assert.Throws<NostifyException>(() => 
            new EventRequester<TestProjectionForExternalData>("", singleSelectors, listSelectors));
    }

    [Fact]
    public void EventRequester_MixedGuidConstructor_HandlesNullArrays()
    {
        // Arrange
        var url = "https://test.com/api/events";
        Func<TestProjectionForExternalData, Guid>[]? singleSelectors = null;
        Func<TestProjectionForExternalData, List<Guid>>[]? listSelectors = null;

        // Act
        var eventRequest = new EventRequester<TestProjectionForExternalData>(url, singleSelectors!, listSelectors!);

        // Assert
        Assert.Equal(url, eventRequest.Url);
        Assert.Empty(eventRequest.SingleSelectors);
        Assert.Empty(eventRequest.ListSelectors);
        Assert.Empty(eventRequest.ForeignIdSelectors);
    }
}
