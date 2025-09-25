using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using nostify;
using Xunit;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace nostify.Tests
{
    public class SagaTests
    {
        private readonly Mock<INostify> _nostifyMock;
        private readonly Mock<Container> _containerMock;

        public SagaTests()
        {
            _nostifyMock = new Mock<INostify>();
            _containerMock = new Mock<Container>();
            _nostifyMock.Setup(n => n.GetSagaContainerAsync())
                .ReturnsAsync(_containerMock.Object);
            _containerMock.Setup(c => c.UpsertItemAsync<Saga>(It.IsAny<Saga>(), null, null, default))
                .ReturnsAsync((ItemResponse<Saga>)null!);
        }

        private SagaStep CreateStep(int order, SagaStepStatus status = SagaStepStatus.WaitingForTrigger)
        {
            var step = new SagaStep(order, new Event());
            step.status = status;
            return step;
        }

        private IEvent CreateEventMock()
        {
            var eventMock = new Mock<Event>();
            eventMock.SetupAllProperties();
            return eventMock.Object;
        }

        [Fact]
        public async Task StartAsync_ShouldSetStatusAndStartFirstStep()
        {
            // Arrange
            var step = CreateStep(1);
            var eventMock = CreateEventMock();
            step.stepEvent = eventMock;

            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step });

            // Act
            await saga.StartAsync(_nostifyMock.Object);

            // Assert
            Assert.Equal(SagaStatus.InProgress, saga.status);
            Assert.NotNull(saga.executionStart);
            _nostifyMock.Verify(n => n.GetSagaContainerAsync(), Times.AtLeastOnce);
        }

        [Fact]
        public async Task HandleSuccessfulStepAsync_ShouldCompleteSagaIfLastStep()
        {
            // Arrange
            var step = CreateStep(1, SagaStepStatus.Triggered);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step });
            saga.status = SagaStatus.InProgress;
            step.status = SagaStepStatus.Triggered;

            // Act
            await saga.HandleSuccessfulStepAsync(_nostifyMock.Object);

            // Assert
            Assert.Equal(SagaStatus.CompletedSuccessfully, saga.status);
            Assert.NotNull(saga.executionCompletedOn);
        }

        [Fact]
        public async Task HandleSuccessfulStepAsync_ShouldTriggerNextStepIfNotLast()
        {
            // Arrange
            var step1 = CreateStep(1, SagaStepStatus.Triggered);
            var step2 = CreateStep(2, SagaStepStatus.WaitingForTrigger);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step1, step2 });
            saga.status = SagaStatus.InProgress;
            step1.status = SagaStepStatus.Triggered;

            // Act
            await saga.HandleSuccessfulStepAsync(_nostifyMock.Object);

            // Assert
            Assert.Equal(SagaStatus.InProgress, saga.status);
            Assert.Equal(SagaStepStatus.Triggered, step2.status);
        }

        [Fact]
        public async Task StartRollbackAsync_ShouldSetStatusAndCallRollback()
        {
            // Arrange
            var step = CreateStep(1, SagaStepStatus.Triggered);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step });
            saga.status = SagaStatus.InProgress;
            step.status = SagaStepStatus.Triggered;

            // Act
            await saga.StartRollbackAsync(_nostifyMock.Object);

            // Assert
            Assert.Equal(SagaStatus.RollingBack, saga.status);
            Assert.NotNull(saga.rollbackStartedOn);
        }

        [Fact]
        public async Task HandleSuccessfulStepRollbackAsync_ShouldCompleteSagaRollbackIfNoNextStep()
        {
            // Arrange
            var step = CreateStep(1, SagaStepStatus.RollingBack);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step });
            saga.status = SagaStatus.RollingBack;
            step.status = SagaStepStatus.RollingBack;

            // Act
            await saga.HandleSuccessfulStepRollbackAsync(_nostifyMock.Object);

            // Assert
            Assert.Equal(SagaStatus.RolledBack, saga.status);
            Assert.NotNull(saga.rollbackCompletedOn);
        }

        [Fact]
        public async Task HandleSuccessfulStepRollbackAsync_ShouldTriggerNextRollbackStepIfExists()
        {
            // Arrange
            var step1 = CreateStep(1, SagaStepStatus.CompletedSuccessfully);
            step1.rollbackEvent = CreateEventMock();
            var step2 = CreateStep(2, SagaStepStatus.RollingBack);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step1, step2 });
            saga.status = SagaStatus.RollingBack;

            // Act
            await saga.HandleSuccessfulStepRollbackAsync(_nostifyMock.Object);

            // Assert
            Assert.Equal(SagaStatus.RollingBack, saga.status);
            Assert.Equal(SagaStepStatus.RolledBack, step2.status);
            Assert.Equal(SagaStepStatus.RollingBack, step1.status);
        }

        [Fact]
        public async Task HandleSuccessfulStepRollbackAsync_ShouldCompleteAllIfNextRollbackEventNotExists()
        {
            // Arrange
            var step1 = CreateStep(1, SagaStepStatus.CompletedSuccessfully);
            //No rollbackEvent for step 1
            var step2 = CreateStep(2, SagaStepStatus.RollingBack);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step1, step2 });
            saga.status = SagaStatus.RollingBack;

            // Act
            await saga.HandleSuccessfulStepRollbackAsync(_nostifyMock.Object);

            // Assert
            Assert.Equal(SagaStatus.RolledBack, saga.status);
            Assert.Equal(SagaStepStatus.RolledBack, step2.status);
            Assert.Equal(SagaStepStatus.RolledBack, step1.status);
        }

        [Fact]
        public void AddStep_ShouldAddStepWithCorrectOrder()
        {
            // Arrange
            ISaga saga = new Saga("TestSaga");
            var evt = new Event();

            // Act
            saga.AddStep(evt);

            // Assert
            Assert.Single(saga.steps);
            Assert.Equal(1, saga.steps[0].order);
        }

        [Fact]
        public void AddStep_ShouldIncrementOrder()
        {
            // Arrange
            ISaga saga = new Saga("TestSaga");
            var evt1 = new Event();
            var evt2 = new Event();

            // Act
            saga.AddStep(evt1);
            saga.AddStep(evt2);

            // Assert
            Assert.Equal(2, saga.steps.Count);
            Assert.Equal(1, saga.steps[0].order);
            Assert.Equal(2, saga.steps[1].order);
        }

        [Fact]
        public void Constructor_ShouldSetProperties()
        {
            // Arrange
            var name = "SagaName";
            var steps = new List<SagaStep>();

            // Act
            ISaga saga = new Saga(name, steps);

            // Assert
            Assert.Equal(name, saga.name);
            Assert.Equal(SagaStatus.Pending, saga.status);
            Assert.NotEqual(Guid.Empty, saga.id);
            Assert.NotEqual(default(DateTime), saga.createdOn);
        }

        [Fact]
        public async Task StartAsync_ShouldThrowIfAlreadyStarted()
        {
            // Arrange
            var step = CreateStep(1);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step });
            saga.status = SagaStatus.InProgress;

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => saga.StartAsync(_nostifyMock.Object));
        }

        [Fact]
        public async Task HandleSuccessfulStepAsync_ShouldThrowIfNotInProgress()
        {
            // Arrange
            var step = CreateStep(1, SagaStepStatus.Triggered);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step });
            saga.status = SagaStatus.Pending;

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => saga.HandleSuccessfulStepAsync(_nostifyMock.Object));
        }

        [Fact]
        public async Task HandleSuccessfulStepRollbackAsync_ShouldThrowIfNotRollingBack()
        {
            // Arrange
            var step = CreateStep(1, SagaStepStatus.RollingBack);
            ISaga saga = new Saga("TestSaga", new List<SagaStep> { step });
            saga.status = SagaStatus.InProgress;

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => saga.HandleSuccessfulStepRollbackAsync(_nostifyMock.Object));
        }

        [Fact]
        public void Saga_Serialization_Roundtrip()
        {
            // Arrange
            var step = CreateStep(1, SagaStepStatus.WaitingForTrigger);
            var saga = new Saga("TestSaga", new List<SagaStep> { step });

            // Act
            var json = JsonConvert.SerializeObject(saga);
            var deserialized = JsonConvert.DeserializeObject<Saga>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(saga.name, deserialized!.name);
            Assert.Single(deserialized.steps);
            Assert.Equal(step.order, deserialized.steps[0].order);
        }
    }
}