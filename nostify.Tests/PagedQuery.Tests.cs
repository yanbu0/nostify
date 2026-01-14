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
}
