using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using Newtonsoft.Json.Linq;
using nostify;
using Xunit;

public class ProjectionBaseClassTests
{
    public class ProjectionBaseClassTestAggregateCommand : NostifyCommand
    {
        ///<summary>
        ///Base Create Command
        ///</summary>
        public static readonly ProjectionBaseClassTestAggregateCommand Create = new ProjectionBaseClassTestAggregateCommand("Create_ProjectionBaseClassTestAggregate", true);
        ///<summary>
        ///Base Update Command
        ///</summary>
        public static readonly ProjectionBaseClassTestAggregateCommand Update = new ProjectionBaseClassTestAggregateCommand("Update_ProjectionBaseClassTestAggregate");
        ///<summary>
        ///Base Delete Command
        ///</summary>
        public static readonly ProjectionBaseClassTestAggregateCommand Delete = new ProjectionBaseClassTestAggregateCommand("Delete_ProjectionBaseClassTestAggregate");
        ///<summary>
        ///Bulk Create Command
        ///</summary>
        public static readonly ProjectionBaseClassTestAggregateCommand BulkCreate = new ProjectionBaseClassTestAggregateCommand("BulkCreate_ProjectionBaseClassTestAggregate", true);


        public ProjectionBaseClassTestAggregateCommand(string name, bool isNew = false)
        : base(name, isNew)
        {

        }
    }

    public class ExternalAggregateCommand : NostifyCommand
    {
        ///<summary>
        ///Base Create Command
        ///</summary>
        public static readonly ExternalAggregateCommand Create = new ExternalAggregateCommand("Create_ExternalAggregate", true);
        ///<summary>
        ///Base Update Command
        ///</summary>
        public static readonly ExternalAggregateCommand Update = new ExternalAggregateCommand("Update_ExternalAggregate");
        ///<summary>
        ///Base Delete Command
        ///</summary>
        public static readonly ExternalAggregateCommand Delete = new ExternalAggregateCommand("Delete_ExternalAggregate");
        ///<summary>
        ///Bulk Create Command
        ///</summary>
        public static readonly ExternalAggregateCommand BulkCreate = new ExternalAggregateCommand("BulkCreate_ExternalAggregate", true);


        public ExternalAggregateCommand(string name, bool isNew = false)
        : base(name, isNew)
        {

        }
    }

    public static Guid testExternalAggregateId = Guid.Parse("8adfe336-aa9e-4492-959f-8048068bc0c2");
    public class ExternalAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => "ExternalAggregate";

        public static string currentStateContainerName => $"{aggregateType}CurrentState";

        public string name { get; set; }
        public bool isDeleted { get; set; }

        public override void Apply(Event eventToApply)
        {
            if (eventToApply.command == ExternalAggregateCommand.Create || eventToApply.command == ExternalAggregateCommand.Update)
            {
                this.UpdateProperties<ExternalAggregate>(eventToApply.payload);
            }
            else if (eventToApply.command == ExternalAggregateCommand.Delete)
            {
                this.isDeleted = true;
            }
        }
    }

    public static Guid testProjAggGuid = Guid.Parse("e21f0782-b9f7-46cf-a5fb-42201050bea8");
    public class ProjectionBaseClassTestAggregate : NostifyObject, IAggregate
    {
        public static string aggregateType => throw new NotImplementedException();

        public static string currentStateContainerName => throw new NotImplementedException();

        public bool isDeleted { get; set; } = false;
        public string name { get; set; }
        public Guid? externalId { get; set; }

        public override void Apply(Event eventToApply)
        {
            if (eventToApply.command == ProjectionBaseClassTestAggregateCommand.Create || eventToApply.command == ProjectionBaseClassTestAggregateCommand.Update)
            {
                this.UpdateProperties<ProjectionBaseClassTestAggregate>(eventToApply.payload);
            }
            else if (eventToApply.command == ProjectionBaseClassTestAggregateCommand.Delete)
            {
                this.isDeleted = true;
            }
        }
    }

    public class ProjectionBaseClassTestProjection : ProjectionBaseClass<ProjectionBaseClassTestProjection, ProjectionBaseClassTestAggregate>, IContainerName, IHasExternalData<ProjectionBaseClassTestProjection>
    {
        public static string containerName => "TestContainer";

        public string name { get; set; }
        public Guid? externalId { get; set; }
        public string? externalAggregateName { get; set; }

        public static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<ProjectionBaseClassTestProjection> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
        {
            return Task.FromResult(new List<ExternalDataEvent>(){
                new ExternalDataEvent(testProjAggGuid, 
                    new List<Event>(){
                        new Event(ExternalAggregateCommand.Create, testExternalAggregateId, new ExternalAggregate(){ id = testExternalAggregateId, name = "TestExternal" })
                    })
            });
        }

        public override void Apply(Event eventToApply)
        {
            if (eventToApply.command == ProjectionBaseClassTestAggregateCommand.Create 
                || eventToApply.command == ProjectionBaseClassTestAggregateCommand.Update)
            {
                this.UpdateProperties<ProjectionBaseClassTestAggregate>(eventToApply.payload);
            }
            else if (eventToApply.command == ExternalAggregateCommand.Create)
            {
                this.externalId = eventToApply.aggregateRootId;
                this.externalAggregateName = eventToApply.GetPayload<ExternalAggregate>().name;
            }
        }
    }

    [Fact]
    public async Task InitAsync_ShouldInitializeProjection()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var mockHttpClient = new HttpClient();
        var testProjection = new ProjectionBaseClassTestProjection(){
            name = "TestProjection",
            id = testProjAggGuid
        };

        mockNostify.Setup(n => n.GetBulkProjectionContainerAsync<ProjectionBaseClassTestProjection>(It.IsAny<string>()))
            .ReturnsAsync(new Mock<Container>().Object);

        // Act
        var result = await testProjection.InitAsync(mockNostify.Object, mockHttpClient);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(result.externalId, testExternalAggregateId);
        Assert.Equal("TestExternal", result.externalAggregateName);
        Assert.True(result.initialized);
    }

    [Fact(Skip = "Doesn't work, need to figure out how to mock cosmos linq query")]
    public async Task InitAsync_WithId_ShouldInitializeProjections()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var mockHttpClient = new HttpClient();
        var mockProjectionContainer = new Mock<Container>();
        var mockAggContainer = new Mock<Container>();
        var mockIOrderedQueryable = new Mock<IOrderedQueryable<ProjectionBaseClassTestAggregate>>();

        mockNostify.Setup(n => n.GetBulkProjectionContainerAsync<ProjectionBaseClassTestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockProjectionContainer.Object);
        mockNostify.Setup(n => n.GetCurrentStateContainerAsync<ProjectionBaseClassTestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockAggContainer.Object);
        mockAggContainer.Setup(a => a.GetItemLinqQueryable<ProjectionBaseClassTestAggregate>(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>(), It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(mockIOrderedQueryable.Object);
        mockIOrderedQueryable.Setup(q => q.ReadAllAsync())
            .ReturnsAsync(new List<ProjectionBaseClassTestAggregate> { new ProjectionBaseClassTestAggregate { id = testProjAggGuid } });


        // Act
        var result = await ProjectionBaseClassTestProjection.InitAsync(testProjAggGuid, mockNostify.Object, mockHttpClient);

        // Assert
        Assert.NotNull(result);
        Assert.All(result, p => Assert.True(p.initialized));
    }

    [Fact(Skip = "Doesn't work, need to figure out how to mock cosmos linq query")]
    public async Task InitAsync_WithIds_ShouldInitializeProjections()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var mockHttpClient = new HttpClient();
        var testIds = new List<Guid> { testProjAggGuid };
        var mockProjectionContainer = new Mock<Container>();
        var mockAggContainer = new Mock<Container>();

        mockNostify.Setup(n => n.GetBulkProjectionContainerAsync<ProjectionBaseClassTestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockProjectionContainer.Object);
        mockNostify.Setup(n => n.GetCurrentStateContainerAsync<ProjectionBaseClassTestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockAggContainer.Object);
        mockAggContainer.Setup(a => a.GetItemLinqQueryable<ProjectionBaseClassTestAggregate>(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>(), It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(new List<ProjectionBaseClassTestAggregate> { new ProjectionBaseClassTestAggregate { id = testProjAggGuid } }.AsQueryable().OrderBy(a => a.id));    

        // Act
        var result = await ProjectionBaseClassTestProjection.InitAsync(testIds, mockNostify.Object, mockHttpClient);

        // Assert
        Assert.NotNull(result);
        Assert.All(result, p => Assert.True(p.initialized));
    }

    [Fact(Skip = "Doesn't work, need to figure out how to mock cosmos linq query")]
    public async Task InitContainerAsync_ShouldInitializeContainer()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var mockHttpClient = new HttpClient();
        var mockProjectionContainer = new Mock<Container>();
        var mockAggContainer = new Mock<Container>();
        var mockContainerResponse = new Mock<ContainerResponse>();

        mockNostify.Setup(n => n.GetBulkProjectionContainerAsync<ProjectionBaseClassTestProjection>(It.IsAny<string>()))
            .ReturnsAsync(mockProjectionContainer.Object);
        mockNostify.Setup(n => n.GetCurrentStateContainerAsync<ProjectionBaseClassTestAggregate>(It.IsAny<string>()))
            .ReturnsAsync(mockAggContainer.Object);
        mockAggContainer.Setup(a => a.GetItemLinqQueryable<ProjectionBaseClassTestAggregate>(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>(), It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(new List<ProjectionBaseClassTestAggregate> { new ProjectionBaseClassTestAggregate { id = testProjAggGuid } }.AsQueryable().OrderBy(a => a.id));    
        mockProjectionContainer.Setup(p => p.GetItemLinqQueryable<ProjectionBaseClassTestProjection>(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>(), It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns(new List<ProjectionBaseClassTestProjection> { new ProjectionBaseClassTestProjection { id = testProjAggGuid } }.AsQueryable().OrderBy(p => p.id));
        mockProjectionContainer.Setup(p => p.ReadContainerAsync(It.IsAny<ContainerRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContainerResponse.Object);
        mockContainerResponse.Setup(c => c.Resource)
            .Returns(new ContainerProperties());

        // Act
        await ProjectionBaseClassTestProjection.InitContainerAsync(mockNostify.Object, mockHttpClient);

        // Assert
        mockNostify.Verify(n => n.GetContainerAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.AtLeastOnce);
        mockNostify.Verify(n => n.GetCurrentStateContainerAsync<ProjectionBaseClassTestAggregate>(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact(Skip = "Doesn't work, need to figure out how to mock cosmos linq query")]
    public async Task InitAllUninitialized_ShouldInitializeAllUninitializedProjections()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var mockHttpClient = new HttpClient();

        mockNostify.Setup(n => n.GetProjectionContainerAsync<ProjectionBaseClassTestProjection>("tenantId"))
            .ReturnsAsync(new Mock<Container>().Object);

        // Act
        await ProjectionBaseClassTestProjection.InitAllUninitialized(mockNostify.Object, mockHttpClient);

        // Assert
        mockNostify.Verify(n => n.GetProjectionContainerAsync<ProjectionBaseClassTestProjection>("tenantId"), Times.Once);
    }
}