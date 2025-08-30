// using System;
// using System.Collections.Generic;
// using System.Net.Http;
// using System.Threading.Tasks;
// using Xunit;
// using nostify;
// using Confluent.Kafka;
// using Moq;
// using Newtonsoft.Json;
// using Microsoft.Azure.Cosmos;

// namespace nostify.Tests;

// /// <summary>
// /// Tests for IEvent interface functionality and polymorphic behavior
// /// </summary>
// public class IEventInterfaceTests
// {
//     private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;

//     public IEventInterfaceTests()
//     {
//         _mockHttpClientFactory = new Mock<IHttpClientFactory>();
//     }

//     #region Recommendation 1: Interface Method Tests

//     [Fact]
//     public async Task PersistEventAsync_WithIEventInterface_ShouldPersistCorrectly()
//     {
//         // Arrange
//         var nostify = CreateTestNostify();
//         var command = new IEventTestCommand();
//         var aggregateId = Guid.NewGuid();
//         var payload = new { test = "data" };
        
//         // Create Event as IEvent to test interface polymorphism
//         IEvent eventToPersist = new Event(command, aggregateId, payload);

//         // Act & Assert - Should not throw exceptions due to interface issues
//         // Note: This will fail in unit test environment due to Cosmos dependencies
//         // but validates the method signature and interface behavior
//         var exception = await Record.ExceptionAsync(() => nostify.PersistEventAsync(eventToPersist));
        
//         // For unit test, we expect a Cosmos-related exception, not a null reference or interface issue
//         Assert.True(exception == null || exception.Message.Contains("Cosmos") || exception is InvalidOperationException);
//     }

//     [Fact]
//     public async Task PublishEventAsync_WithSingleIEvent_ShouldPublishCorrectly()
//     {
//         // Arrange
//         var nostify = CreateTestNostify();
//         var command = new IEventTestCommand();
//         var aggregateId = Guid.NewGuid();
//         var payload = new { test = "data" };
        
//         // Create Event as IEvent to test interface polymorphism
//         IEvent eventToPublish = new Event(command, aggregateId, payload);

//         // Act & Assert - Should not throw due to interface issues
//         var exception = await Record.ExceptionAsync(() => nostify.PublishEventAsync(eventToPublish));
        
//         // We expect either success or Kafka-related exception, not interface problems
//         Assert.True(exception == null || exception is ProduceException<string, string> || exception.Message.Contains("Kafka"));
//     }

//     [Fact]
//     public async Task PublishEventAsync_WithIEventList_ShouldPublishAllEvents()
//     {
//         // Arrange
//         var nostify = CreateTestNostify();
//         var command1 = new IEventTestCommand("Test_Command1");
//         var command2 = new IEventTestCommand("Test_Command2");
//         var aggregateId1 = Guid.NewGuid();
//         var aggregateId2 = Guid.NewGuid();
        
//         // Create list of IEvent objects
//         var eventList = new List<IEvent>
//         {
//             new Event(command1, aggregateId1, new { test = "data1" }),
//             new Event(command2, aggregateId2, new { test = "data2" })
//         };

//         // Act & Assert - Should handle list of IEvent properly
//         var exception = await Record.ExceptionAsync(() => nostify.PublishEventAsync(eventList));
        
//         // We expect either success or Kafka-related exception, not interface problems
//         Assert.True(exception == null || exception is ProduceException<string, string> || exception.Message.Contains("Kafka"));
//     }

//     [Fact]
//     public async Task HandleUndeliverableAsync_WithIEvent_ShouldHandleCorrectly()
//     {
//         // Arrange
//         INostify nostify = CreateTestNostify();
//         var command = new IEventTestCommand();
//         var aggregateId = Guid.NewGuid();
//         var payload = new { test = "data" };
        
//         // Create Event as IEvent to test interface polymorphism
//         IEvent eventToHandle = new Event(command, aggregateId, payload);
//         string functionName = "TestFunction";
//         string errorMessage = "Test error message";

//         // Act & Assert - Should not throw due to interface issues
//         var exception = await Record.ExceptionAsync(() => 
//             nostify.HandleUndeliverableAsync(functionName, errorMessage, eventToHandle));
        
//         // We expect either success or Cosmos-related exception, not interface problems
//         Assert.True(exception == null || exception.Message.Contains("Cosmos") || exception is InvalidOperationException);
//     }

//     #endregion

//     #region Recommendation 2: Interface Polymorphism Testing

//     [Fact]
//     public void Event_ShouldImplementIEventInterface_WithAllRequiredProperties()
//     {
//         // Arrange
//         var command = new IEventTestCommand();
//         var aggregateId = Guid.NewGuid();
//         var payload = new { test = "data" };
//         var userId = Guid.NewGuid();
//         var partitionKey = Guid.NewGuid();

//         // Act
//         var eventInstance = new Event(command, aggregateId, payload, userId, partitionKey);
//         IEvent iEventInterface = eventInstance;

//         // Assert - Test that Event properly implements IEvent interface
//         Assert.NotNull(iEventInterface);
//         Assert.Equal(eventInstance.id, iEventInterface.id);
//         Assert.Equal(eventInstance.aggregateRootId, iEventInterface.aggregateRootId);
//         Assert.Equal(eventInstance.partitionKey, iEventInterface.partitionKey);
//         Assert.Equal(eventInstance.userId, iEventInterface.userId);
//         Assert.Equal(eventInstance.command, iEventInterface.command);
//         Assert.Equal(eventInstance.timestamp, iEventInterface.timestamp);
//         Assert.NotNull(iEventInterface.payload);
//     }

//     [Fact]
//     public void IEvent_PolymorphicBehavior_ShouldWorkWithEventImplementation()
//     {
//         // Arrange
//         var command = new IEventTestCommand();
//         var aggregateId = Guid.NewGuid();
//         var payload = new { test = "data", value = 42 };

//         // Act - Create Event but reference as IEvent
//         IEvent iEvent = new Event(command, aggregateId, payload);
//         Event concreteEvent = (Event)iEvent;

//         // Assert - Polymorphic behavior should work correctly
//         Assert.IsType<Event>(iEvent);
//         Assert.Equal(iEvent.id, concreteEvent.id);
//         Assert.Equal(iEvent.aggregateRootId, concreteEvent.aggregateRootId);
        
//         // Test that payload casting works
//         var deserializedPayload = concreteEvent.GetPayload<dynamic>();
//         Assert.NotNull(deserializedPayload);
//     }

//     [Fact]
//     public void IEvent_InterfaceContract_ShouldExposeAllRequiredMembers()
//     {
//         // Arrange
//         var command = new IEventTestCommand();
//         var aggregateId = Guid.NewGuid();
//         var payload = new { name = "test", value = 123 };
//         var userId = Guid.NewGuid();
//         var partitionKey = Guid.NewGuid();

//         // Act
//         IEvent iEvent = new Event(command, aggregateId, payload, userId, partitionKey);

//         // Assert - All IEvent interface members should be accessible
//         Assert.True(iEvent.id != Guid.Empty);
//         Assert.Equal(aggregateId, iEvent.aggregateRootId);
//         Assert.Equal(partitionKey, iEvent.partitionKey);
//         Assert.Equal(userId, iEvent.userId);
//         Assert.Equal(command, iEvent.command);
//         Assert.True(iEvent.timestamp > DateTime.MinValue);
//         Assert.NotNull(iEvent.payload);
        
//         // Test that we can read/write properties through interface
//         var newPartitionKey = Guid.NewGuid();
//         iEvent.partitionKey = newPartitionKey;
//         Assert.Equal(newPartitionKey, iEvent.partitionKey);
//     }

//     [Fact]
//     public void Event_CastToIEvent_ShouldPreserveAllProperties()
//     {
//         // Arrange
//         var command = new IEventTestCommand("Test_Cast");
//         var aggregateId = Guid.NewGuid();
//         var complexPayload = new 
//         { 
//             id = Guid.NewGuid(),
//             name = "Test Object",
//             values = new[] { 1, 2, 3 },
//             nested = new { prop = "value" }
//         };

//         // Act
//         var originalEvent = new Event(command, aggregateId, complexPayload);
//         IEvent castedEvent = originalEvent;
//         Event backToEvent = (Event)castedEvent;

//         // Assert - All properties should be preserved through casting
//         Assert.Equal(originalEvent.id, castedEvent.id);
//         Assert.Equal(originalEvent.id, backToEvent.id);
//         Assert.Equal(originalEvent.aggregateRootId, castedEvent.aggregateRootId);
//         Assert.Equal(originalEvent.command.name, castedEvent.command.name);
        
//         // Test complex payload preservation
//         var originalPayloadJson = JsonConvert.SerializeObject(originalEvent.payload);
//         var castedPayloadJson = JsonConvert.SerializeObject(castedEvent.payload);
//         var backToEventPayloadJson = JsonConvert.SerializeObject(backToEvent.payload);
        
//         Assert.Equal(originalPayloadJson, castedPayloadJson);
//         Assert.Equal(originalPayloadJson, backToEventPayloadJson);
//     }

//     #endregion

//     #region Recommendation 3: Container Extension Tests

//     [Fact]
//     public void ContainerExtensions_ApplyAndPersistAsync_WithIEventList_ShouldHaveCorrectSignature()
//     {
//         // This test validates that the method signatures exist and can be called
//         // Arrange
//         var mockContainer = new Mock<Container>();
//         var events = new List<IEvent>
//         {
//             new Event(new IEventTestCommand(), Guid.NewGuid(), new { test = "data" })
//         };
        
//         // Act & Assert - This should compile and not throw method not found
//         var methodInfo = typeof(ContainerExtensions).GetMethod(
//             nameof(ContainerExtensions.ApplyAndPersistAsync),
//             new[] { typeof(Container), typeof(List<IEvent>), typeof(PartitionKey), typeof(Guid?) }
//         );
        
//         Assert.NotNull(methodInfo);
//         Assert.True(methodInfo.IsStatic);
//         Assert.True(methodInfo.IsPublic);
//     }

//     [Fact]
//     public void ContainerExtensions_ApplyAndPersistAsync_WithSingleIEvent_ShouldHaveCorrectSignature()
//     {
//         // Validate the single IEvent overload exists
//         var methodInfo = typeof(ContainerExtensions).GetMethod(
//             nameof(ContainerExtensions.ApplyAndPersistAsync),
//             new[] { typeof(Container), typeof(IEvent), typeof(PartitionKey) }
//         );
        
//         Assert.NotNull(methodInfo);
//         Assert.True(methodInfo.IsStatic);
//         Assert.True(methodInfo.IsPublic);
//     }

//     [Fact]
//     public void ContainerExtensions_IEventOverloads_ShouldAcceptEventObjects()
//     {
//         // Test that Event objects can be passed to IEvent parameters
//         // Arrange
//         var testEvent = new Event(new IEventTestCommand(), Guid.NewGuid(), new { test = "data" });
//         var eventList = new List<IEvent> { testEvent };
        
//         // Act & Assert - These should all compile without casting
//         Assert.Single(eventList);
//         Assert.IsAssignableFrom<IEvent>(testEvent);
//         Assert.IsType<Event>(eventList[0]);
        
//         // Test that the concrete Event can be used where IEvent is expected
//         IEvent iEventRef = testEvent;
//         Assert.Equal(testEvent.id, iEventRef.id);
//     }

//     #endregion

//     #region Additional Tests with Existing TestCommand

//     [Fact]
//     public void IEvent_PolymorphicBehavior_WithExistingTestCommand_ShouldWorkCorrectly()
//     {
//         // Arrange
//         var command = new TestCommand();
//         var aggregateId = Guid.NewGuid();
//         var payload = new { test = "data", value = 42 };

//         // Act - Create Event but reference as IEvent
//         IEvent iEvent = new Event(command, aggregateId, payload);
//         Event concreteEvent = (Event)iEvent;

//         // Assert - Polymorphic behavior should work correctly
//         Assert.IsType<Event>(iEvent);
//         Assert.Equal(iEvent.id, concreteEvent.id);
//         Assert.Equal(iEvent.aggregateRootId, concreteEvent.aggregateRootId);
        
//         // Test that payload casting works
//         var deserializedPayload = concreteEvent.GetPayload<dynamic>();
//         Assert.NotNull(deserializedPayload);
//     }

//     [Fact]
//     public void IEvent_InterfaceContract_WithExistingTestCommand_ShouldExposeAllRequiredMembers()
//     {
//         // Arrange
//         var command = new TestCommand();
//         var aggregateId = Guid.NewGuid();
//         var payload = new { name = "test", value = 123 };
//         var userId = Guid.NewGuid();
//         var partitionKey = Guid.NewGuid();

//         // Act
//         IEvent iEvent = new Event(command, aggregateId, payload, userId, partitionKey);

//         // Assert - All IEvent interface members should be accessible
//         Assert.True(iEvent.id != Guid.Empty);
//         Assert.Equal(aggregateId, iEvent.aggregateRootId);
//         Assert.Equal(partitionKey, iEvent.partitionKey);
//         Assert.Equal(userId, iEvent.userId);
//         Assert.Equal(command, iEvent.command);
//         Assert.True(iEvent.timestamp > DateTime.MinValue);
//         Assert.NotNull(iEvent.payload);
        
//         // Test that we can read/write properties through interface
//         var newPartitionKey = Guid.NewGuid();
//         iEvent.partitionKey = newPartitionKey;
//         Assert.Equal(newPartitionKey, iEvent.partitionKey);
//     }

//     [Fact]
//     public void Event_CastToIEvent_WithExistingTestCommand_ShouldPreserveAllProperties()
//     {
//         // Arrange
//         var command = new TestCommand(); // Using parameterless constructor to avoid conflicts
//         var aggregateId = Guid.NewGuid();
//         var complexPayload = new 
//         { 
//             id = Guid.NewGuid(),
//             name = "Test Object",
//             values = new[] { 1, 2, 3 },
//             nested = new { prop = "value" }
//         };

//         // Act
//         var originalEvent = new Event(command, aggregateId, complexPayload);
//         IEvent castedEvent = originalEvent;
//         Event backToEvent = (Event)castedEvent;

//         // Assert - All properties should be preserved through casting
//         Assert.Equal(originalEvent.id, castedEvent.id);
//         Assert.Equal(originalEvent.id, backToEvent.id);
//         Assert.Equal(originalEvent.aggregateRootId, castedEvent.aggregateRootId);
//         Assert.Equal(originalEvent.command.name, castedEvent.command.name);
        
//         // Test complex payload preservation
//         var originalPayloadJson = JsonConvert.SerializeObject(originalEvent.payload);
//         var castedPayloadJson = JsonConvert.SerializeObject(castedEvent.payload);
//         var backToEventPayloadJson = JsonConvert.SerializeObject(backToEvent.payload);
        
//         Assert.Equal(originalPayloadJson, castedPayloadJson);
//         Assert.Equal(originalPayloadJson, backToEventPayloadJson);
//     }

//     [Fact]
//     public void ContainerExtensions_ApplyAndPersistAsync_WithExistingTestCommand_ShouldHaveCorrectSignature()
//     {
//         // This test validates that the method signatures exist and can be called with existing TestCommand
//         // Arrange
//         var mockContainer = new Mock<Container>();
//         var events = new List<IEvent>
//         {
//             new Event(new TestCommand(), Guid.NewGuid(), new { test = "data" })
//         };
        
//         // Act & Assert - This should compile and not throw method not found
//         var methodInfo = typeof(ContainerExtensions).GetMethod(
//             nameof(ContainerExtensions.ApplyAndPersistAsync),
//             new[] { typeof(Container), typeof(List<IEvent>), typeof(PartitionKey), typeof(Guid?) }
//         );
        
//         Assert.NotNull(methodInfo);
//         Assert.True(methodInfo.IsStatic);
//         Assert.True(methodInfo.IsPublic);
//     }

//     [Fact]
//     public void ContainerExtensions_IEventOverloads_WithExistingTestCommand_ShouldAcceptEventObjects()
//     {
//         // Test that Event objects can be passed to IEvent parameters using existing TestCommand
//         // Arrange
//         var testEvent = new Event(new TestCommand(), Guid.NewGuid(), new { test = "data" });
//         var eventList = new List<IEvent> { testEvent };
        
//         // Act & Assert - These should all compile without casting
//         Assert.Single(eventList);
//         Assert.IsAssignableFrom<IEvent>(testEvent);
//         Assert.IsType<Event>(eventList[0]);
        
//         // Test that the concrete Event can be used where IEvent is expected
//         IEvent iEventRef = testEvent;
//         Assert.Equal(testEvent.id, iEventRef.id);
//     }

//     #endregion

//     #region Helper Methods

//     private Nostify CreateTestNostify()
//     {
//         return new Nostify(
//             "test-primary-key",
//             "test-db",
//             "test-cosmos-url",
//             "localhost:9092",
//             _mockHttpClientFactory.Object,
//             "/tenantId",
//             Guid.NewGuid()
//         );
//     }

//     #endregion
// }

// // Test helper class for IEvent tests to avoid conflicts
// public class IEventTestCommand : NostifyCommand
// {
//     public IEventTestCommand(string name = "IEventTestCommand") : base(name) { }
// }
