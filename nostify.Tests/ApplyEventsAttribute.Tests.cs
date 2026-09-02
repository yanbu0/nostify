using System;
using System.Diagnostics;
using System.Linq;
using nostify;
using nostify.Attributes;
using Xunit;

namespace nostify.Tests
{
    /// <summary>
    /// Tests for the <see cref="ApplyEventsAttribute"/>-based dispatch and its interaction
    /// with the existing dynamic Apply(EventType, IEvent) overload-based dispatch.
    ///
    /// These tests follow the same EventType pattern as the generated command templates
    /// in templates/nostify/_ReplaceMe_/Aggregates/_ReplaceMe_/_ReplaceMe_Command.cs, using
    /// an OrderCommand base with concrete Create__Order_, Update__Order_, etc. types.
    /// </summary>
    public class ApplyEventsAttributeTests
    {
        #region Test helpers - EventType model

        /// <summary>
        /// Base command type for the Order aggregate, mirroring the _ReplaceMe_Command template.
        /// </summary>
        private abstract class OrderCommand : EventType
        {
            public static Create__Order_ Create => Create__Order_.Instance;
            public static Update__Order_ Update => Update__Order_.Instance;
            public static BulkCreate__Order_ BulkCreate => BulkCreate__Order_.Instance;
            public static BulkUpdate__Order_ BulkUpdate => BulkUpdate__Order_.Instance;

            protected OrderCommand(string name, bool isNew = false, bool allowNullPayload = false)
                : base(name, isNew, allowNullPayload)
            {
            }
        }

        private sealed class Create__Order_ : OrderCommand
        {
            public static readonly Create__Order_ Instance = new Create__Order_();

            private Create__Order_() : base("Create__Order_", isNew: true)
            {
            }
        }

        private sealed class Update__Order_ : OrderCommand
        {
            public static readonly Update__Order_ Instance = new Update__Order_();

            private Update__Order_() : base("Update__Order_")
            {
            }
        }

        private sealed class BulkCreate__Order_ : OrderCommand
        {
            public static readonly BulkCreate__Order_ Instance = new BulkCreate__Order_();

            private BulkCreate__Order_() : base("BulkCreate__Order_", isNew: true)
            {
            }
        }

        private sealed class BulkUpdate__Order_ : OrderCommand
        {
            public static readonly BulkUpdate__Order_ Instance = new BulkUpdate__Order_();

            private BulkUpdate__Order_() : base("BulkUpdate__Order_")
            {
            }
        }

        private sealed class Delete__Order_ : OrderCommand
        {
            public static readonly Delete__Order_ Instance = new Delete__Order_();

            private Delete__Order_() : base("Delete__Order_", allowNullPayload: true)
            {
            }
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

            [ApplyEvents(typeof(Create__Order_))]
            protected void ApplyCreate(IEvent e)
            {
                CreateHandledCount++;
            }

            [ApplyEvents(typeof(Update__Order_))]
            protected void ApplyUpdate(IEvent e)
            {
                UpdateHandledCount++;
            }

            [ApplyEvents(typeof(BulkCreate__Order_), typeof(BulkUpdate__Order_))]
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

            [ApplyEvents(typeof(Create__Order_))]
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

            [ApplyEvents(typeof(Create__Order_))]
            protected void FirstHandler(IEvent e) { }

            [ApplyEvents(typeof(Create__Order_))]
            protected void SecondHandler(IEvent e) { }

            protected override void Apply(EventType eventType, IEvent eventToApply)
            {
                // not used
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

            [ApplyEvents(typeof(Create__Order_))]
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
        public void AttributeOnlyAggregate_UsesDefaultFallbackForUnhandledEvents()
        {
            // Arrange
            var aggregate = new AttributeOnlyAggregate
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            };

            var deleteEvent = new TestEvent(Delete__Order_.Instance);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => aggregate.Apply(deleteEvent));
            Assert.Contains("Unsupported event type", ex.Message);
            Assert.Contains(nameof(AttributeOnlyAggregate), ex.Message);
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
