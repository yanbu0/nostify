// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Threading.Tasks;
// using Microsoft.Azure.Cosmos;
// using Moq;
// using Xunit;
// using nostify;

// namespace nostify.Tests;

// public class QueryExtensionsTests
// {
//     public class TestEntity
//     {
//         public Guid Id { get; set; }
//         public string? Name { get; set; }
//         public int Value { get; set; }

//         public TestEntity()
//         {
//             Id = Guid.NewGuid();
//         }

//         public TestEntity(string name, int value) : this()
//         {
//             Name = name;
//             Value = value;
//         }
//     }

//     [Fact]
//     public async Task ReadFeedIteratorAsync_WithEmptyResults_ShouldReturnEmptyList()
//     {
//         // Arrange
//         var mockFeedIterator = new Mock<FeedIterator<TestEntity>>();
//         mockFeedIterator.SetupGet(x => x.HasMoreResults).Returns(false);

//         // Act
//         var result = await mockFeedIterator.Object.ReadFeedIteratorAsync();

//         // Assert
//         Assert.NotNull(result);
//         Assert.Empty(result);
//     }

//     [Fact]
//     public async Task ReadFeedIteratorAsync_WithSingleBatch_ShouldReturnAllItems()
//     {
//         // Arrange
//         var testEntities = new List<TestEntity>
//         {
//             new TestEntity("Entity1", 1),
//             new TestEntity("Entity2", 2),
//             new TestEntity("Entity3", 3)
//         };

//         var mockFeedResponse = new Mock<FeedResponse<TestEntity>>();
//         mockFeedResponse.Setup(x => x.GetEnumerator()).Returns(testEntities.GetEnumerator());

//         var mockFeedIterator = new Mock<FeedIterator<TestEntity>>();
//         mockFeedIterator.SetupSequence(x => x.HasMoreResults)
//             .Returns(true)   // First call - has results
//             .Returns(false); // Second call - no more results
//         mockFeedIterator.Setup(x => x.ReadNextAsync())
//             .Returns(Task.FromResult(mockFeedResponse.Object));

//         // Act
//         var result = await mockFeedIterator.Object.ReadFeedIteratorAsync();

//         // Assert
//         Assert.NotNull(result);
//         Assert.Equal(3, result.Count);
//         Assert.Equal("Entity1", result[0].Name);
//         Assert.Equal("Entity2", result[1].Name);
//         Assert.Equal("Entity3", result[2].Name);
//     }

//     [Fact]
//     public async Task ReadFeedIteratorAsync_WithException_ShouldPropagateException()
//     {
//         // Arrange
//         var mockFeedIterator = new Mock<FeedIterator<TestEntity>>();
//         mockFeedIterator.SetupGet(x => x.HasMoreResults).Returns(true);
//         mockFeedIterator.Setup(x => x.ReadNextAsync())
//             .Returns(Task.FromException<FeedResponse<TestEntity>>(new CosmosException("Test exception", System.Net.HttpStatusCode.InternalServerError, 0, "test", 1.0)));

//         // Act & Assert
//         await Assert.ThrowsAsync<CosmosException>(async () => 
//             await mockFeedIterator.Object.ReadFeedIteratorAsync());
//     }

//     [Fact]
//     public void ReadFeedIteratorAsync_ShouldBeExtensionMethod()
//     {
//         // This test verifies that the method exists as an extension method
//         // and can be called on FeedIterator<T> instances

//         // Arrange
//         var mockFeedIterator = new Mock<FeedIterator<TestEntity>>();

//         // Act & Assert
//         // The fact that this compiles proves the extension method exists
//         var task = mockFeedIterator.Object.ReadFeedIteratorAsync();
//         Assert.NotNull(task);
//     }

//     // Simplified tests for the other extension methods since they depend on Cosmos DB specifics
//     [Fact]
//     public void QueryExtensionMethods_ShouldExistAndBeAccessible()
//     {
//         // This test verifies the extension methods exist by checking their method signatures
//         var extensionMethods = typeof(QueryExtensions).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        
//         var firstOrNewMethod = extensionMethods.FirstOrDefault(m => m.Name == "FirstOrNewAsync");
//         var firstOrDefaultMethod = extensionMethods.FirstOrDefault(m => m.Name == "FirstOrDefaultAsync");
//         var readAllMethod = extensionMethods.FirstOrDefault(m => m.Name == "ReadAllAsync");
//         var readFeedIteratorMethod = extensionMethods.FirstOrDefault(m => m.Name == "ReadFeedIteratorAsync");

//         Assert.NotNull(firstOrNewMethod);
//         Assert.NotNull(firstOrDefaultMethod);
//         Assert.NotNull(readAllMethod);
//         Assert.NotNull(readFeedIteratorMethod);

//         // Verify they are generic methods
//         Assert.True(firstOrNewMethod.IsGenericMethodDefinition);
//         Assert.True(firstOrDefaultMethod.IsGenericMethodDefinition);
//         Assert.True(readAllMethod.IsGenericMethodDefinition);
//         Assert.True(readFeedIteratorMethod.IsGenericMethodDefinition);
//     }

//     [Fact]
//     public void QueryExtensions_ShouldBeStaticClass()
//     {
//         // Verify QueryExtensions is a static class
//         var type = typeof(QueryExtensions);
//         Assert.True(type.IsSealed);
//         Assert.True(type.IsAbstract);
//         Assert.True(type.IsClass);
//     }
// }
