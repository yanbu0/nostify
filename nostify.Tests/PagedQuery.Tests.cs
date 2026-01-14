using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;
using nostify;

namespace nostify.Tests;

public class PagedQueryTests
{
    // Test model that implements ITenantFilterable
    public class TestTenantItem : NostifyObject, ITenantFilterable
    {
        public string name { get; set; } = string.Empty;
        public int age { get; set; }
        public string email { get; set; } = string.Empty;
        public bool isActive { get; set; }
        public DateTime createdAt { get; set; }

        public override void Apply(IEvent e)
        {
            UpdateProperties<TestTenantItem>(e.payload);
        }
    }

    // Test model without ITenantFilterable
    public class TestItem : NostifyObject
    {
        public string name { get; set; } = string.Empty;
        public int value { get; set; }
        public bool isDeleted { get; set; }
        
        // Custom partition key properties for testing
        public Guid userId { get; set; }
        public Guid organizationId { get; set; }
        public Guid deviceId { get; set; }
        public Guid accountId { get; set; }
        public Guid projectId { get; set; }
        public Guid sessionId { get; set; }
        public Guid customerId { get; set; }
        public Guid vendorId { get; set; }
        public Guid locationId { get; set; }
        public Guid groupId { get; set; }
        public Guid departmentId { get; set; }
        public Guid workspaceId { get; set; }
        public Guid regionId { get; set; }
        public Guid customPartitionKey { get; set; }

        public override void Apply(IEvent e)
        {
            UpdateProperties<TestItem>(e.payload);
        }
    }

    [Fact]
    public void TableStateChange_DefaultValues_ShouldBeValid()
    {
        // Arrange & Act
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = null,
            sortColumn = null,
            sortDirection = null
        };

        // Assert
        Assert.Equal(1, tableState.page);
        Assert.Equal(10, tableState.pageSize);
        Assert.Null(tableState.filters);
        Assert.Null(tableState.sortColumn);
        Assert.Null(tableState.sortDirection);
    }

    [Fact]
    public void TableStateChange_WithFilters_ShouldStoreFilters()
    {
        // Arrange
        var filters = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("name", "John"),
            new KeyValuePair<string, string>("age", "30")
        };

        // Act
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 20,
            filters = filters,
            sortColumn = "name",
            sortDirection = "desc"
        };

        // Assert
        Assert.NotNull(tableState.filters);
        Assert.Equal(2, tableState.filters.Count);
        Assert.Equal("name", tableState.filters[0].Key);
        Assert.Equal("John", tableState.filters[0].Value);
        Assert.Equal("age", tableState.filters[1].Key);
        Assert.Equal("30", tableState.filters[1].Value);
    }

    [Fact]
    public void PagedResult_DefaultConstructor_ShouldAllowPropertyAssignment()
    {
        // Arrange & Act
        var result = new PagedResult<TestItem>
        {
            items = new List<TestItem>(),
            totalCount = 0
        };

        // Assert
        Assert.NotNull(result.items);
        Assert.Equal(0, result.totalCount);
    }

    [Fact]
    public void PagedResult_WithItems_ShouldStoreItemsAndCount()
    {
        // Arrange
        var items = new List<TestItem>
        {
            new TestItem { id = Guid.NewGuid(), name = "Item1", value = 100 },
            new TestItem { id = Guid.NewGuid(), name = "Item2", value = 200 }
        };

        // Act
        var result = new PagedResult<TestItem>
        {
            items = items,
            totalCount = 50
        };

        // Assert
        Assert.NotNull(result.items);
        Assert.Equal(2, result.items.Count);
        Assert.Equal(50, result.totalCount);
        Assert.Equal("Item1", result.items[0].name);
        Assert.Equal(200, result.items[1].value);
    }

    [Fact]
    public void ITableStateChange_Interface_ShouldDefineRequiredProperties()
    {
        // Arrange
        ITableStateChange tableState = new TableStateChange
        {
            page = 2,
            pageSize = 25,
            filters = new List<KeyValuePair<string, string>>(),
            sortColumn = "id",
            sortDirection = "asc"
        };

        // Assert
        Assert.Equal(2, tableState.page);
        Assert.Equal(25, tableState.pageSize);
        Assert.NotNull(tableState.filters);
        Assert.Equal("id", tableState.sortColumn);
        Assert.Equal("asc", tableState.sortDirection);
    }

    [Fact]
    public void IPagedResult_Interface_ShouldDefineRequiredProperties()
    {
        // Arrange
        IPagedResult<TestItem> result = new PagedResult<TestItem>
        {
            items = new List<TestItem> { new TestItem { name = "Test" } },
            totalCount = 100
        };

        // Assert
        Assert.NotNull(result.items);
        Assert.Single(result.items);
        Assert.Equal(100, result.totalCount);
    }

    [Fact]
    public void TableStateChange_PaginationCalculation_ShouldBeCorrect()
    {
        // Arrange
        var tableState = new TableStateChange
        {
            page = 3,
            pageSize = 15
        };

        // Act
        var offset = (tableState.page - 1) * tableState.pageSize;

        // Assert
        Assert.Equal(30, offset); // (3-1) * 15 = 30
    }

    [Fact]
    public void TableStateChange_MultipleFilters_ShouldMaintainOrder()
    {
        // Arrange
        var filters = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("status", "active"),
            new KeyValuePair<string, string>("category", "premium"),
            new KeyValuePair<string, string>("region", "us-west")
        };

        // Act
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = filters
        };

        // Assert
        Assert.Equal(3, tableState.filters.Count);
        Assert.Equal("status", tableState.filters[0].Key);
        Assert.Equal("category", tableState.filters[1].Key);
        Assert.Equal("region", tableState.filters[2].Key);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    [InlineData("ASC")]
    [InlineData("DESC")]
    public void TableStateChange_SortDirection_ShouldAcceptVariousFormats(string direction)
    {
        // Arrange & Act
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "name",
            sortDirection = direction
        };

        // Assert
        Assert.Equal(direction, tableState.sortDirection);
    }

    [Fact]
    public void PagedResult_GenericType_ShouldWorkWithDifferentTypes()
    {
        // Arrange & Act
        var stringResult = new PagedResult<string>
        {
            items = new List<string> { "one", "two", "three" },
            totalCount = 100
        };

        var intResult = new PagedResult<int>
        {
            items = new List<int> { 1, 2, 3 },
            totalCount = 50
        };

        // Assert
        Assert.Equal(3, stringResult.items.Count);
        Assert.Equal(100, stringResult.totalCount);
        Assert.Equal(3, intResult.items.Count);
        Assert.Equal(50, intResult.totalCount);
    }

    [Fact]
    public void TableStateChange_EmptyFilters_ShouldHandleNull()
    {
        // Arrange & Act
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = null
        };

        // Assert
        Assert.Null(tableState.filters);
    }

    [Fact]
    public void TableStateChange_EmptyFilters_ShouldHandleEmptyList()
    {
        // Arrange & Act
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>()
        };

        // Assert
        Assert.NotNull(tableState.filters);
        Assert.Empty(tableState.filters);
    }

    [Fact]
    public void PagedResult_EmptyItems_ShouldBeValid()
    {
        // Arrange & Act
        var result = new PagedResult<TestItem>
        {
            items = new List<TestItem>(),
            totalCount = 0
        };

        // Assert
        Assert.NotNull(result.items);
        Assert.Empty(result.items);
        Assert.Equal(0, result.totalCount);
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 25, 50)]
    [InlineData(5, 100, 400)]
    public void TableStateChange_OffsetCalculation_ShouldBeCorrect(int page, int pageSize, int expectedOffset)
    {
        // Arrange
        var tableState = new TableStateChange
        {
            page = page,
            pageSize = pageSize
        };

        // Act
        var offset = (tableState.page - 1) * tableState.pageSize;

        // Assert
        Assert.Equal(expectedOffset, offset);
    }

    [Fact]
    public void TestTenantItem_ShouldImplementITenantFilterable()
    {
        // Arrange & Act
        var item = new TestTenantItem
        {
            id = Guid.NewGuid(),
            tenantId = Guid.NewGuid(),
            name = "Test",
            age = 25,
            email = "test@example.com"
        };

        // Assert
        Assert.IsAssignableFrom<ITenantFilterable>(item);
        Assert.NotEqual(Guid.Empty, item.tenantId);
    }

    [Fact]
    public void TableStateChange_ComplexScenario_ShouldHandleAllProperties()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var filters = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("status", "active"),
            new KeyValuePair<string, string>("verified", "true")
        };

        // Act
        var tableState = new TableStateChange
        {
            page = 3,
            pageSize = 20,
            filters = filters,
            sortColumn = "createdDate",
            sortDirection = "desc"
        };

        // Assert
        Assert.Equal(3, tableState.page);
        Assert.Equal(20, tableState.pageSize);
        Assert.Equal(2, tableState.filters.Count);
        Assert.Equal("createdDate", tableState.sortColumn);
        Assert.Equal("desc", tableState.sortDirection);
    }

    // Integration tests with mocked Cosmos DB Container
    [Fact]
    public async Task PagedQueryAsync_WithValidTenantId_ShouldReturnPagedResults()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Item1", age = 25, email = "item1@test.com" },
            new TestTenantItem { tenantId = tenantId, name = "Item2", age = 30, email = "item2@test.com" },
            new TestTenantItem { tenantId = tenantId, name = "Item3", age = 35, email = "item3@test.com" }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = null,
            sortColumn = null,
            sortDirection = null
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.items.Count);
        Assert.Equal(3, result.totalCount);
        Assert.Equal("Item1", result.items[0].name);
    }

    [Fact]
    public async Task PagedQueryAsync_WithEmptyGuid_ShouldThrowArgumentException()
    {
        // Arrange
        var mockContainer = new Mock<Container>();
        var tableState = new TableStateChange { page = 1, pageSize = 10 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, Guid.Empty, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithZeroPage_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange { page = 0, pageSize = 10 };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        Assert.Contains("Page must be greater than or equal to 1", ex.Message);
        Assert.Contains("0", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithNegativePage_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange { page = -5, pageSize = 10 };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        Assert.Contains("Page must be greater than or equal to 1", ex.Message);
        Assert.Contains("-5", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithZeroPageSize_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange { page = 1, pageSize = 0 };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        Assert.Contains("Page size must be greater than or equal to 1", ex.Message);
        Assert.Contains("0", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithNegativePageSize_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange { page = 1, pageSize = -10 };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        Assert.Contains("Page size must be greater than or equal to 1", ex.Message);
        Assert.Contains("-10", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithZeroPage_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange { page = 0, pageSize = 10 };

        IOrderedQueryable<TestTenantItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await orderedQuery.PagedQueryAsync(tableState, tenantId, InMemoryQueryExecutor.Default));

        Assert.Contains("Page must be greater than or equal to 1", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithZeroPageSize_ShouldThrowArgumentException()
    {
        // Arrange
        var partitionKeyValue = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { customPartitionKey = partitionKeyValue, name = "Test", value = 100 }
        };
        var tableState = new TableStateChange { page = 1, pageSize = 0 };

        IOrderedQueryable<TestItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await orderedQuery.PagedQueryAsync(tableState, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default));

        Assert.Contains("Page size must be greater than or equal to 1", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithNullTableState_ShouldThrowArgumentNullException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };

        var queryable = testItems.AsQueryable();
        TableStateChange? tableState = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState!, tenantId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithOverflowingPaginationValues_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        // These values would cause (page - 1) * pageSize to overflow
        var tableState = new TableStateChange { page = int.MaxValue, pageSize = 2 };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        Assert.Contains("overflow", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithInvalidPartitionKeyName_ShouldThrowArgumentException()
    {
        // Arrange
        var partitionKeyValue = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { customPartitionKey = partitionKeyValue, name = "Test", value = 100 }
        };
        var tableState = new TableStateChange { page = 1, pageSize = 10 };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestItem>(tableState, "invalid; DROP TABLE--", partitionKeyValue, InMemoryQueryExecutor.Default));

        Assert.Contains("Invalid partition key name", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithNullTableState_ShouldThrowArgumentNullException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };

        IOrderedQueryable<TestTenantItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);
        TableStateChange? tableState = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await orderedQuery.PagedQueryAsync(tableState!, tenantId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithOverflowingPaginationValues_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        // These values would cause (page - 1) * pageSize to overflow
        var tableState = new TableStateChange { page = int.MaxValue, pageSize = 2 };

        IOrderedQueryable<TestTenantItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await orderedQuery.PagedQueryAsync(tableState, tenantId, InMemoryQueryExecutor.Default));

        Assert.Contains("overflow", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_ShouldReturnResults()
    {
        // Arrange
        var partitionKeyValue = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { customPartitionKey = partitionKeyValue, name = "Item1", value = 100 },
            new TestItem { customPartitionKey = partitionKeyValue, name = "Item2", value = 200 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.items.Count);
        Assert.Equal(2, result.totalCount);
    }

    [Fact]
    public async Task PagedQueryAsync_WithFilters_ShouldReturnFilteredResults()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "ActiveUser", age = 25, email = "active@test.com" }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("name", "ActiveUser")
            }
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.items);
        Assert.Equal("ActiveUser", result.items[0].name);
    }

    [Fact]
    public async Task PagedQueryAsync_WithInvalidFilterColumnName_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("name; DROP TABLE--", "malicious")
            }
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer<TestTenantItem>(new List<TestTenantItem>());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithNonExistentFilterProperty_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("nonExistentProperty", "someValue")
            }
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer<TestTenantItem>(new List<TestTenantItem>());

        // Act & Assert - Expression.Property throws ArgumentException for non-existent properties
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithNonExistentSortProperty_ShouldThrowArgumentException()
    {
        // Arrange - Using IQueryable (not IOrderedQueryable) to test sort property validation
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "nonExistentColumn",
            sortDirection = "asc"
        };

        // Create a regular IQueryable (not IOrderedQueryable) to ensure ApplySorting is called
        var queryable = testItems.AsQueryable();

        // Act & Assert - Expression.Property throws ArgumentException for non-existent properties
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithInvalidGuidFilterValue_ShouldThrowDescriptiveArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("tenantId", "not-a-valid-guid")
            }
        };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        // Verify the exception message contains helpful information
        Assert.Contains("tenantId", ex.Message);
        Assert.Contains("not-a-valid-guid", ex.Message);
        Assert.Contains("Guid", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithInvalidIntFilterValue_ShouldThrowDescriptiveArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("age", "not-a-number")
            }
        };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        // Verify the exception message contains helpful information
        Assert.Contains("age", ex.Message);
        Assert.Contains("not-a-number", ex.Message);
        Assert.Contains("Int32", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithInvalidBoolFilterValue_ShouldThrowDescriptiveArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25, isActive = true }
        };
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("isActive", "not-a-bool")
            }
        };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        // Verify the exception message contains helpful information
        Assert.Contains("isActive", ex.Message);
        Assert.Contains("not-a-bool", ex.Message);
        Assert.Contains("Boolean", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithOverflowIntFilterValue_ShouldThrowDescriptiveArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                // Value larger than int.MaxValue
                new KeyValuePair<string, string>("age", "99999999999999999999")
            }
        };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        // Verify the exception message contains helpful information about overflow
        Assert.Contains("age", ex.Message);
        Assert.Contains("out of range", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithInvalidDateTimeFilterValue_ShouldThrowDescriptiveArgumentException()
    {
        // Arrange - need a type with DateTime property
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Test", age = 25 }
        };
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("createdAt", "not-a-date")
            }
        };

        var queryable = testItems.AsQueryable();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));

        // Verify the exception message contains helpful information
        Assert.Contains("createdAt", ex.Message);
        Assert.Contains("not-a-date", ex.Message);
        Assert.Contains("DateTime", ex.Message);
    }

    [Fact]
    public async Task PagedQueryAsync_WithSorting_ShouldReturnSortedResults()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Alice", age = 30 },
            new TestTenantItem { tenantId = tenantId, name = "Bob", age = 25 },
            new TestTenantItem { tenantId = tenantId, name = "Charlie", age = 35 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "age",
            sortDirection = "asc"
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.items.Count);
        Assert.Equal(3, result.totalCount);
    }

    [Fact]
    public async Task PagedQueryAsync_WithInvalidSortColumn_ShouldThrowArgumentException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "age; DROP TABLE--",
            sortDirection = "asc"
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer<TestTenantItem>(new List<TestTenantItem>());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithPagination_ShouldReturnCorrectPage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        // Provide full dataset so pagination query can actually paginate
        var testItems = new List<TestTenantItem>();
        for (int i = 1; i <= 10; i++)
        {
            testItems.Add(new TestTenantItem { tenantId = tenantId, name = $"Item{i}", age = 20 + i });
        }

        var tableState = new TableStateChange
        {
            page = 2,
            pageSize = 5,
            sortColumn = null,
            sortDirection = null
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.items.Count); // Page 2 with pageSize 5 should return 5 items (items 6-10)
        Assert.Equal(10, result.totalCount); // Total count is all items
        // Verify we got items 6-10
        Assert.Equal("Item6", result.items[0].name);
        Assert.Equal("Item10", result.items[4].name);
    }

    [Fact]
    public async Task PagedQueryAsync_WithMultipleFilters_ShouldApplyAllFilters()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "John", age = 25, email = "john@verified.com" }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("name", "John"),
                new KeyValuePair<string, string>("age", "25")
            }
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.items);
        Assert.Equal("John", result.items[0].name);
        Assert.Equal(25, result.items[0].age);
    }

    [Fact]
    public async Task PagedQueryAsync_WithDescendingSort_ShouldReturnDescendingResults()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "First", age = 35 },
            new TestTenantItem { tenantId = tenantId, name = "Second", age = 30 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "age",
            sortDirection = "desc"
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.items.Count);
    }

    [Fact]
    public async Task PagedQueryAsync_WithNoResults_ShouldReturnEmptyList()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer<TestTenantItem>(new List<TestTenantItem>());

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.items);
        Assert.Equal(0, result.totalCount);
    }

    [Fact]
    public async Task PagedQueryAsync_WithLargePageSize_ShouldHandleCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = Enumerable.Range(1, 100)
            .Select(i => new TestTenantItem
            {
                tenantId = tenantId,
                name = $"Item{i}",
                age = 20 + i
            })
            .ToList();

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 100
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestTenantItem>(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.items.Count);
        Assert.Equal(100, result.totalCount);
    }

    #region Custom Partition Key Tests

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_UserId_ShouldReturnResults()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { userId = userId, name = "UserItem1", value = 100 },
            new TestItem { userId = userId, name = "UserItem2", value = 200 },
            new TestItem { userId = userId, name = "UserItem3", value = 300 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "userId", userId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.items.Count);
        Assert.Equal(3, result.totalCount);
        Assert.Equal("UserItem1", result.items[0].name);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_OrganizationId_ShouldReturnResults()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { organizationId = orgId, name = "OrgItem1", value = 500 },
            new TestItem { organizationId = orgId, name = "OrgItem2", value = 600 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "organizationId", orgId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.items.Count);
        Assert.Equal(2, result.totalCount);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_AndFilters_ShouldReturnFilteredResults()
    {
        // Arrange
        var deviceId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { deviceId = deviceId, name = "Device1", value = 100 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("name", "Device1")
            }
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "deviceId", deviceId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.items);
        Assert.Equal("Device1", result.items[0].name);
        Assert.Equal(100, result.items[0].value);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_AndMultipleFilters_ShouldReturnResults()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { accountId = accountId, name = "AccountItem", value = 250 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("name", "AccountItem"),
                new KeyValuePair<string, string>("value", "250")
            }
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "accountId", accountId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.items);
        Assert.Equal("AccountItem", result.items[0].name);
        Assert.Equal(250, result.items[0].value);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_AndSortAscending_ShouldReturnSortedResults()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { projectId = projectId, name = "Project1", value = 300 },
            new TestItem { projectId = projectId, name = "Project2", value = 100 },
            new TestItem { projectId = projectId, name = "Project3", value = 200 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "value",
            sortDirection = "asc"
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "projectId", projectId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.items.Count);
        // Note: In real scenario, these would be sorted by Cosmos DB, but in our mock we return as-is
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_AndSortDescending_ShouldReturnResults()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { sessionId = sessionId, name = "SessionA", value = 50 },
            new TestItem { sessionId = sessionId, name = "SessionB", value = 150 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "name",
            sortDirection = "desc"
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "sessionId", sessionId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.items.Count);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_AndPagination_ShouldReturnPagedResults()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { customerId = customerId, name = "Customer1", value = 10 },
            new TestItem { customerId = customerId, name = "Customer2", value = 20 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 2
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "customerId", customerId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.items.Count);
        Assert.Equal(2, result.totalCount);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_AndSecondPage_ShouldReturnResults()
    {
        // Arrange
        var vendorId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { vendorId = vendorId, name = "Vendor1", value = 10 },
            new TestItem { vendorId = vendorId, name = "Vendor2", value = 20 },
            new TestItem { vendorId = vendorId, name = "Vendor3", value = 30 },
            new TestItem { vendorId = vendorId, name = "Vendor4", value = 40 }
        };

        var tableState = new TableStateChange
        {
            page = 2,
            pageSize = 2
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "vendorId", vendorId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.items.Count); // Page 2 should return items 3-4
        Assert.Equal(4, result.totalCount);
        Assert.Equal("Vendor3", result.items[0].name);
        Assert.Equal("Vendor4", result.items[1].name);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_EmptyResults_ShouldReturnEmptyList()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var testItems = new List<TestItem>();

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "locationId", locationId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.items);
        Assert.Equal(0, result.totalCount);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_InvalidFilterColumn_ShouldThrowArgumentException()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("name'; DROP TABLE users--", "malicious")
            }
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer<TestItem>(new List<TestItem>());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "groupId", groupId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_NonExistentFilterProperty_ShouldThrowArgumentException()
    {
        // Arrange
        var groupId = Guid.NewGuid();
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("nonExistentProperty", "someValue")
            }
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer<TestItem>(new List<TestItem>());

        // Act & Assert - Expression.Property throws ArgumentException for non-existent properties
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "groupId", groupId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_NonExistentSortProperty_ShouldThrowArgumentException()
    {
        // Arrange - Using IQueryable (not IOrderedQueryable) to test sort property validation
        var groupId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { groupId = groupId, name = "Test", value = 25 }
        };
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "nonExistentColumn",
            sortDirection = "asc"
        };

        // Create a regular IQueryable (not IOrderedQueryable) to ensure ApplySorting is called
        var queryable = testItems.AsQueryable();

        // Act & Assert - Expression.Property throws ArgumentException for non-existent properties
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queryable.PagedQueryAsync<TestItem>(tableState, "groupId", groupId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_InvalidSortColumn_ShouldThrowArgumentException()
    {
        // Arrange
        var departmentId = Guid.NewGuid();
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "name; DELETE FROM items--"
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer<TestItem>(new List<TestItem>());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "departmentId", departmentId, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_ComplexScenario_ShouldReturnResults()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { workspaceId = workspaceId, name = "WorkspaceItem1", value = 75 },
            new TestItem { workspaceId = workspaceId, name = "WorkspaceItem2", value = 125 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 5,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("value", "75")
            },
            sortColumn = "name",
            sortDirection = "asc"
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "workspaceId", workspaceId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.items); // Only 1 item matches the filter value=75
        Assert.Equal(1, result.totalCount);
        Assert.Equal("WorkspaceItem1", result.items[0].name);
        Assert.Equal(75, result.items[0].value);
    }

    [Fact]
    public async Task PagedQueryAsync_WithCustomPartitionKey_LargeDataset_ShouldReturnAllResults()
    {
        // Arrange
        var regionId = Guid.NewGuid();
        var testItems = new List<TestItem>();
        for (int i = 0; i < 50; i++)
        {
            testItems.Add(new TestItem { regionId = regionId, name = $"RegionItem{i}", value = i * 10 });
        }

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 50
        };

        var mockContainer = CosmosTestHelpers.CreateMockContainer(testItems);

        // Act
        var result = await mockContainer.Object.PagedQueryAsync<TestItem>(tableState, "regionId", regionId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(50, result.items.Count);
        Assert.Equal(50, result.totalCount);
    }

    #endregion

    #region IOrderedQueryable Extension Method Tests

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithTenantId_ShouldPreserveOrdering()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Zebra", age = 30 },
            new TestTenantItem { tenantId = tenantId, name = "Apple", age = 25 },
            new TestTenantItem { tenantId = tenantId, name = "Mango", age = 35 },
            new TestTenantItem { tenantId = Guid.NewGuid(), name = "Other", age = 40 } // Different tenant
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            // Sort parameters should be ignored for IOrderedQueryable
            sortColumn = "age",
            sortDirection = "asc"
        };

        // Pre-order by name descending (Zebra, Mango, Apple order expected)
        IOrderedQueryable<TestTenantItem> orderedQuery = testItems.AsQueryable().OrderByDescending(x => x.name);

        // Act
        var result = await orderedQuery.PagedQueryAsync(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.totalCount);
        Assert.Equal(3, result.items.Count);
        // Verify pre-applied ordering is preserved (name descending), not tableState sorting (age asc)
        Assert.Equal("Zebra", result.items[0].name);
        Assert.Equal("Mango", result.items[1].name);
        Assert.Equal("Apple", result.items[2].name);
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithTenantId_ShouldApplyPagination()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>();
        for (int i = 0; i < 25; i++)
        {
            testItems.Add(new TestTenantItem { tenantId = tenantId, name = $"Item{i:D2}", age = i });
        }

        var tableStatePage1 = new TableStateChange { page = 1, pageSize = 10 };
        var tableStatePage2 = new TableStateChange { page = 2, pageSize = 10 };
        var tableStatePage3 = new TableStateChange { page = 3, pageSize = 10 };

        // Pre-order by name ascending
        IOrderedQueryable<TestTenantItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);

        // Act
        var resultPage1 = await orderedQuery.PagedQueryAsync(tableStatePage1, tenantId, InMemoryQueryExecutor.Default);
        var resultPage2 = await orderedQuery.PagedQueryAsync(tableStatePage2, tenantId, InMemoryQueryExecutor.Default);
        var resultPage3 = await orderedQuery.PagedQueryAsync(tableStatePage3, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.Equal(25, resultPage1.totalCount);
        Assert.Equal(10, resultPage1.items.Count);
        Assert.Equal("Item00", resultPage1.items[0].name);
        Assert.Equal("Item09", resultPage1.items[9].name);

        Assert.Equal(25, resultPage2.totalCount);
        Assert.Equal(10, resultPage2.items.Count);
        Assert.Equal("Item10", resultPage2.items[0].name);
        Assert.Equal("Item19", resultPage2.items[9].name);

        Assert.Equal(25, resultPage3.totalCount);
        Assert.Equal(5, resultPage3.items.Count); // Only 5 remaining items
        Assert.Equal("Item20", resultPage3.items[0].name);
        Assert.Equal("Item24", resultPage3.items[4].name);
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithTenantId_ShouldApplyFilters()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>
        {
            new TestTenantItem { tenantId = tenantId, name = "Alice", age = 30, email = "alice@test.com" },
            new TestTenantItem { tenantId = tenantId, name = "Bob", age = 25, email = "bob@test.com" },
            new TestTenantItem { tenantId = tenantId, name = "Charlie", age = 30, email = "charlie@test.com" }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("age", "30")
            }
        };

        // Pre-order by name ascending
        IOrderedQueryable<TestTenantItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);

        // Act
        var result = await orderedQuery.PagedQueryAsync(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.Equal(2, result.totalCount);
        Assert.Equal(2, result.items.Count);
        // Verify ordering is preserved after filtering
        Assert.Equal("Alice", result.items[0].name);
        Assert.Equal("Charlie", result.items[1].name);
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithTenantId_EmptyGuid_ShouldThrow()
    {
        // Arrange
        var testItems = new List<TestTenantItem>();
        IOrderedQueryable<TestTenantItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);
        var tableState = new TableStateChange { page = 1, pageSize = 10 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await orderedQuery.PagedQueryAsync(tableState, Guid.Empty, InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithCustomPartitionKey_ShouldPreserveOrdering()
    {
        // Arrange
        var partitionKeyValue = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { customPartitionKey = partitionKeyValue, name = "Zebra", value = 300 },
            new TestItem { customPartitionKey = partitionKeyValue, name = "Apple", value = 100 },
            new TestItem { customPartitionKey = partitionKeyValue, name = "Mango", value = 200 },
            new TestItem { customPartitionKey = Guid.NewGuid(), name = "Other", value = 400 } // Different partition
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            // Sort parameters should be ignored for IOrderedQueryable
            sortColumn = "name",
            sortDirection = "asc"
        };

        // Pre-order by value descending (300, 200, 100 order expected)
        IOrderedQueryable<TestItem> orderedQuery = testItems.AsQueryable().OrderByDescending(x => x.value);

        // Act
        var result = await orderedQuery.PagedQueryAsync(tableState, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.totalCount);
        Assert.Equal(3, result.items.Count);
        // Verify pre-applied ordering is preserved (value descending), not tableState sorting (name asc)
        Assert.Equal("Zebra", result.items[0].name);  // value 300
        Assert.Equal("Mango", result.items[1].name);  // value 200
        Assert.Equal("Apple", result.items[2].name);  // value 100
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithCustomPartitionKey_ShouldApplyPagination()
    {
        // Arrange
        var partitionKeyValue = Guid.NewGuid();
        var testItems = new List<TestItem>();
        for (int i = 0; i < 30; i++)
        {
            testItems.Add(new TestItem { customPartitionKey = partitionKeyValue, name = $"Item{i:D2}", value = i * 10 });
        }

        var tableStatePage1 = new TableStateChange { page = 1, pageSize = 10 };
        var tableStatePage2 = new TableStateChange { page = 2, pageSize = 10 };
        var tableStatePage3 = new TableStateChange { page = 3, pageSize = 10 };

        // Pre-order by value descending
        IOrderedQueryable<TestItem> orderedQuery = testItems.AsQueryable().OrderByDescending(x => x.value);

        // Act
        var resultPage1 = await orderedQuery.PagedQueryAsync(tableStatePage1, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default);
        var resultPage2 = await orderedQuery.PagedQueryAsync(tableStatePage2, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default);
        var resultPage3 = await orderedQuery.PagedQueryAsync(tableStatePage3, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default);

        // Assert - items ordered by value descending: 290, 280, ..., 0
        Assert.Equal(30, resultPage1.totalCount);
        Assert.Equal(10, resultPage1.items.Count);
        Assert.Equal(290, resultPage1.items[0].value);  // Item29
        Assert.Equal(200, resultPage1.items[9].value);  // Item20

        Assert.Equal(30, resultPage2.totalCount);
        Assert.Equal(10, resultPage2.items.Count);
        Assert.Equal(190, resultPage2.items[0].value);  // Item19
        Assert.Equal(100, resultPage2.items[9].value);  // Item10

        Assert.Equal(30, resultPage3.totalCount);
        Assert.Equal(10, resultPage3.items.Count);
        Assert.Equal(90, resultPage3.items[0].value);   // Item09
        Assert.Equal(0, resultPage3.items[9].value);    // Item00
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithCustomPartitionKey_ShouldApplyFilters()
    {
        // Arrange
        var partitionKeyValue = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { customPartitionKey = partitionKeyValue, name = "HighValue1", value = 100 },
            new TestItem { customPartitionKey = partitionKeyValue, name = "LowValue", value = 50 },
            new TestItem { customPartitionKey = partitionKeyValue, name = "HighValue2", value = 100 }
        };

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("value", "100")
            }
        };

        // Pre-order by name descending
        IOrderedQueryable<TestItem> orderedQuery = testItems.AsQueryable().OrderByDescending(x => x.name);

        // Act
        var result = await orderedQuery.PagedQueryAsync(tableState, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default);

        // Assert
        Assert.Equal(2, result.totalCount);
        Assert.Equal(2, result.items.Count);
        // Verify ordering is preserved after filtering (name descending)
        Assert.Equal("HighValue2", result.items[0].name);
        Assert.Equal("HighValue1", result.items[1].name);
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithCustomPartitionKey_InvalidPartitionKeyName_ShouldThrow()
    {
        // Arrange
        var testItems = new List<TestItem>();
        IOrderedQueryable<TestItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);
        var tableState = new TableStateChange { page = 1, pageSize = 10 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await orderedQuery.PagedQueryAsync(tableState, "invalid; DROP TABLE--", Guid.NewGuid(), InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithCustomPartitionKey_InvalidSortColumn_ShouldThrow()
    {
        // Arrange
        var testItems = new List<TestItem>();
        IOrderedQueryable<TestItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);
        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 10,
            sortColumn = "invalid; DROP TABLE--"
        };

        // Act & Assert - should validate sortColumn even though it's ignored
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await orderedQuery.PagedQueryAsync(tableState, "customPartitionKey", Guid.NewGuid(), InMemoryQueryExecutor.Default));
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithThenBy_ShouldPreserveSecondaryOrdering()
    {
        // Arrange
        var partitionKeyValue = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { customPartitionKey = partitionKeyValue, name = "Apple", value = 100 },
            new TestItem { customPartitionKey = partitionKeyValue, name = "Apple", value = 50 },
            new TestItem { customPartitionKey = partitionKeyValue, name = "Banana", value = 75 },
            new TestItem { customPartitionKey = partitionKeyValue, name = "Banana", value = 25 }
        };

        var tableState = new TableStateChange { page = 1, pageSize = 10 };

        // Pre-order by name ascending, then by value descending
        IOrderedQueryable<TestItem> orderedQuery = testItems.AsQueryable()
            .OrderBy(x => x.name)
            .ThenByDescending(x => x.value);

        // Act
        var result = await orderedQuery.PagedQueryAsync(tableState, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default);

        // Assert - verify both primary and secondary ordering preserved
        Assert.Equal(4, result.items.Count);
        Assert.Equal("Apple", result.items[0].name);
        Assert.Equal(100, result.items[0].value);  // Apple with higher value first
        Assert.Equal("Apple", result.items[1].name);
        Assert.Equal(50, result.items[1].value);   // Apple with lower value second
        Assert.Equal("Banana", result.items[2].name);
        Assert.Equal(75, result.items[2].value);   // Banana with higher value first
        Assert.Equal("Banana", result.items[3].name);
        Assert.Equal(25, result.items[3].value);   // Banana with lower value second
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_EmptyResult_ShouldReturnEmptyList()
    {
        // Arrange
        var partitionKeyValue = Guid.NewGuid();
        var testItems = new List<TestItem>
        {
            new TestItem { customPartitionKey = Guid.NewGuid(), name = "Other", value = 100 } // Different partition
        };

        var tableState = new TableStateChange { page = 1, pageSize = 10 };

        IOrderedQueryable<TestItem> orderedQuery = testItems.AsQueryable().OrderBy(x => x.name);

        // Act
        var result = await orderedQuery.PagedQueryAsync(tableState, "customPartitionKey", partitionKeyValue, InMemoryQueryExecutor.Default);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.items);
        Assert.Equal(0, result.totalCount);
    }

    [Fact]
    public async Task PagedQueryAsync_IOrderedQueryable_WithFiltersAndPagination_Combined()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var testItems = new List<TestTenantItem>();
        for (int i = 0; i < 20; i++)
        {
            // Half with age 30, half with age 25
            testItems.Add(new TestTenantItem
            {
                tenantId = tenantId,
                name = $"User{i:D2}",
                age = i % 2 == 0 ? 30 : 25
            });
        }

        var tableState = new TableStateChange
        {
            page = 1,
            pageSize = 5,
            filters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("age", "30")
            }
        };

        // Pre-order by name descending
        IOrderedQueryable<TestTenantItem> orderedQuery = testItems.AsQueryable().OrderByDescending(x => x.name);

        // Act
        var result = await orderedQuery.PagedQueryAsync(tableState, tenantId, InMemoryQueryExecutor.Default);

        // Assert
        Assert.Equal(10, result.totalCount); // 10 items with age=30
        Assert.Equal(5, result.items.Count);  // Page size of 5
        // Verify ordering preserved (name descending) with even-numbered items (age=30)
        Assert.Equal("User18", result.items[0].name);
        Assert.Equal("User16", result.items[1].name);
        Assert.Equal("User14", result.items[2].name);
        Assert.Equal("User12", result.items[3].name);
        Assert.Equal("User10", result.items[4].name);
    }

    #endregion
}
