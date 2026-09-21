using System;
using System.Diagnostics;
using System.Linq;
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
        private sealed class AttributeOnlyAggregate : NostifyObject, IAggregate
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
        private sealed class AttributeOnlyAggregateByName : NostifyObject, IAggregate
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
        private sealed class HybridAggregate : NostifyObject, IAggregate
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
        private sealed class HybridAggregateByName : NostifyObject, IAggregate
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
        private sealed class ConflictingAggregate : NostifyObject, IAggregate
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
        private sealed class ConflictingAggregateByName : NostifyObject, IAggregate
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
        private sealed class StringOnlyAggregateByName : NostifyObject, IAggregate
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

        /// <summary>
        /// Aggregate used for the performance comparison between attribute-based and dynamic dispatch.
        /// </summary>
        private sealed class PerformanceAggregate : NostifyObject, IAggregate
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
        public void PerformanceAggregate_ComparesAttributeVsDynamicDispatch_For1000Events()
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

            // Warm-up to ensure the handler map is built and JIT has run.
            aggregate.Apply(attributeEvents[0]);
            aggregate.Apply(dynamicEvents[0]);
            aggregate.AttributeHandledCount = 0;
            aggregate.DynamicHandledCount = 0;

            // Act - attribute-based dispatch timing
            var sw = Stopwatch.StartNew();
            foreach (var evt in attributeEvents)
            {
                aggregate.Apply(evt);
            }
            sw.Stop();
            var attributeMs = sw.Elapsed.TotalMilliseconds;

            // Act - dynamic dispatch timing
            sw.Restart();
            foreach (var evt in dynamicEvents)
            {
                aggregate.Apply(evt);
            }
            sw.Stop();
            var dynamicMs = sw.Elapsed.TotalMilliseconds;

            // Assert correctness
            Assert.Equal(eventCount, aggregate.AttributeHandledCount);
            Assert.Equal(eventCount, aggregate.DynamicHandledCount);

            // Output timing comparison (no assertion on timing values to keep test stable).
            var faster = attributeMs < dynamicMs ? "Attribute" : "Dynamic";
            var msDiff = Math.Abs(attributeMs - dynamicMs);
            var denom = Math.Max(attributeMs, dynamicMs);
            var pctDiff = denom > 0 ? (msDiff / denom) * 100.0 : 0.0;

            Console.WriteLine(
                $"Apply dispatch performance over {eventCount} events: " +
                $"Attribute={attributeMs:F3} ms, Dynamic={dynamicMs:F3} ms. " +
                $"Faster={faster}, Δ={msDiff:F3} ms ({pctDiff:F2}%).");

            Assert.True(true, $"Apply dispatch performance over {eventCount} events: " +
                               $"Attribute={attributeMs:F3} ms, Dynamic={dynamicMs:F3} ms. " +
                               $"Faster={faster}, Δ={msDiff:F3} ms ({pctDiff:F2}%).");
        }

        #endregion
    }
}
