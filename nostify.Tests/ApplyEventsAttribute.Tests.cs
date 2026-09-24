using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using nostify;
using Xunit;

namespace nostify.Tests
{
    /// <summary>
    /// Tests for the <see cref="ApplyEventsAttribute"/>-based dispatch and its interaction
    /// with the existing dynamic Apply(EventType, IEvent) overload-based dispatch.
    ///
    /// These tests follow the same EventType pattern as the generated command templates
    /// in templates/nostify/_ReplaceMe_/Aggregates/_ReplaceMe_/_ReplaceMe_Command.cs, using
    /// concrete Create_Order, Update_Order, etc. classes plus a static OrderCommand facade.
    /// </summary>
    public class ApplyEventsAttributeTests
    {
        #region Test helpers - EventType model

        /// <summary>
        /// Concrete event types plus a static command facade, mirroring the current templates.
        /// </summary>
        private sealed class Create_Order : EventType
        {
            public Create_Order() : base("Create_Order", isNew: true) { }
        }

        private sealed class Update_Order : EventType
        {
            public Update_Order() : base("Update_Order") { }
        }

        private sealed class Delete_Order : EventType
        {
            public Delete_Order() : base("Delete_Order", isNew: false, allowNullPayload: true) { }
        }

        private sealed class BulkCreate_Order : EventType
        {
            public BulkCreate_Order() : base("BulkCreate_Order", isNew: true) { }
        }

        private sealed class BulkUpdate_Order : EventType
        {
            public BulkUpdate_Order() : base("BulkUpdate_Order") { }
        }

        private sealed class BulkDelete_Order : EventType
        {
            public BulkDelete_Order() : base("BulkDelete_Order", isNew: false, allowNullPayload: true) { }
        }

        private sealed class Canonical_Order : EventType
        {
            public Canonical_Order() : base("Canonical_Order") { }
        }

        private static class OrderCommand
        {
            public static Create_Order Create => new Create_Order();
            public static Update_Order Update => new Update_Order();
            public static Delete_Order Delete => new Delete_Order();
            public static BulkCreate_Order BulkCreate => new BulkCreate_Order();
            public static BulkUpdate_Order BulkUpdate => new BulkUpdate_Order();
            public static BulkDelete_Order BulkDelete => new BulkDelete_Order();
        }

        /// <summary>
        /// Simple concrete Event that allows us to plug in any EventType instance.
        /// </summary>
        private sealed class TestEvent : Event
        {
            public TestEvent(EventType type)
            {
                eventType = type;
                aggregateRootId = Guid.NewGuid();
                id = Guid.NewGuid();
                //tenantId = Guid.NewGuid();
                payload = new { value = 1 };
            }
        }

        #endregion

        #region Test helpers - Aggregates

        /// <summary>
        /// Simple aggregate that uses only attribute-based Apply handlers.
        /// </summary>
        public class AttributeOnlyAggregate : NostifyObject, IAggregate
        {
            // IAggregate implementation (minimal for tests)
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            public int CreateHandledCount { get; private set; }
            public int UpdateHandledCount { get; private set; }
            public int MultiHandledCount { get; private set; }

            [ApplyEvents(typeof(Create_Order))]
            protected void ApplyCreate(IEvent e)
            {
                CreateHandledCount++;
            }

            [ApplyEvents(typeof(Update_Order))]
            protected void ApplyUpdate(IEvent e)
            {
                UpdateHandledCount++;
            }

            [ApplyEvents(typeof(BulkCreate_Order), typeof(BulkUpdate_Order))]
            protected void ApplyBulk(IEvent e)
            {
                MultiHandledCount++;
            }

        }

        /// <summary>
        /// Simple aggregate that uses string-based attribute Apply handlers.
        /// </summary>
        private class AttributeOnlyAggregateByName : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            public int CreateHandledCount { get; private set; }
            public int UpdateHandledCount { get; private set; }
            public int MultiHandledCount { get; private set; }

            [ApplyEvents("Create_Order")]
            protected void ApplyCreate(IEvent e)
            {
                CreateHandledCount++;
            }

            [ApplyEvents("Update_Order")]
            protected void ApplyUpdate(IEvent e)
            {
                UpdateHandledCount++;
            }

            [ApplyEvents("BulkCreate_Order", "BulkUpdate_Order")]
            protected void ApplyBulk(IEvent e)
            {
                MultiHandledCount++;
            }
        }

        /// <summary>
        /// Aggregate that supports both attribute-based handlers and dynamic overloads.
        /// </summary>
        private class HybridAggregate : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            public int AttributeHandledCount { get; private set; }
            public int DynamicHandledCount { get; private set; }

            [ApplyEvents(typeof(Create_Order))]
            protected void ApplyCreateAttribute(IEvent e)
            {
                AttributeHandledCount++;
            }

            /// <summary>
            /// Dynamic overload used as a fallback for events without attributes.
            /// </summary>
            protected override void Apply(EventType eventType, IEvent eventToApply)
            {
                DynamicHandledCount++;
            }
        }

        /// <summary>
        /// Aggregate that supports both string-based attribute handlers and dynamic overloads.
        /// </summary>
        private class HybridAggregateByName : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            public int AttributeHandledCount { get; private set; }
            public int DynamicHandledCount { get; private set; }

            [ApplyEvents("Create_Order")]
            protected void ApplyCreateAttribute(IEvent e)
            {
                AttributeHandledCount++;
            }

            /// <summary>
            /// Dynamic overload used as a fallback for events without attributes.
            /// </summary>
            protected override void Apply(EventType eventType, IEvent eventToApply)
            {
                DynamicHandledCount++;
            }
        }

        /// <summary>
        /// Aggregate used to verify conflict detection when multiple methods handle the same event type.
        /// </summary>
        private class ConflictingAggregate : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            [ApplyEvents(typeof(Create_Order))]
            protected void FirstHandler(IEvent e) { }

            [ApplyEvents(typeof(Create_Order))]
            protected void SecondHandler(IEvent e) { }

            protected override void Apply(EventType eventType, IEvent eventToApply)
            {
                // not used
            }
        }

        /// <summary>
        /// Aggregate used to verify conflict detection when multiple methods handle the same event type name.
        /// </summary>
        private class ConflictingAggregateByName : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            [ApplyEvents("Create_Order")]
            protected void FirstHandler(IEvent e) { }

            [ApplyEvents("Create_Order")]
            protected void SecondHandler(IEvent e) { }

            protected override void Apply(EventType eventType, IEvent eventToApply)
            {
                // not used
            }
        }

        /// <summary>
        /// Aggregate used to validate string-only event name handling without a local EventType class.
        /// </summary>
        private class StringOnlyAggregateByName : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";
            public int HandledCount { get; private set; }

            [ApplyEvents("DoesNotExist_Order")]
            protected void Handler(IEvent e)
            {
                HandledCount++;
            }
        }

        private class CanonicalInstanceAggregate : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";
            public int HandledCount { get; private set; }

            [ApplyEvents(typeof(Canonical_Order))]
            protected void Handle(IEvent e)
            {
                HandledCount++;
            }
        }

        private class InvalidCanonicalInstanceAggregate : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            [ApplyEvents(typeof(EventType))]
            protected void Handle(IEvent e)
            {
            }
        }

        /// <summary>
        /// Aggregate with an attributed handler whose return type violates the handler contract.
        /// </summary>
        private sealed class NonVoidHandlerAggregate : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            [ApplyEvents(typeof(Create_Order))]
            private int Handle(IEvent e) => 1;
        }

        /// <summary>
        /// Aggregate with an attributed CLR type that is not an EventType.
        /// </summary>
        private sealed class InvalidEventTypeHandlerAggregate : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            [ApplyEvents(typeof(string))]
            private void Handle(IEvent e)
            {
            }
        }

        /// <summary>
        /// Aggregate used for the performance comparison between attribute-based and dynamic dispatch.
        /// </summary>
        private class PerformanceAggregate : NostifyObject, IAggregate
        {
            public bool isDeleted { get; set; }
            public static string aggregateType => "Order";
            public static string currentStateContainerName => "OrderCurrentState";

            public int AttributeHandledCount { get; set; }
            public int DynamicHandledCount { get; set; }

            [ApplyEvents(typeof(Create_Order))]
            protected void ApplyViaAttribute(IEvent e)
            {
                AttributeHandledCount++;
            }

            protected override void Apply(EventType eventType, IEvent eventToApply)
            {
                DynamicHandledCount++;
            }
        }

        #endregion

        #region Tests

        [Fact]
        public void AttributeOnlyAggregate_UsesAttributeHandlersForMappedEvents()
        {
            // Arrange
            var aggregate = new AttributeOnlyAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var createEvent = new TestEvent(OrderCommand.Create);
            var updateEvent = new TestEvent(OrderCommand.Update);
            var bulkCreateEvent = new TestEvent(OrderCommand.BulkCreate);
            var bulkUpdateEvent = new TestEvent(OrderCommand.BulkUpdate);

            // Act
            aggregate.Apply(createEvent);
            aggregate.Apply(updateEvent);
            aggregate.Apply(bulkCreateEvent);
            aggregate.Apply(bulkUpdateEvent);

            // Assert
            Assert.Equal(1, aggregate.CreateHandledCount);
            Assert.Equal(1, aggregate.UpdateHandledCount);
            Assert.Equal(2, aggregate.MultiHandledCount); // BulkCreate + BulkUpdate
        }

        [Fact]
        public void TemplateStyleEventTypes_ExposeExpectedFlags()
        {
            Assert.IsType<Create_Order>(OrderCommand.Create);
            Assert.IsType<Update_Order>(OrderCommand.Update);

            Assert.True(OrderCommand.Create.isNew);
            Assert.False(OrderCommand.Create.allowNullPayload);

            Assert.False(OrderCommand.Update.isNew);
            Assert.False(OrderCommand.Update.allowNullPayload);

            Assert.False(OrderCommand.Delete.isNew);
            Assert.True(OrderCommand.Delete.allowNullPayload);

            Assert.True(OrderCommand.BulkCreate.isNew);
            Assert.False(OrderCommand.BulkCreate.allowNullPayload);

            Assert.False(OrderCommand.BulkUpdate.isNew);
            Assert.False(OrderCommand.BulkUpdate.allowNullPayload);

            Assert.False(OrderCommand.BulkDelete.isNew);
            Assert.True(OrderCommand.BulkDelete.allowNullPayload);
        }

        [Fact]
        public void EventTypeGetRequiredInstance_ResolvesInheritedGenericInstance()
        {
            var instance = EventType.GetRequiredInstance(typeof(Create_Order));

            Assert.IsType<Create_Order>(instance);
        }

        [Fact]
        public void AttributeOnlyAggregateByName_UsesAttributeHandlersForMappedEvents()
        {
            // Arrange
            var aggregate = new AttributeOnlyAggregateByName
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var createEvent = new TestEvent(OrderCommand.Create);
            var updateEvent = new TestEvent(OrderCommand.Update);
            var bulkCreateEvent = new TestEvent(OrderCommand.BulkCreate);
            var bulkUpdateEvent = new TestEvent(OrderCommand.BulkUpdate);

            // Act
            aggregate.Apply(createEvent);
            aggregate.Apply(updateEvent);
            aggregate.Apply(bulkCreateEvent);
            aggregate.Apply(bulkUpdateEvent);

            // Assert
            Assert.Equal(1, aggregate.CreateHandledCount);
            Assert.Equal(1, aggregate.UpdateHandledCount);
            Assert.Equal(2, aggregate.MultiHandledCount); // BulkCreate + BulkUpdate
        }

        [Fact]
        public void AttributeOnlyAggregateByName_MatchesByEventTypeNameAcrossDifferentClrTypes()
        {
            // Arrange
            var aggregate = new AttributeOnlyAggregateByName
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var createEvent = new TestEvent(new NostifyCommand("Create_Order", isNew: true));

            // Act
            aggregate.Apply(createEvent);

            // Assert
            Assert.Equal(1, aggregate.CreateHandledCount);
            Assert.Equal(0, aggregate.UpdateHandledCount);
            Assert.Equal(0, aggregate.MultiHandledCount);
        }

        [Fact]
        public void HybridAggregate_PrefersAttributesAndFallsBackToDynamic()
        {
            // Arrange
            var aggregate = new HybridAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var createEvent = new TestEvent(OrderCommand.Create); // has attribute handler
            var updateEvent = new TestEvent(OrderCommand.Update); // no attribute handler

            // Act
            aggregate.Apply(createEvent); // should use attribute-based handler
            aggregate.Apply(updateEvent); // should use dynamic fallback

            // Assert
            Assert.Equal(1, aggregate.AttributeHandledCount);
            Assert.Equal(1, aggregate.DynamicHandledCount);
        }

        [Fact]
        public void HybridAggregateByName_PrefersAttributesAndFallsBackToDynamic()
        {
            // Arrange
            var aggregate = new HybridAggregateByName
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var createEvent = new TestEvent(OrderCommand.Create); // has attribute handler
            var updateEvent = new TestEvent(OrderCommand.Update); // no attribute handler

            // Act
            aggregate.Apply(createEvent); // should use attribute-based handler
            aggregate.Apply(updateEvent); // should use dynamic fallback

            // Assert
            Assert.Equal(1, aggregate.AttributeHandledCount);
            Assert.Equal(1, aggregate.DynamicHandledCount);
        }

        [Fact]
        public void ConflictingAggregate_ThrowsOnConflictingAttributeHandlers()
        {
            // Arrange
            var aggregate = new ConflictingAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var createEvent = new TestEvent(OrderCommand.Create);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => aggregate.Apply(createEvent));
            Assert.Contains("Multiple ApplyEventsAttribute handlers", ex.Message);
        }

        [Fact]
        public void ConflictingAggregateByName_ThrowsOnConflictingAttributeHandlers()
        {
            // Arrange
            var aggregate = new ConflictingAggregateByName
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var createEvent = new TestEvent(OrderCommand.Create);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => aggregate.Apply(createEvent));
            Assert.Contains("Multiple ApplyEventsAttribute handlers", ex.Message);
        }

        [Fact]
        public void CanonicalInstanceAggregate_UsesInheritedCanonicalInstanceMapping()
        {
            var aggregate = new CanonicalInstanceAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            aggregate.Apply(new TestEvent(new Canonical_Order()));

            Assert.Equal(1, aggregate.HandledCount);
        }

        [Fact]
        public void InvalidCanonicalInstanceAggregate_ThrowsClearError()
        {
            var aggregate = new InvalidCanonicalInstanceAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var ex = Assert.Throws<InvalidOperationException>(() => aggregate.Apply(new TestEvent(new Canonical_Order())));
            Assert.Contains("Unable to resolve the canonical EventType instance", ex.Message);
            Assert.Contains(nameof(EventType), ex.Message);
            Assert.Contains("must be a concrete EventType type", ex.Message);
        }

        [Fact]
        public void NonVoidHandlerAggregate_ThrowsClearContractError()
        {
            // Arrange
            var aggregate = new NonVoidHandlerAggregate();

            // Act
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => aggregate.Apply(new TestEvent(OrderCommand.Create)));

            // Assert
            Assert.Contains(nameof(NonVoidHandlerAggregate), exception.Message, StringComparison.Ordinal);
            Assert.Contains("must return void", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void InvalidEventTypeHandlerAggregate_ThrowsClearContractError()
        {
            // Arrange
            var aggregate = new InvalidEventTypeHandlerAggregate();

            // Act
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => aggregate.Apply(new TestEvent(OrderCommand.Create)));

            // Assert
            Assert.Contains(typeof(string).FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains("does not derive from EventType", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ApplyAndPersistAsync_WithMixedEventsFromSameTopic_DispatchesEveryEventToItsApplyHandler()
        {
            // Arrange. A shared broker topic produces one ordered event stream for the entity.
            // The container overload is the common dispatch point used by the single-event,
            // list-event, retry, and default-handler persistence routes.
            var aggregateId = Guid.NewGuid();
            var partitionKey = Guid.NewGuid();
            var events = new List<IEvent>
            {
                new TestEvent(OrderCommand.Create) { aggregateRootId = aggregateId, partitionKey = partitionKey },
                new TestEvent(OrderCommand.Update) { aggregateRootId = aggregateId, partitionKey = partitionKey },
                new TestEvent(OrderCommand.BulkCreate) { aggregateRootId = aggregateId, partitionKey = partitionKey },
                new TestEvent(OrderCommand.BulkUpdate) { aggregateRootId = aggregateId, partitionKey = partitionKey }
            };
            var container = new Mock<Container>();
            container
                .Setup(c => c.CreateItemAsync(
                    It.IsAny<AttributeOnlyAggregate>(),
                    It.IsAny<PartitionKey?>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<ItemResponse<AttributeOnlyAggregate>>());

            // Act
            AttributeOnlyAggregate? aggregate = await container.Object.ApplyAndPersistAsync<AttributeOnlyAggregate>(
                events,
                new PartitionKey(partitionKey.ToString()));

            // Assert. Each logical EventType was preserved and dispatched independently even
            // though all events are modeled as arriving through one shared topic stream.
            Assert.NotNull(aggregate);
            Assert.Equal(1, aggregate.CreateHandledCount);
            Assert.Equal(1, aggregate.UpdateHandledCount);
            Assert.Equal(2, aggregate.MultiHandledCount);
            container.Verify(c => c.CreateItemAsync(
                aggregate,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void AttributeOnlyAggregate_SupportsMultipleEventsOnSingleHandler()
        {
            // Arrange
            var aggregate = new AttributeOnlyAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var bulkCreateEvent = new TestEvent(OrderCommand.BulkCreate);
            var bulkUpdateEvent = new TestEvent(OrderCommand.BulkUpdate);

            // Act
            aggregate.Apply(bulkCreateEvent);
            aggregate.Apply(bulkUpdateEvent);

            // Assert
            Assert.Equal(2, aggregate.MultiHandledCount);
        }

        [Fact]
        public void AttributeOnlyAggregateByName_SupportsMultipleEventsOnSingleHandler()
        {
            // Arrange
            var aggregate = new AttributeOnlyAggregateByName
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var bulkCreateEvent = new TestEvent(OrderCommand.BulkCreate);
            var bulkUpdateEvent = new TestEvent(OrderCommand.BulkUpdate);

            // Act
            aggregate.Apply(bulkCreateEvent);
            aggregate.Apply(bulkUpdateEvent);

            // Assert
            Assert.Equal(2, aggregate.MultiHandledCount);
        }

        [Fact]
        public void AttributeOnlyAggregate_UsesDefaultFallbackForUnhandledEvents()
        {
            // Arrange
            var aggregate = new AttributeOnlyAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var deleteEvent = new TestEvent(OrderCommand.Delete);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => aggregate.Apply(deleteEvent));
            Assert.Contains("Unsupported event type", ex.Message);
            Assert.Contains(nameof(AttributeOnlyAggregate), ex.Message);
        }

        [Fact]
        public void StringOnlyAggregateByName_HandlesMatchingNameWithoutTypeResolution()
        {
            // Arrange
            var aggregate = new StringOnlyAggregateByName
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var evt = new TestEvent(new NostifyCommand("DoesNotExist_Order"));

            // Act
            aggregate.Apply(evt);

            // Assert
            Assert.Equal(1, aggregate.HandledCount);
        }

        [Fact]
        public void PerformanceAggregate_DispatchesAttributeAndDynamicEventsCorrectly()
        {
            // Arrange
            var aggregate = new PerformanceAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            const int eventCount = 1000;
            var attributeEvents = Enumerable.Range(0, eventCount)
                .Select(_ => new TestEvent(OrderCommand.Create))
                .ToList();

            var dynamicEvents = Enumerable.Range(0, eventCount)
                .Select(_ => new TestEvent(OrderCommand.Update))
                .ToList();

            // Act. This is a deterministic dispatch stress test, not a
            // microbenchmark; performance measurements belong in a benchmark
            // harness where process and runtime conditions are controlled.
            foreach (var evt in attributeEvents)
            {
                aggregate.Apply(evt);
            }

            foreach (var evt in dynamicEvents)
            {
                aggregate.Apply(evt);
            }

            // Assert
            Assert.Equal(eventCount, aggregate.AttributeHandledCount);
            Assert.Equal(eventCount, aggregate.DynamicHandledCount);
        }

        #endregion
    }
}
