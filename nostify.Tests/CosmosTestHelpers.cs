using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Azure.Cosmos;
using Moq;

namespace nostify.Tests;

/// <summary>
/// Provides helper methods for mocking Cosmos DB components in tests.
/// </summary>
public static class CosmosTestHelpers
{
    /// <summary>
    /// Creates a mocked Cosmos DB Container with pre-configured query responses that evaluates queries against the provided items.
    /// </summary>
    /// <typeparam name="T">The type of items to be returned by the container queries.</typeparam>
    /// <param name="items">The list of items to query against.</param>
    /// <returns>A mocked Container object configured to execute queries against the items list.</returns>
    public static Mock<Container> CreateMockContainer<T>(List<T> items) where T : class
    {
        var mockContainer = new Mock<Container>();

        // Setup for COUNT queries
        mockContainer.Setup(x => x.GetItemQueryIterator<int>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>()))
            .Returns<QueryDefinition, string, QueryRequestOptions>((queryDef, continuationToken, options) =>
            {
                var filteredCount = FilterItems(items, queryDef).Count;
                
                var mockCountResponse = new Mock<FeedResponse<int>>();
                mockCountResponse.Setup(x => x.GetEnumerator()).Returns(new List<int> { filteredCount }.GetEnumerator());

                var mockCountIterator = new Mock<FeedIterator<int>>();
                mockCountIterator.SetupSequence(x => x.HasMoreResults)
                    .Returns(true)
                    .Returns(false);
                mockCountIterator.Setup(x => x.ReadNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(mockCountResponse.Object);

                return mockCountIterator.Object;
            });

        // Setup for item queries
        mockContainer.Setup(x => x.GetItemQueryIterator<T>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>()))
            .Returns<QueryDefinition, string, QueryRequestOptions>((queryDef, continuationToken, options) =>
            {
                var filteredItems = FilterItems(items, queryDef);
                
                var mockItemResponse = new Mock<FeedResponse<T>>();
                mockItemResponse.Setup(x => x.GetEnumerator()).Returns(filteredItems.GetEnumerator());

                var mockItemIterator = new Mock<FeedIterator<T>>();
                mockItemIterator.SetupSequence(x => x.HasMoreResults)
                    .Returns(true)
                    .Returns(false);
                mockItemIterator.Setup(x => x.ReadNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(mockItemResponse.Object);

                return mockItemIterator.Object;
            });

        return mockContainer;
    }

    /// <summary>
    /// Filters items based on the query definition by parsing basic WHERE clauses.
    /// This is a simplified query parser that handles common patterns used in PagedQuery.
    /// </summary>
    private static List<T> FilterItems<T>(List<T> items, QueryDefinition queryDef) where T : class
    {
        // Access the query text and parameters through reflection
        var queryTextProperty = queryDef.GetType().GetProperty("QueryText");
        if (queryTextProperty == null) return items;
        
        var queryText = queryTextProperty.GetValue(queryDef) as string;
        if (string.IsNullOrEmpty(queryText)) return items;

        // Extract parameters from QueryDefinition using reflection
        var parameters = ExtractParameters(queryDef);
        
        var resultList = items.ToList();

        // Parse and apply partition key filter
        var partitionKeyMatch = Regex.Match(queryText, @"c\.(\w+)\s*=\s*@partitionKeyValue");
        if (partitionKeyMatch.Success && parameters.ContainsKey("@partitionKeyValue"))
        {
            var propertyName = partitionKeyMatch.Groups[1].Value;
            var propertyValue = parameters["@partitionKeyValue"];
            resultList = resultList.Where(item =>
            {
                var itemValue = GetPropertyValue(item, propertyName);
                return itemValue != null && itemValue.Equals(propertyValue);
            }).ToList();
        }

        // Parse and apply additional filters (AND c.propertyName = @filterValueN)
        var filterMatches = Regex.Matches(queryText, @"AND\s+c\.(\w+)\s*=\s*@(filterValue\d+)");
        foreach (System.Text.RegularExpressions.Match match in filterMatches)
        {
            var propertyName = match.Groups[1].Value;
            var paramName = "@" + match.Groups[2].Value;
            
            if (parameters.ContainsKey(paramName))
            {
                var filterValue = parameters[paramName]?.ToString();
                resultList = resultList.Where(item =>
                {
                    var propValue = GetPropertyValue(item, propertyName);
                    if (propValue == null) return false;
                    return propValue.ToString() == filterValue;
                }).ToList();
            }
        }

        // Parse and apply ORDER BY
        var orderByMatch = Regex.Match(queryText, @"ORDER BY\s+c\.(\w+)\s+(ASC|DESC)", RegexOptions.IgnoreCase);
        if (orderByMatch.Success)
        {
            var sortProperty = orderByMatch.Groups[1].Value;
            var sortDirection = orderByMatch.Groups[2].Value.ToUpper();
            
            resultList = sortDirection == "DESC"
                ? resultList.OrderByDescending(item => GetPropertyValue(item, sortProperty)).ToList()
                : resultList.OrderBy(item => GetPropertyValue(item, sortProperty)).ToList();
        }

        // Parse and apply OFFSET/LIMIT
        var offsetLimitMatch = Regex.Match(queryText, @"OFFSET\s+@offset\s+LIMIT\s+@limit", RegexOptions.IgnoreCase);
        if (offsetLimitMatch.Success)
        {
            var offset = parameters.ContainsKey("@offset") ? Convert.ToInt32(parameters["@offset"]) : 0;
            var limit = parameters.ContainsKey("@limit") ? Convert.ToInt32(parameters["@limit"]) : int.MaxValue;
            
            resultList = resultList.Skip(offset).Take(limit).ToList();
        }

        return resultList;
    }

    /// <summary>
    /// Extracts parameters from a QueryDefinition using reflection.
    /// </summary>
    private static Dictionary<string, object?> ExtractParameters(QueryDefinition queryDef)
    {
        var parameters = new Dictionary<string, object?>();
        
        // Access the internal parameters collection via reflection
        var parametersField = queryDef.GetType().GetField("parameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (parametersField == null)
        {
            // Try property instead
            var parametersProperty = queryDef.GetType().GetProperty("Parameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (parametersProperty != null)
            {
                var paramList = parametersProperty.GetValue(queryDef);
                if (paramList != null)
                {
                    // Parameters might be a List<(string, object)> or similar
                    var listType = paramList.GetType();
                    if (listType.IsGenericType)
                    {
                        var enumerator = (paramList as System.Collections.IEnumerable)?.GetEnumerator();
                        if (enumerator != null)
                        {
                            while (enumerator.MoveNext())
                            {
                                var item = enumerator.Current;
                                if (item == null) continue;
                                
                                var itemType = item.GetType();
                                
                                // Try to get Name and Value properties
                                var nameProperty = itemType.GetProperty("Name");
                                var valueProperty = itemType.GetProperty("Value");
                                
                                if (nameProperty != null && valueProperty != null)
                                {
                                    var name = nameProperty.GetValue(item) as string;
                                    var value = valueProperty.GetValue(item);
                                    if (name != null)
                                    {
                                        parameters[name] = value;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        return parameters;
    }

    /// <summary>
    /// Gets a property value from an object by property name.
    /// </summary>
    private static object? GetPropertyValue(object obj, string propertyName)
    {
        var property = obj.GetType().GetProperty(propertyName, 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        return property?.GetValue(obj);
    }

    /// <summary>
    /// Creates a mocked Cosmos DB Container specifically for Event queries used by ExternalDataEvent.
    /// This handles LINQ queries via GetItemLinqQueryable.
    /// </summary>
    /// <param name="events">The list of events to be returned by the container queries.</param>
    /// <returns>A mocked Container object configured to execute LINQ queries against the events list.</returns>
    public static Mock<Container> CreateMockContainerWithEvents(List<Event> events)
    {
        var mockContainer = new Mock<Container>();
        
        // Setup GetItemLinqQueryable to return a mock that works with LINQ
        // The actual code calls GetItemLinqQueryable<Event>() with no arguments
        mockContainer.Setup(x => x.GetItemLinqQueryable<Event>(
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>(),
                It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(new MockOrderedQueryable<Event>(events));
        
        return mockContainer;
    }
}

/// <summary>
/// Mock IOrderedQueryable implementation for testing Cosmos LINQ queries
/// </summary>
public class MockOrderedQueryable<T> : IOrderedQueryable<T>
{
    private readonly List<T> _items;
    private readonly IQueryable<T> _queryable;

    public MockOrderedQueryable(List<T> items)
    {
        _items = items;
        _queryable = items.AsQueryable();
    }

    public Type ElementType => _queryable.ElementType;
    public System.Linq.Expressions.Expression Expression => _queryable.Expression;
    public IQueryProvider Provider => new MockQueryProvider<T>(_items);

    public IEnumerator<T> GetEnumerator() => _queryable.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Mock IQueryProvider that enables LINQ operations on mock data
/// </summary>
public class MockQueryProvider<T> : IQueryProvider
{
    private readonly List<T> _items;

    public MockQueryProvider(List<T> items)
    {
        _items = items;
    }

    public IQueryable CreateQuery(System.Linq.Expressions.Expression expression)
    {
        return new MockOrderedQueryable<T>(_items);
    }

    public IQueryable<TElement> CreateQuery<TElement>(System.Linq.Expressions.Expression expression)
    {
        // Execute the expression against the items to produce a new queryable
        var result = System.Linq.Expressions.Expression.Lambda(expression).Compile().DynamicInvoke();
        if (result is IEnumerable<TElement> enumerable)
        {
            return enumerable.AsQueryable();
        }
        return new List<TElement>().AsQueryable();
    }

    public object? Execute(System.Linq.Expressions.Expression expression)
    {
        return System.Linq.Expressions.Expression.Lambda(expression).Compile().DynamicInvoke();
    }

    public TResult Execute<TResult>(System.Linq.Expressions.Expression expression)
    {
        var result = Execute(expression);
        return result == null ? default! : (TResult)result;
    }
}

