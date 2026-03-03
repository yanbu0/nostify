using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace nostify.Tests;

public class RetryableQueryTests
{
    #region Test Helpers

    private static List<TestAggregate> CreateTestData()
    {
        return new List<TestAggregate>
        {
            new TestAggregate { id = Guid.NewGuid(), name = "Alpha" },
            new TestAggregate { id = Guid.NewGuid(), name = "Bravo" },
            new TestAggregate { id = Guid.NewGuid(), name = "Charlie" },
            new TestAggregate { id = Guid.NewGuid(), name = "Delta" },
            new TestAggregate { id = Guid.NewGuid(), name = "Echo" },
        };
    }

    private static RetryableQuery<TestAggregate> CreateQuery(
        List<TestAggregate>? data = null,
        RetryOptions? options = null,
        IQueryExecutor? executor = null)
    {
        data ??= CreateTestData();
        options ??= new RetryOptions();
        executor ??= InMemoryQueryExecutor.Default;
        return new RetryableQuery<TestAggregate>(data.AsQueryable(), options, executor);
    }

    /// <summary>
    /// A test IQueryExecutor that throws CosmosException(429) for the first N calls,
    /// then delegates to InMemoryQueryExecutor.
    /// </summary>
    private class ThrowThenSucceedQueryExecutor : IQueryExecutor
    {
        private int _callCount;
        private readonly int _failCount;

        public ThrowThenSucceedQueryExecutor(int failCount = 1)
        {
            _failCount = failCount;
        }

        public int CallCount => _callCount;

        private void MaybeThrow()
        {
            _callCount++;
            if (_callCount <= _failCount)
            {
                throw new CosmosException("Rate limited", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);
            }
        }

        public Task<List<T>> ReadAllAsync<T>(IQueryable<T> query)
        {
            MaybeThrow();
            return Task.FromResult(query.ToList());
        }

        public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query)
        {
            MaybeThrow();
            return Task.FromResult(query.FirstOrDefault());
        }

        public Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new()
        {
            MaybeThrow();
            return Task.FromResult(query.FirstOrDefault() ?? new T());
        }

        public Task<int> CountAsync<T>(IQueryable<T> query)
        {
            MaybeThrow();
            return Task.FromResult(query.Count());
        }
    }

    /// <summary>
    /// A test IQueryExecutor that always throws CosmosException(429).
    /// </summary>
    private class AlwaysThrow429QueryExecutor : IQueryExecutor
    {
        public int CallCount { get; private set; }

        private void Throw()
        {
            CallCount++;
            throw new CosmosException("Rate limited", HttpStatusCode.TooManyRequests, 0, string.Empty, 0);
        }

        public Task<List<T>> ReadAllAsync<T>(IQueryable<T> query) { Throw(); return null!; }
        public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query) { Throw(); return null!; }
        public Task<T> FirstOrNewAsync<T>(IQueryable<T> query) where T : new() { Throw(); return null!; }
        public Task<int> CountAsync<T>(IQueryable<T> query) { Throw(); return null!; }
    }

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_NullQuery_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RetryableQuery<TestAggregate>(null!, new RetryOptions()));
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var data = new List<TestAggregate>().AsQueryable();
        Assert.Throws<ArgumentNullException>(() =>
            new RetryableQuery<TestAggregate>(data, null!));
    }

    [Fact]
    public void Constructor_PreservesQueryAndOptions()
    {
        var data = CreateTestData().AsQueryable();
        var options = new RetryOptions { MaxRetries = 7 };
        var query = new RetryableQuery<TestAggregate>(data, options, InMemoryQueryExecutor.Default);

        Assert.Same(data, query.Query);
        Assert.Same(options, query.Options);
    }

    #endregion

    #region LINQ Chain Methods

    [Fact]
    public async Task Where_FiltersCorrectly()
    {
        var query = CreateQuery();
        var results = await query.Where(x => x.name.Contains("a")).ReadAllAsync();

        Assert.Equal(4, results.Count); // Alpha, Bravo, Charlie, Delta all contain lowercase 'a'
        Assert.All(results, r => Assert.Contains("a", r.name));
    }

    [Fact]
    public void Where_PreservesRetryOptions()
    {
        var options = new RetryOptions { MaxRetries = 42 };
        var query = CreateQuery(options: options);
        var filtered = query.Where(x => x.name == "Alpha");

        Assert.Same(options, filtered.Options);
    }

    [Fact]
    public async Task Select_ProjectsCorrectly()
    {
        var query = CreateQuery();
        var names = await query.Select(x => x.name).ReadAllAsync();

        Assert.Equal(5, names.Count);
        Assert.Contains("Alpha", names);
        Assert.Contains("Echo", names);
    }

    [Fact]
    public async Task OrderBy_SortsAscending()
    {
        var query = CreateQuery();
        var results = await query.OrderBy(x => x.name).ReadAllAsync();

        Assert.Equal("Alpha", results[0].name);
        Assert.Equal("Bravo", results[1].name);
        Assert.Equal("Echo", results[4].name);
    }

    [Fact]
    public async Task OrderByDescending_SortsDescending()
    {
        var query = CreateQuery();
        var results = await query.OrderByDescending(x => x.name).ReadAllAsync();

        Assert.Equal("Echo", results[0].name);
        Assert.Equal("Alpha", results[4].name);
    }

    [Fact]
    public async Task ThenBy_AppliesSecondarySort()
    {
        // Create items with duplicate first letters to test secondary sort
        var data = new List<TestAggregate>
        {
            new TestAggregate { id = Guid.NewGuid(), name = "B2" },
            new TestAggregate { id = Guid.NewGuid(), name = "A1" },
            new TestAggregate { id = Guid.NewGuid(), name = "B1" },
            new TestAggregate { id = Guid.NewGuid(), name = "A2" },
        };

        var query = CreateQuery(data);
        var results = await query
            .OrderBy(x => x.name.Substring(0, 1))
            .ThenBy(x => x.name)
            .ReadAllAsync();

        Assert.Equal("A1", results[0].name);
        Assert.Equal("A2", results[1].name);
        Assert.Equal("B1", results[2].name);
        Assert.Equal("B2", results[3].name);
    }

    [Fact]
    public async Task ThenByDescending_AppliesSecondaryDescendingSort()
    {
        var data = new List<TestAggregate>
        {
            new TestAggregate { id = Guid.NewGuid(), name = "B2" },
            new TestAggregate { id = Guid.NewGuid(), name = "A1" },
            new TestAggregate { id = Guid.NewGuid(), name = "B1" },
            new TestAggregate { id = Guid.NewGuid(), name = "A2" },
        };

        var query = CreateQuery(data);
        var results = await query
            .OrderBy(x => x.name.Substring(0, 1))
            .ThenByDescending(x => x.name)
            .ReadAllAsync();

        Assert.Equal("A2", results[0].name);
        Assert.Equal("A1", results[1].name);
        Assert.Equal("B2", results[2].name);
        Assert.Equal("B1", results[3].name);
    }

    [Fact]
    public async Task Take_LimitsResults()
    {
        var query = CreateQuery();
        var results = await query.OrderBy(x => x.name).Take(2).ReadAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("Alpha", results[0].name);
        Assert.Equal("Bravo", results[1].name);
    }

    [Fact]
    public async Task Skip_SkipsResults()
    {
        var query = CreateQuery();
        var results = await query.OrderBy(x => x.name).Skip(3).ReadAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("Delta", results[0].name);
        Assert.Equal("Echo", results[1].name);
    }

    [Fact]
    public async Task Distinct_RemovesDuplicates()
    {
        var data = new List<TestAggregate>
        {
            new TestAggregate { id = Guid.NewGuid(), name = "Alpha" },
            new TestAggregate { id = Guid.NewGuid(), name = "Alpha" },
            new TestAggregate { id = Guid.NewGuid(), name = "Bravo" },
        };

        var query = CreateQuery(data);
        var names = await query.Select(x => x.name).Distinct().ReadAllAsync();

        Assert.Equal(2, names.Count);
        Assert.Contains("Alpha", names);
        Assert.Contains("Bravo", names);
    }

    [Fact]
    public async Task ChainedOperations_WorkTogether()
    {
        var query = CreateQuery();
        var results = await query
            .Where(x => x.name != "Echo")
            .OrderByDescending(x => x.name)
            .Take(2)
            .ReadAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal("Delta", results[0].name);
        Assert.Equal("Charlie", results[1].name);
    }

    [Fact]
    public void AsQueryable_ReturnsUnderlyingQueryable()
    {
        var data = CreateTestData().AsQueryable();
        var query = new RetryableQuery<TestAggregate>(data, new RetryOptions(), InMemoryQueryExecutor.Default);

        Assert.Same(data, query.AsQueryable());
    }

    #endregion

    #region Terminal Methods - Basic

    [Fact]
    public async Task ReadAllAsync_ReturnsAllItems()
    {
        var data = CreateTestData();
        var query = CreateQuery(data);
        var results = await query.ReadAllAsync();

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task ReadAllAsync_EmptyQuery_ReturnsEmptyList()
    {
        var query = CreateQuery(new List<TestAggregate>());
        var results = await query.ReadAllAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsFirstMatch()
    {
        var query = CreateQuery();
        var result = await query.Where(x => x.name == "Charlie").FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.Equal("Charlie", result!.name);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_NoMatch_ReturnsNull()
    {
        var query = CreateQuery();
        var result = await query.Where(x => x.name == "NonExistent").FirstOrDefaultAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        var query = CreateQuery();
        var count = await query.CountAsync();

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task CountAsync_WithFilter_ReturnsFilteredCount()
    {
        var query = CreateQuery();
        var count = await query.Where(x => x.name.StartsWith("A") || x.name.StartsWith("B")).CountAsync();

        Assert.Equal(2, count);
    }

    #endregion

    #region FirstOrNewAsync Extension

    [Fact]
    public async Task FirstOrNewAsync_WithMatch_ReturnsMatch()
    {
        var query = CreateQuery();
        var result = await query.Where(x => x.name == "Delta").FirstOrNewAsync();

        Assert.Equal("Delta", result.name);
    }

    [Fact]
    public async Task FirstOrNewAsync_NoMatch_ReturnsNewInstance()
    {
        var query = CreateQuery();
        var result = await query.Where(x => x.name == "NonExistent").FirstOrNewAsync();

        Assert.NotNull(result);
        Assert.Equal("Test1", result.name); // TestAggregate default name
    }

    #endregion

    #region 429 Retry - Succeeds After Retry

    [Fact]
    public async Task ReadAllAsync_429ThenSuccess_RetriesAndReturns()
    {
        var executor = new ThrowThenSucceedQueryExecutor(failCount: 1);
        var options = new RetryOptions { MaxRetries = 3 };
        var data = CreateTestData();
        var query = new RetryableQuery<TestAggregate>(data.AsQueryable(), options, executor);

        var results = await query.ReadAllAsync();

        Assert.Equal(5, results.Count);
        Assert.Equal(2, executor.CallCount); // 1 fail + 1 success
    }

    [Fact]
    public async Task FirstOrDefaultAsync_429ThenSuccess_RetriesAndReturns()
    {
        var executor = new ThrowThenSucceedQueryExecutor(failCount: 2);
        var options = new RetryOptions { MaxRetries = 3 };
        var data = CreateTestData();
        var query = new RetryableQuery<TestAggregate>(data.AsQueryable(), options, executor);

        var result = await query.FirstOrDefaultAsync();

        Assert.NotNull(result);
        Assert.Equal(3, executor.CallCount); // 2 fails + 1 success
    }

    [Fact]
    public async Task CountAsync_429ThenSuccess_RetriesAndReturns()
    {
        var executor = new ThrowThenSucceedQueryExecutor(failCount: 1);
        var options = new RetryOptions { MaxRetries = 3 };
        var data = CreateTestData();
        var query = new RetryableQuery<TestAggregate>(data.AsQueryable(), options, executor);

        var count = await query.CountAsync();

        Assert.Equal(5, count);
        Assert.Equal(2, executor.CallCount);
    }

    #endregion

    #region 429 Retry - Exhausted

    [Fact]
    public async Task ReadAllAsync_429Exhausted_ThrowsCosmosException()
    {
        var executor = new AlwaysThrow429QueryExecutor();
        var options = new RetryOptions { MaxRetries = 2 };
        var query = new RetryableQuery<TestAggregate>(CreateTestData().AsQueryable(), options, executor);

        var ex = await Assert.ThrowsAsync<CosmosException>(() => query.ReadAllAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(3, executor.CallCount); // initial + 2 retries
    }

    [Fact]
    public async Task FirstOrDefaultAsync_429Exhausted_ThrowsCosmosException()
    {
        var executor = new AlwaysThrow429QueryExecutor();
        var options = new RetryOptions { MaxRetries = 1 };
        var query = new RetryableQuery<TestAggregate>(CreateTestData().AsQueryable(), options, executor);

        var ex = await Assert.ThrowsAsync<CosmosException>(() => query.FirstOrDefaultAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(2, executor.CallCount); // initial + 1 retry
    }

    [Fact]
    public async Task CountAsync_429Exhausted_ThrowsCosmosException()
    {
        var executor = new AlwaysThrow429QueryExecutor();
        var options = new RetryOptions { MaxRetries = 0 };
        var query = new RetryableQuery<TestAggregate>(CreateTestData().AsQueryable(), options, executor);

        var ex = await Assert.ThrowsAsync<CosmosException>(() => query.CountAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(1, executor.CallCount); // just the initial attempt
    }

    #endregion

    #region InMemoryQueryExecutor

    [Fact]
    public async Task InMemoryQueryExecutor_ReadAllAsync_Works()
    {
        var data = CreateTestData().AsQueryable();
        var results = await InMemoryQueryExecutor.Default.ReadAllAsync(data);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task InMemoryQueryExecutor_FirstOrDefaultAsync_Works()
    {
        var data = CreateTestData().AsQueryable().Where(x => x.name == "Bravo");
        var result = await InMemoryQueryExecutor.Default.FirstOrDefaultAsync(data);

        Assert.NotNull(result);
        Assert.Equal("Bravo", result!.name);
    }

    [Fact]
    public async Task InMemoryQueryExecutor_FirstOrNewAsync_NoMatch_ReturnsNew()
    {
        var data = Enumerable.Empty<TestAggregate>().AsQueryable();
        var result = await InMemoryQueryExecutor.Default.FirstOrNewAsync(data);

        Assert.NotNull(result);
        Assert.Equal("Test1", result.name); // default
    }

    [Fact]
    public async Task InMemoryQueryExecutor_CountAsync_Works()
    {
        var data = CreateTestData().AsQueryable().Where(x => x.name.StartsWith("A"));
        var count = await InMemoryQueryExecutor.Default.CountAsync(data);

        Assert.Equal(1, count);
    }

    #endregion

    #region MockRetryableContainer Query Methods

    [Fact]
    public async Task MockRetryableContainer_FilteredQuery_String_ReturnsRetryableQuery()
    {
        var item = new TestAggregate { id = Guid.NewGuid(), name = "MockItem" };
        var mock = new MockRetryableContainer<TestAggregate>(applyResult: item);

        var results = await mock.FilteredQuery<TestAggregate>("partition1").ReadAllAsync();

        Assert.Single(results);
        Assert.Equal("MockItem", results[0].name);
    }

    [Fact]
    public async Task MockRetryableContainer_FilteredQuery_PartitionKey_ReturnsRetryableQuery()
    {
        var item = new TestAggregate { id = Guid.NewGuid(), name = "Test" };
        var mock = new MockRetryableContainer<TestAggregate>(applyResult: item);

        var results = await mock.FilteredQuery<TestAggregate>(new PartitionKey("pk")).ReadAllAsync();

        Assert.Single(results);
    }

    [Fact]
    public async Task MockRetryableContainer_FilteredQuery_WithFilter_Filters()
    {
        var item = new TestAggregate { id = Guid.NewGuid(), name = "Included" };
        var mock = new MockRetryableContainer<TestAggregate>(applyResult: item);

        var results = await mock
            .FilteredQuery<TestAggregate>("pk", x => x.name == "NonExistent")
            .ReadAllAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task MockRetryableContainer_GetItemLinqQueryable_ReturnsRetryableQuery()
    {
        var item = new TestAggregate { id = Guid.NewGuid(), name = "QueryItem" };
        var mock = new MockRetryableContainer<TestAggregate>(applyResult: item);

        var results = await mock.GetItemLinqQueryable<TestAggregate>().ReadAllAsync();

        Assert.Single(results);
        Assert.Equal("QueryItem", results[0].name);
    }

    [Fact]
    public async Task MockRetryableContainer_FilteredQuery_DifferentType_ReturnsEmpty()
    {
        var item = new TestAggregate { id = Guid.NewGuid(), name = "NotProjection" };
        var mock = new MockRetryableContainer<TestAggregate>(applyResult: item);

        var results = await mock.FilteredQuery<TestProjection>(new PartitionKey("pk")).ReadAllAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task MockRetryableContainer_NullApplyResult_QueryReturnsEmpty()
    {
        var mock = new MockRetryableContainer<TestAggregate>();

        var results = await mock.GetItemLinqQueryable<TestAggregate>().ReadAllAsync();

        Assert.Empty(results);
    }

    #endregion

    #region WithRetry No-Arg Extension

    [Fact]
    public void WithRetry_NoArgs_CreatesRetryableContainerWithDefaults()
    {
        var mockContainer = new Moq.Mock<Container>();
        var retryable = mockContainer.Object.WithRetry();

        Assert.NotNull(retryable);
        Assert.IsType<RetryableContainer>(retryable);
        Assert.NotNull(retryable.Options);
    }

    #endregion

    #region Retry Logging

    [Fact]
    public async Task ReadAllAsync_429Retry_LogsRetryAttempts()
    {
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger>();
        var executor = new ThrowThenSucceedQueryExecutor(failCount: 1);
        var options = new RetryOptions
        {
            MaxRetries = 3,
            LogRetries = true,
            Logger = mockLogger.Object
        };
        var query = new RetryableQuery<TestAggregate>(CreateTestData().AsQueryable(), options, executor);

        await query.ReadAllAsync();

        mockLogger.Verify(
            l => l.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                Moq.It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                Moq.It.Is<Moq.It.IsAnyType>((v, t) => v.ToString()!.Contains("429") && v.ToString()!.Contains("ReadAllAsync")),
                Moq.It.IsAny<Exception?>(),
                Moq.It.IsAny<Func<Moq.It.IsAnyType, Exception?, string>>()),
            Moq.Times.Once());
    }

    #endregion
}
