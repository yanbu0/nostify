
# nostify

## Overview

`nostify` provides a framework for creating event driven microservices implementing the CQRS/ES pattern. It leverages Azure Functions for scalability and Cosmos DB for data storage, making it suitable for applications requiring high throughput and low latency. It also assumes some basic familiarity with domain driven design. For a practical demonstration, see the [nostify-example repository](https://github.com/yanbu0/nostify-example) which showcases microservices built with this framework.

## Features

- Microservice Architecture: Encapsulates domain logic within services, promoting modularity and scalability.

- CQRS Implementation: Separates command and query responsibilities to optimize performance and maintainability.

- Event Sourcing: Utilizes events to represent state changes, enabling traceability and auditability.

- Azure Functions Integration: Scales automatically to handle varying loads, ensuring high availability.

- Cosmos DB Storage: Provides a globally distributed, multi-model database for storing aggregates and projections.

- Multi-tenant by default, ideal for SaaS applications

## Current Status

### Updates

- 3.8.1
  - **Bug Fix**: Fixed issue with null values in PropertyCheck when projectionIdPropertyValue is null
  - **Template Updates**: All template project files updated to reference nostify 3.8.1
- 3.8.0
  - **Enhanced UpdateProperties using PropertyCheck class**: Added new `UpdateProperties<T>(Guid eventAggregateRootId, object payload, List<PropertyCheck> propertyCheckValues)` overload for conditional property mapping based on ID matching, used in `Apply` when a projection has multiple references to external aggregates of the same type.
  - **Comprehensive PropertyCheck Testing**: Added 14+ test scenarios including shared ID handling, edge cases, and complex multi-property updates
  - **Template Updates**: All template project files updated to reference nostify 3.8.0
- 3.7.4
  - **Bug Fix**: Fixed issue with PersistEventAsync method
  - **Template Updates**: All template project files updated to reference nostify 3.7.4
- 3.7.3
  - **Enhanced ValidatePayload Testing**: Added comprehensive POCO class tests for ValidatePayload functionality with RequiredFor and validation attributes
  - **Template Updates**: All template project files updated to reference nostify 3.7.3
- 3.7.1
  - **Mixed Constructor Support**: `EventRequester` now supports mixing single ID selectors (`Func<T, Guid?>`) and list ID selectors (`Func<T, List<Guid?>>`) in the same instance
  - **EventRequester List Constructor**: Added params constructor for list selectors: `new EventRequester(url, p => p.listOfIds)`
  - **Non-Nullable Constructor Support**: Added constructors accepting `Func<T, Guid>[]` and `Func<T, List<Guid>>[]` for stricter null handling
- 3.7.0
  - **Enhanced Multi-Service Event Querying**: Added `GetMultiServiceEventsAsync` method for efficient parallel querying of multiple external services
  - **EventRequester Pattern**: New `EventRequester<T>` class with support for multiple foreign ID selectors per service
  - **Point-in-Time Query Support**: EventRequest endpoints now support optional DateTime path parameters for historical event queries (`/EventRequest/{pointInTime:datetime?}`)
  - **Parallel External Data Processing**: GetMultiServiceEventsAsync executes all external service calls simultaneously for improved performance
  - **Flexible Service Configuration**: EventRequester supports complex projection relationships with multiple foreign ID mappings
  - **Template Updates**: All template project files updated to reference nostify 3.7.0
- 3.6.0
  - Added `IEvent` interface for better abstraction and testability of Event objects
  - Enhanced `EventFactory` (renamed from EventBuilder) with instance-based design and optional validation
  - Added `CreateNullPayloadEvent()` method to EventFactory for operations without payload data (e.g., delete operations)
  - Updated all templates to use `new EventFactory().Create<T>()` instead of direct Event instantiation for consistency
  - Templates now reference nostify 3.6.0 across all project types (nostify, nostifyAggregate, nostifyProjection)
- 3.5.0
  - Added comprehensive payload validation system with `ValidatePayload<T>()` method on Events
  - Introduced `RequiredForAttribute` to specify command-specific property validation requirements
  - Enhanced Event class with validation capabilities for aggregate properties
- 3.4.4
  - Updated service template to use Newtonsoft.Json explicitly by default, System.Text.Json is a hot mess
  - Updated Projection template to use GetEventsAsync
- 3.4.3
  - Updated service template to use latest factory method
- 3.4.2
  - Fixed template connection string error
- 3.4.0
  - Added `MultiApplyAndPersistAsync<P>` methods in `Nostify` and container extensions to allow applying and saving the results of an `Event` across multiple Projections. This is useful when you have a large number of Projections in a container that will be updated simultaneously by a single event. Incorporates multi-threaded, batch processing and retries for larger amounts of data.
  - Changed template return types to base object type
  - Cleaned up tests
- 3.3.1
  - Updated templates to emit conditional compliation commands in templates
- 3.3.0
  - Added overload to GetEventsAsync to accept a `params Func<TProjection, List<Guid?>>[]` foreignIdSelectorsList so you can now get id properties inside lists of child objects
- 3.2.0
  - Added support for Saga orchestration
- 3.1.1
  - Improved details available in errors published to Kafka
  - Aggregate template updated with ability to set namespace properly
- 3.0.0 Released
  - Improved inheritance, now simpler to build Projections
  - Consistent, easier way to init Projection Containers, can call from `INostify` rather than a static class
  - No more `ProjectionBaseClass<P>` abstract class, Projections now only have to implement `NostifyObject` and `IProjection`, making it easy to
  create a base class for both the root Aggregate and Projections of it containing common properties and methods.
  - Templates updated to use 3.0.0 compatible code, remove `OkResult()`, add base class by default

### Coming Soon

- Better support for non-command events (4.0)

## Getting Started

To run locally you will need to install some dependencies:

- Azurite: `npm install -g azurite`

- Azurite VS Code Extension: <https://marketplace.visualstudio.com/items?itemName=Azurite.azurite>

- Docker Desktop: <https://www.docker.com/products/docker-desktop/>

- Confluent CLI: <https://docs.confluent.io/confluent-cli/current/install.html> 
  - To run: `confluent local kafka start`
  - To add topic: `confluent local kafka topic create <Topic Name>`

- Cosmos Emulator: <https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-develop-emulator?tabs=windows%2Ccsharp&pivots=api-nosql>

To install `nostify` and templates:

```powershell
dotnet new install nostify
```

To spin up a nostify project:

```powershell
dotnet new nostify -ag <Your_Aggregate_Name> -p <Port Number To Run on Locally>

dotnet restore
```

This will install the templates, create the default project based off your Aggregate, and install all the necessary libraries.

## Architecture

The library is designed to be used in a microservice pattern (although not necessarily required) using an Azure Function App api and Cosmos as the event store. Kafka serves as the messaging backpane, and projections can be stored in Cosmos or Redis depending on query needs.

You should set up a Function App and Cosmos DB per Aggregate Microservice.

Projections that contain data from multiple Aggregates can be updated by Event Handlers from other microservices. Why would this happen? Well say you have a Bank Account record. If we were using a relational database for a data store we'd have to either run two queries or do a join to get the Bank Account and the name of the Account Manager. Using the CQRS model, we can "pre-render" a projection that contains both the account info and the account manager info without having to join tables together. This example is obviously very simple, but in a complex environment where you're joining together dozens of tables to create a DTO to send to the user interface and returning 100's of thousands or millions of records, this type of architecture can dramatically improve BOTH system performance and throughput. Reference architecture diagram:

![Architecture Diagram](https://raw.githubusercontent.com/yanbu0/nostify/main/readme_assets/architecture-diagram.png)

## Why????

When is comes to scaling there are two things to consider: speed and throughput. "Speed" meaning the quickness of the individual action, and "throughput" meaning the number of concurrent actions that can be performed at the same time. Using `nostify` addresses both of those concerns.  

Speed really comes into play only on the query side for most applications. Thats a large part of the concept behind the CQRS pattern. By seperating the command side from the query side you essentially deconstruct the datastore that would traditionally be utilizing a RDBMS in order to create materialized views of various projections of the aggregate. Think of these views as "pre-rendered" views in a traditional relational database. In a traditional database a view simplifies queries but still runs the joins in real time when data is requested. By materializing the view, we denormalize the data and accept the increased complexity associated with keeping the data accurate in order to massively decrease the performance cost of querying that data. In addition, we gain flexibility by being able to appropriately resource each container to give containers being queried the hardest more resources.

Throughput is the other half of the equation. If you were using physical architecture, you'd have an app server talking to a separate database server serving up your application. The app server say has 4 processors with 8 cores each, so there is a limitation on the number of concurrent tasks that can be performed. We can enhance throughput through proper coding, using parallel processing, and non-blocking code, but there is at a certain point a physical limit to the number of things that can be happening at once. With `nostify` and the use of Azure Functions, this limitation is removed other than by cost. If 1000 queries hit at the same moment in time, 1000 instances of an Azure Function spin up to handle it. You're limited more by cost than physical hardware.

## Concepts

### Service

A service in `nostify` is a stand alone mini-application that encapsulates a group of aggregates and projections and the logic for handling state changes to them.  Generally speaking, a single service should encapsulate the logic for a single domain context if you're embracing the microservices pattern.

A service template will contain the directory structure, class files, and code to handle basic Create, Update, and Delete commands as well as some basic queries out of the box.

#### Create

```powershell

dotnet new nostify -ag <Your_Aggregate_Name> -p <Port Number To Run on Locally>
```

### Aggregate

An aggregate encapsulates the logic around the "C" or Command part of the CQRS pattern.

>An Aggregate is a cluster of associated objects that we treat as a unit for the purpose of data changes.
>-Eric Evans

For example a Purchase Requisition might have a `id`, `pr_number`,  and `lineItems` properties where `lineItems` is a `List<LineItem>` type.  The aggregate would be composed of multiple types of objects as well as simple types in this case, and the line items of the requisition would never be edited outside the context of their containing req.

In `nostify` all state changes coming from the UI are called a `NostifyCommand` and must be performed against an aggregate.  This is done by composing an `Event`, which is written to the event store.  This triggers the event getting pushed to the messaging system (Kafka) which the event handler subscribes to.  Once triggered the event  handler updates the current state container of the aggregate.

#### Rules

- All state changes must be applied to an aggregate, and not directly to a projection or to an entity within an aggregate.  
- Aggregates may only refer to other aggregates by id value.
- An aggregate must implement the `NostifyObject` abstract class and the `IAggregate` interface.
- All state changes must be applied using the `Apply()` method, either directly or by using the `ApplyAndPersistAsync<T>()` method.  The current state projection of an aggregate is simply the sum of all the events in the event store applied to a new instance of that aggregate object.
- In `nostify` only the changed properties should be included when creating an `Event`, best practice is to not send the entire aggregate.
- A service generally contains one or more aggregates.  A read-only service, such as for BI purposes might not.  Domain boundaries should be respected when grouping aggregates together in a service.  IE - grouping a `WorkOrder` aggregate in the same service as a `WorkOrderStatus` aggregate (which might be an aggregate if your application allows users to add and update them) might make sense, where as putting a `PurchaseRequest` and a `WorkOrder` in the same service might not.  This is an art not a science so do what makes sense to your application.  It is theoretically possible to completely abandon the microservice concept and group an entire application into a single service, but probably not a good idea for scalability and maintainability.

#### Create

A "base" aggregate is created when you use the dotnet cli to create a new `nostify` service

```powershell
dotnet new nostify -ag <Your_Aggregate_Name> -p <Port Number To Run on Locally>
```

If you are adding a new aggregate to an existing service, run the below cli command from the Aggregates directory.

```powershell
dotnet new nostifyAggregate -ag <Your_Aggregate_Name> -s <Name_Of_Service>
```

### Projection

A projection encapsulates the logic surrounding the "Q" or Query part of the CQRS pattern.

The current state of an aggregate is the sum of all the events for that aggregate in the event store.  However, querying the entire stream of events and applying them every time you want to send the current state of an aggregate to the user interface can be inefficient, even more so if you are trying to compose a DTO with properties from multiple aggregates across services.

The solution to this in `nostify` is the projection pattern.  A projection defines a set of properties from one or more aggregates and then stores the current state of those properties as updated by the event stream.  Every projection will have a "base" aggregate that will be the trigger to create a new persisted instance of the projection, and will define any necessary queries for getting data from external aggregates.  

#### Rules

- No command is ever performed on a projection, they are updated by subscribing to and handling events on their component aggregates.
- A projection will have a "base" aggregate and the `id` property of the projection will match the `id` property of the base aggregate. The base aggregate will be the aggregate that the create event is handled, creating a new instance of the projection in the container.
- A projection must implement `IProjection` and the `NostifyObject` class, usually by inheirting the base class of the root Aggregate. Most will also need to implement `IHasExternalData<P>`.
- For projections with data coming from aggregates external to the "base" aggregate, `IHasExternalData.GetExternalDataEvents()` method must be implemented to handle getting events from external sources to apply to the projection. The results of this method are used by the projection initialization process to update either the external data of a new single projection instance, or many projections when the container is initialized or data is imported.

#### Create

A projection must be added to an existing service.  Base aggregate must already exist. From the Projections directory:

```powershell
dotnet new nostifyProjection -ag <Base_Aggregate_Name> --projectionName <Projection_Name>
```

### Event

An `Event` captures a state change to the application. Generally, this is caused by the user issuing a command such as "save". When a command comes in from the front end to the endpoint, the http triggers a command handler function which validates the command, composes an `Event` and persists it to the event store. Note that while a `Command` is always an `Event`, an `Event` is not necessarily always a `Command`. It is possible for an `Event` to originate elsewhere, say from an IoT device for example.

Events implement the `IEvent` interface, which provides better abstraction and testability. The EventFactory returns `IEvent` instances for consistent usage throughout the framework.

In a typical scenario, the `Event` is created in the command handler using the EventFactory factory class, the `payload` is validated, and then saved to the event store:

```C#
// Default behavior - validation enabled
IEvent pe = new EventFactory().Create<TestAggregate>(TestCommand.Create, newId, newTest);

// Or disable validation using method chaining
IEvent pe = new EventFactory().NoValidate().Create<TestAggregate>(TestCommand.Create, newId, newTest);
await _nostify.PersistEventAsync(pe);
```

When a command becomes an `Event` the text name of the command becomes the topic name published to Kafka, so for this example the event handler, `OnTestCreated` would subscribe to the `Create_Test` topic.

#### Payload Validation

Event payloads are validated by default when using EventFactory. This is done by placing `ValidationAttribute` attributes on the properties of the Aggregate the Command is being performed on. This ensures that required properties are present and valid according to the specified command. Only properties present on the current payload will be validated, except for `[Required]` and `[RequiredFor()]`. 

```C#
// EventFactory validates by default - no need for manual validation
IEvent pe = new EventFactory().Create<TestAggregate>(TestCommand.Create, newId, newTest);
await _nostify.PersistEventAsync(pe);

// Or skip validation if needed
IEvent pe = new EventFactory().NoValidate().Create<TestAggregate>(TestCommand.Create, newId, newTest);
await _nostify.PersistEventAsync(pe);

// For events with no payload data (like delete operations)
IEvent pe = new EventFactory().CreateNullPayloadEvent(TestCommand.Delete, aggregateId);
await _nostify.PersistEventAsync(pe);
```

Most of the time, you will want to use `RequiredFor` instead of `Required` to mark a property as required for that specific command or list of commands. `Required` is still a valid validation attribute, but it will require that property to be present and not null for EVERY command:

```C#
public class TestAggregate : NostifyObject, IAggregate
{
    [RequiredFor("Create_Test")]
    public string Name { get; set; }
    
    [RequiredFor(["Update_Test", "Create_Test"])]
    public int Value { get; set; }
}
```

#### CreateNullPayloadEvent Method

The `CreateNullPayloadEvent` method is specifically designed for operations that don't require payload data, such as delete operations or events that only need to record that an action occurred. This method:

- Automatically disables validation (since there's no meaningful payload to validate)
- Creates an event with an empty object as payload
- Is ideal for delete operations, status changes, or audit trail events

```C#
// Typical delete operation - no payload data needed
IEvent deleteEvent = new EventFactory().CreateNullPayloadEvent(TestCommand.Delete, aggregateId);
await _nostify.PersistEventAsync(deleteEvent);

// With user and partition information
IEvent deleteEvent = new EventFactory().CreateNullPayloadEvent(TestCommand.Delete, aggregateId, userId, partitionKey);
await _nostify.PersistEventAsync(deleteEvent);
```

### Command

A `Command` is an `Event` that comes from the user interface.  All `Aggregate` classes should have a matching `Command` class where you must register all commands that the user may issue.  This class must extend the `NostifyCommand` class.  It will look like this by default:

```C#
public class TestCommand : NostifyCommand
{
  ///<summary>
  ///Base Create Command
  ///</summary>
  public static readonly TestCommand Create = new TestCommand("Create_Test", true);
  ///<summary>
  ///Base Update Command
  ///</summary>
  public static readonly TestCommand Update = new TestCommand("Update_Test");
  ///<summary>
  ///Base Delete Command
  ///</summary>
  public static readonly TestCommand Delete = new TestCommand("Delete_Test");


  public TestCommand(string name, bool isNew = false)
  : base(name, isNew)
  {

  }
}
```

The commands may then be handled in the `Apply()` method:

```C#
public override void Apply(Event eventToApply)
{
    if (eventToApply.command == TestCommand.Create || eventToApply.command == TestCommand.Update)
    {
        this.UpdateProperties<Test>(eventToApply.payload);
    }
    else if (eventToApply.command == TestCommand.Delete)
    {
        this.isDeleted = true;
    }
}
```

### Saga

The `Saga` pattern allows you to create multi-step, long lived transactions across multiple services and define rollback actions in case of failure to maintain data consistency. `nostify` does not require a particular method of implementation but provides a class structure and some basic functions to support implementing `Saga` orchestration.

## Setup

The template will use dependency injection to add a singleton instance of the Nostify class and adds HttpClient by default. You may need to edit these to match your configuration:

```C#

public  class  Program
{

  private  static  void  Main(string[] args)
  {
    var host = new  HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
      services.AddHttpClient();   

      var config = context.Configuration;

      //Note: This is the api key for the cosmos emulator by default
      string? apiKey = config.GetValue<string>("cosmosApiKey");
      string? dbName = config.GetValue<string>("cosmosDbName");
      string? endPoint = config.GetValue<string>("cosmosEndPoint");
      string? kafka = config.GetValue<string>("BrokerList");
      bool autoCreateContainers = config.GetValue<bool>("AutoCreateContainers");
      int defaultThroughput = config.GetValue<int>("DefaultContainerThroughput");
      bool verboseNostifyBuild = config.GetValue<bool>("VerboseNostifyBuild");
      bool useGatewayConnection = config.GetValue<bool>("UseGatewayConnection");
      var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

      var nostify = NostifyFactory.WithCosmos(
                                cosmosApiKey: apiKey,
                                cosmosDbName: dbName,
                                cosmosEndpointUri: endPoint,
                                createContainers: autoCreateContainers,
                                containerThroughput: defaultThroughput,
                                useGatewayConnection: useGatewayConnection)
                            .WithKafka(kafka)
                            .WithHttp(httpClientFactory)
                            .Build<InventoryItem>(verboseNostifyBuild); //Where T is the base aggregate of the service

      services.AddSingleton<INostify>(nostify);

      services.AddLogging();
    })
    .Build();
    
    host.Run();
  }
}
```

By default, the template will contain the single Aggregate specified. In the Aggregates folder you will find Aggregate and AggregateCommand class files already stubbed out. The AggregateCommand base class contains default implementations for Create, Update, and Delete. The `UpdateProperties<T>()` method will update any properties of the Aggregate with the value of the Event payload with the same property name. Note that `UpdateProperties<T>()` uses reflection, so extremely high performance may require writing code to directly handle the updates for your Aggregate's specific properties.

```C#

public  class  Test : NostifyObject, IAggregate
{

  public  Test()
  {
  }   

  public  bool  isDeleted { get; set; } = false;  

  public  static  string  aggregateType => "Test";


  public  override  void  Apply(Event  eventToApply)
  {
    if (eventToApply.command == TestCommand.Create || eventToApply.command == TestCommand.Update)
    {
      this.UpdateProperties<Test>(eventToApply.payload);
    }
    else  if (eventToApply.command == TestCommand.Delete)
    {
      this.isDeleted = true;
    }
  }
}  

```

## Basic Tasks

### Initializing Current State Container

The template will include a basic method to create, or recreate the current state container. It might become necesseary to recreate a container if a bug was introduced that corrupted the data, for instance. For the current state of an Aggreate, it is simple to recreate the container from the event stream. You will find the function to do so under Admin.

### Querying

There is a Queries folder to contain the queries for the Aggregate. Three basic queries are created when you spin up the template: Get Single `GET <AggreateType>/{aggregateId}`, Get All `GET <AggregateType>` (note: if this will return large amounts of data you may want to refactor the default query), and Rehydrate `GET Rehydrate<AggregateType>/{aggregateId}/{datetime?}` which returns the current state of the Aggregate directly from the event stream to the specified datetime.

To do your own query, simply add a new Azure Function per query, inject `HttpClient` and `INostify`, grab the container you want to query, and run a query with `GetItemLinqQueryable<T>()` using Linq syntax. Below is an example of the basic get single instance query included in the template generation.

```C#
public  class  GetTest
{
  private  readonly  HttpClient  _client;
  private  readonly  INostify  _nostify;

  public  GetTest(HttpClient  httpClient, INostify  nostify)
  {
    this._client = httpClient;
    this._nostify = nostify;
  }    

  [Function(nameof(GetTest))]
  public  async  Task<IActionResult> Run(
  [HttpTrigger("get", Route = "Test/{aggregateId:guid}")] HttpRequestData  req,
  Guid  aggregateId,
  ILogger  log)
  {
    Container  currentStateContainer = await  _nostify.GetCurrentStateContainerAsync<Test>();

    Test  retObj = await  currentStateContainer
                          .GetItemLinqQueryable<Test>()
                          .Where(x => x.id == aggregateId)
                          .FirstOrDefaultAsync();

    return  new  OkObjectResult(retObj);
  }
}
```

### Create New Aggregate

One of the "out of the box" commands handled by `nostify` is the `Create_Aggregate` command.  The template logic flow is:

- Create command is inbound via http post from user interface.  Typically this would indicate the user saved a new record.
- Body of post must contain JSON with all the properties to set on create. The `Apply()` method in the aggregate will call `UpdateProperties<T>()` which will match any properties in the JSON to the aggregate and set them automatically.  Any properties not in the JSON or properties in the JSON that do not match the aggregate will be ignored.  As such it is only necessary to send the properties that you want to set over the wire.
- In a typical application there would be validation of the command occuring, validating say that all required properties are set.  `nostify` does not dictate a validation pattern, use the one that makes the most sense for your application.  There would probably be some kind of auth here as well for most apps.
- The command handler creates an `Event` and persists it to the event store.  Note that the command registered must indicate a new record is being created by setting the `isNew` parameter to true:
`public static readonly TestCommand Create = new TestCommand("Create_Test", true);`
- The event publisher function is triggered by an event being written to the event store and publishes the event to Kafka.
- The create handler that is subscribed to the event will be triggered and update the projection containing the current state for the aggregate.

Other projections added will need to contain their own logic for handling the create event.  The event handler for update naming convention is: `On<AggregateName>Created`.  For example: "OnTestCreated".

### Update Aggregate

Updating the aggregate and its base current state projection is also handled by the templates. The template logic flow is:

- Update command comes in via http patch from user interface.  Typically this would indicate a user saved an existing record.
- Body of the request must contain a JSON object with a property `id` in the default template.
- The default event handler for the current state container will query the container for the aggregate id, get the current state, apply the update, then save the update to the container.  This is contained in the `ApplyAndPersistAsync<T>()` method.
- Body of patch must contain JSON with all the properties to update. The `Apply()` method in the aggregate will call `UpdateProperties<T>()` which will match any properties in the JSON to the aggregate and set them automatically.  Any properties not in the JSON or properties in the JSON that do not match the aggregate will be ignored.  As such it is only necessary to send the properties that you want to set over the wire.
- There may be more than one event to subscribe to to update an aggregate if you implement a more complex object. For instance, if you have a property of `List<T>` you may have another command that is issued from the UI that does an http PUT to replace objects in the list.

The function handling the update event naming convention is: `On<AggregateName>Updated`, for example: "OnTestUpdated".

### Delete Aggregate

Deleting an aggregate by default does not actually remove it from the current state container, it simple sets the `isDeleted` property to true.  Template logic flow is:

- Delete command comes in via http delete from user interface.  This indicates the user chose to delete an existing record.
- Url must contain a guid that matches the `id` property of the aggregate instance to mark as deleted.
- The command handler creates an `Event` and persists it to the event store.
- The `Event` is published to Kafka and then the event handler function applies the `Event` to the current state, which sets the `isDeleted` property to true.
- Queries to the current state container should take into account that it may contain deleted aggregates.

### Create New Projection Container

Note: for the following `Projection` examples we will be using the following projection `TestWithStatus` as an example:

```C#
public class TestWithStatus : NostifyObject, IProjection, IHasExternalData<TestWithStatus>
{
  public TestWithStatus()
  {

  }

  public static string containerName => "TestWithStatus";
  
  //Test properites
  public string testName { get; set; }
  public Guid? statusId { get; set; }
  public Guid? testTypeId { get; set; }

  //Status properties
  public string? statusName { get; set; }
  public string? statusCategory { get; set; }

  //Test Type properties
  public string? testType { get; set; }


  public override void Apply(Event eventToApply)
  {
      //Should update the command tree below to not use string matching
      if (eventToApply.command.name.Equals("Create_Test") || eventToApply.command.name.Equals("Update_Test"))
      {
          this.UpdateProperties<TestWithStatus>(eventToApply.payload);
      }
      else if (eventToApply.command.name.Equals("Update_Status") 
        || eventToApply.command.name.Equals("Create_Status"))
      {
        //If property names don't match up or there are duplicates, you can map them using a simple Dictionary
        var propMap = new Dictionary<string, string> {
          {"name","statusName"},
          {"category","statusCategory"}
        };
        //The method signature is a little different in this case
        this.UpdateProperties<TestWithStatus>(eventToApply.payload, propMap, true);
      }
      else if (eventToApply.command.name.Equals("Delete_Test"))
      {
          this.isDeleted = true;
      }
  }

  public class StatusName
  {
      public Guid id { get; set; }
      public string statusName { get; set; }
  }

  public async static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<TestWithStatus> projectionsToInit, INostify nostify, HttpClient httpClient = null, DateTime? pointInTime = null)
  {
    // If data exists within this service, even if a different container, use the container to get the data
    Container sameServiceEventStore = await nostify.GetEventStoreContainerAsync();
    
    //Use GetEventsAsync to get events from the same service, the selectors are a parameter list of the properties that are used to filter the events
    List<ExternalDataEvent> externalDataEvents = await ExternalDataEvent.GetEventsAsync<TestWithStatus>(sameServiceEventStore, 
        projectionsToInit, 
        p => p.testStatusId);

    if (httpClient != null)
    {
      // NEW: Use GetMultiServiceEventsAsync for efficient parallel querying of multiple external services
      // Pass EventRequester constructors directly as parameters to leverage the params array
      var multiServiceEvents = await ExternalDataEvent.GetMultiServiceEventsAsync(
        httpClient, 
        projectionsToInit,
        pointInTime,
        new EventRequester<TestWithStatus>(
          $"{Environment.GetEnvironmentVariable("LocationServiceUrl")}/api/EventRequest", 
          p => p.locationId),
        new EventRequester<TestWithStatus>(
          $"{Environment.GetEnvironmentVariable("StatusServiceUrl")}/api/EventRequest", 
          p => p.statusId)
        // Add more services as needed
      );
      externalDataEvents.AddRange(multiServiceEvents);

      // ALTERNATIVE: For single service calls, continue using GetEventsAsync
      // var locationEvents = await ExternalDataEvent.GetEventsAsync(httpClient,
      //              $"{Environment.GetEnvironmentVariable("LocationServiceUrl")}/api/EventRequest",
      //              projectionsToInit,
      //              p => p.locationId,
      //              pointInTime);
      // externalDataEvents.AddRange(locationEvents);
    }
    

    return externalDataEvents;
  }

}
```

Note the `GetExternalDataEventsAsync()` method. You must implement this such that it returns a `List<ExternalDataEvent>`. `ExternalDataEvent` contains a `Guid` proprty that points at the "base" aggregate `id` value, and a `List<Event>` property which are all of the events external to the base aggregate that need to be applied to get the projection to the current state (or point in time state if desired).

#### Multi-Service Event Querying

For efficient parallel querying of multiple external services, use the new `GetMultiServiceEventsAsync` method with `EventRequester` objects:

```csharp
// Query all services in parallel - EventRequester constructors passed directly as parameters
var events = await ExternalDataEvent.GetMultiServiceEventsAsync(
    httpClient, 
    projections,
    pointInTime,
    new EventRequester<YourProjection>(
        "https://service1.com/api/EventRequest", 
        p => p.service1Id),
    new EventRequester<YourProjection>(
        "https://service2.com/api/EventRequest", 
        p => p.service2Id, p => p.alternateService2Id) // Multiple foreign ID selectors
);
```

**Benefits of GetMultiServiceEventsAsync:**
- **Parallel Execution**: All external service calls execute simultaneously instead of sequentially
- **Better Performance**: Significantly faster when querying multiple services
- **Simplified Code**: Single method call instead of multiple GetEventsAsync calls
- **Point-in-Time Support**: DateTime parameter is automatically formatted as an ISO path parameter (`/2024-01-15T10:30:00.0000000Z`)

#### EventRequester Configuration

The `EventRequester<T>` class supports multiple foreign ID selectors for complex projection relationships:

```csharp
// Single foreign ID selector
new EventRequester<TestProjection>(url, p => p.foreignId)

// Multiple foreign ID selectors (query events for any matching ID)  
new EventRequester<TestProjection>(url, p => p.primaryId, p => p.secondaryId, p => p.fallbackId)

// List foreign ID selectors (convenient params syntax)
new EventRequester<TestProjection>(url, p => p.listOfIds, p => p.anotherListOfIds)

// Non-nullable function parameter constructors (for stricter type checking)
new EventRequester<TestProjection>(url, new Func<TestProjection, Guid>[] { p => p.requiredId })
new EventRequester<TestProjection>(url, new Func<TestProjection, List<Guid>>[] { p => p.requiredListOfIds })
new EventRequester<TestProjection>(url, 
    new Func<TestProjection, Guid>[] { p => p.requiredId },
    new Func<TestProjection, List<Guid>>[] { p => p.requiredListOfIds })
```

#### Mixed Constructor for Complex Relationships

For projections that have both individual foreign keys and collections of foreign keys, use the mixed constructor:

```csharp
// Example projection with both single and list foreign IDs
public class ComplexProjection : IUniquelyIdentifiable
{
    public Guid id { get; set; }
    public Guid? primarySiteId { get; set; }      // Single foreign key
    public Guid? ownerId { get; set; }            // Single foreign key
    public List<Guid?> relatedIds { get; set; }   // Collection of foreign keys
    public List<Guid?> departmentIds { get; set; } // Collection of foreign keys
}

// Mixed constructor combining single and list selectors
var eventRequester = new EventRequester<ComplexProjection>(
    url: "https://api.example.com/events",
    singleIdSelectors: new Func<ComplexProjection, Guid?>[] {
        p => p.primarySiteId,     // Single ID selector
        p => p.ownerId            // Another single ID selector
    },
    listIdSelectors: new Func<ComplexProjection, List<Guid?>>[] {
        p => p.relatedIds,        // List ID selector - gets all related IDs
        p => p.departmentIds      // Another list ID selector - gets all department IDs
    }
);

// Use with GetMultiServiceEventsAsync
var events = await ExternalDataEvent.GetMultiServiceEventsAsync(
    httpClient,
    projections,
    DateTime.Now, // Point in time (optional)
    eventRequester
);
```

**Benefits of Mixed Constructor:**
- **Flexibility**: Mix both single and list selectors in the same EventRequester
- **Clean API**: No need to manually flatten lists before creating the EventRequester
- **Backward Compatibility**: Existing code continues to work unchanged
- **Type Safety**: All selectors are strongly typed and validated at compile time

#### Non-Nullable Constructor Support

For scenarios requiring stricter null handling, use the non-nullable constructor overloads:

```csharp
// Non-nullable single ID selectors
var singleRequester = new EventRequester<TestProjection>(
    url: "https://api.example.com/events",
    new Func<TestProjection, Guid>[] { p => p.requiredId, p => p.primaryId }
);

// Non-nullable list ID selectors  
var listRequester = new EventRequester<TestProjection>(
    url: "https://api.example.com/events",
    new Func<TestProjection, List<Guid>>[] { p => p.requiredIds, p => p.relatedIds }
);

// Mixed non-nullable constructor
var mixedRequester = new EventRequester<TestProjection>(
    url: "https://api.example.com/events",
    singleIdSelectors: new Func<TestProjection, Guid>[] { p => p.requiredId },
    listIdSelectors: new Func<TestProjection, List<Guid>>[] { p => p.requiredIds }
);
```

**Benefits of Non-Nullable Constructors:**
- **Stricter Type Checking**: Ensures function parameters cannot return null
- **Better Intent**: Clearly indicates required non-nullable ID fields
- **Runtime Safety**: Reduces null reference exceptions in ID processing
- **API Consistency**: Provides both nullable and non-nullable options for all scenarios

#### Single Service Event Querying

For single external service calls, continue using the traditional `GetEventsAsync` method:

```csharp
var locationEvents = await ExternalDataEvent.GetEventsAsync(httpClient,
    $"{Environment.GetEnvironmentVariable("LocationServiceUrl")}/api/EventRequest",
    projectionsToInit,
    p => p.locationId,
    pointInTime);
```

For Events in a seperate service you must pass an HttpClient and the url of the `EventRequest` endpoint for the service. The Init function with make an http call to the endpoint to get the events. You'll probably need to authenticate and get a bearer token from your authentication service to pass along with the request in a production environment.

#### EventRequest Endpoint Configuration

The `EventRequest` endpoint in each nostify service automatically supports point-in-time queries via a DateTime path parameter:

```csharp
[Function(nameof(EventRequest))]
public async Task<List<Event>> Run(
    [HttpTrigger("post", Route = "EventRequest/{pointInTime:datetime?}")] HttpRequestData req,
    [FromBody] List<Guid> aggregateRootIds,
    DateTime? pointInTime,
    FunctionContext context,
    ILogger log)
```

**Endpoint URL Format:**
- Current events: `POST /api/EventRequest`
- Point-in-time events: `POST /api/EventRequest/2024-01-15T10:30:00.0000000Z`

**DateTime Parameter Constraints:**
- Must be URL-encoded for special characters
- Recommended format: ISO 8601 (`2024-01-15T10:30:00.0000000Z`)
- Supports nullable DateTime (`{pointInTime:datetime?}`)
- Simple dates like `8/25/2025` require URL encoding as `8%2F25%2F2025`

**Body Parameters:**
- Content-Type: `application/json`
- Body: Array of aggregate root IDs (`List<Guid>`)

This endpoint is automatically generated in each nostify service and is used by `GetEventsAsync` and `GetMultiServiceEventsAsync` methods to retrieve events for external projections.

### Create New Projection

A `Projection` will have a "base" aggregate when it is defined. Projection create event handlers should subscribe to the create event of the base aggregate.

Adding a new instance of a projection requires implementing the `Apply()` method to handle all necessary events, and the `GetExternalDataEventsAsync()` method to get events external to the base aggregate when initializing new instances of the projection.

This method is called in the event handler function to update the projection with any exsiting external data and then applied and saved to the projection container along with the `Event` signifying the creation of the `Test`

```C#
 //Get projection container
  Container projectionContainer = await _nostify.GetProjectionContainerAsync<TestWithStatus>();
  //Update projection container
  var newProj = await projectionContainer.ApplyAndPersistAsync<TestWithStatus>(newEvent);
  //Initialize projection with external data
  await newProj.InitAsync(_nostify, _httpClient);
```

  The naming convention of the event handler is: `On<Base Aggregate Name>Created_For_<Projection Name>`.  For example: "OnTestCreated_For_TestWithStatus".  The create event of the base aggregate is the only event to subscribe to in this case for most implementations.

### Update Projection

Updating a `Projection` works the same as updating the current state projection of an aggregate, except you're more likely to be subscribing to multiple events and you may be subscribing to various events from multiple services.

For instance, with our `TestWithStatus` projection, we will need to subscribe to the update event for both `Test` and `Status` aggregates to capture and handle them in the `Apply()` method, see example above.

This means we will need two event handler functions, `OnTestUpdated_For_TestWithStatus` and `OnStatusUpdated_For_TestWithStatus`.  Note the naming convention.

They will both take in their respective events and update the projection container.  Using the `ApplyAndPersistAsync()` method for updates to the base aggregate will automatically query the projection using the `id` value and call `Apply()` then save the updated projection back to the data store.

### Delete Projection

For most projections is it appriopriate to delete the item out of the container when the base aggregate is deleted (the `isDeleted` property is set to `true`). 

The naming convention of the event handler is: `On<Base Aggregate Name>Deleted_For_<Projection Name>`.  For example: "OnTestDeleted_For_TestWithStatus". The delete event of the base aggregate is the only event to subscribe to in this case for most implementations.

### Event Handlers

Events are things that have already happened and need to be handled by the system. A Command, as discussed elsewhere in the documentation, becomes an Event after validation.  As an example: the user clicks a button, the system issues an http POST to the command endpoint, it passes authentication and authorization, then the data in the body of the http call is validated, then the command code runs to write an Event to the Event Store with the pertinant payload data.

As such there should not be any validation to perform in an Event Handler, the "thing" has already happened.  An Event Handler is there to process the Event and perform any necessary state updates such as updating the data stored in a Projection container. After the Event is stored, it is published to the event bus (Kafka being used that way in this case), and then the Event Handlers pick it up by subscribing to the event.  Kafka makes sure each Event Handler receives each event it is subscribed to.

The Event should be applied to the object's (Aggregate/Projection) current state by calling the `Apply()` method. This method needs to take an Event and change the state of the object based on the Event.  A "Create" Event might start with a new instance of the object and then update any properties of the object based off the Event's payload.  

#### Helper Methods

There are a number of methods in the framework to help create Event Handlers. They remove writing a large amount of boilerplate code in most circumstances. There may be Events where the helpers don't really apply, or may be scenarios where you require more performance, but 99% of the time you should leverage these methods to simplify your code.  The templates will create Event Handlers with these methods

##### Create/Update From Single Event

A single "Create" or "Update" Event for an Aggregate or the base Aggregate of a Projection can be handled with a couple lines of code with these helper methods. For an Event Handler for an Aggregate subscribed to a Kafka topic you simply pull the Event out of the triggerEvent, make sure it's not null, then get the container you're going to update out of Cosmos and call the `ApplyAndPersistAsync<T>([event])` method. These will get automatically created by the template.

```C#
public async Task Run([KafkaTrigger(<Trigger Details Here>)] NostifyKafkaTriggerEvent triggerEvent,
    ILogger log)
{
    Event? newEvent = triggerEvent.GetEvent();
    try
    {
        if (newEvent != null)
        {
            //Update aggregate current state projection
            Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync<Test>();
            await currentStateContainer.ApplyAndPersistAsync<Test>(newEvent);
        }                           
    }
    catch (Exception e)
    {
        await _nostify.HandleUndeliverableAsync(nameof(FunctionName), e.Message, newEvent);
    }

    
}
```

The `ApplyAndPersistAsync()` method will deduce the partition key of the object the Event is being applied to by pulling it from the Event and then either create a new instance of the object if needed or do a point read on the database using the `id` Guid property. This keeps RU consumption down and performance high. Once the object has been created or found, the Event is fed into the object's `Apply([event])` method, which is specified by the developer for each object type. The results are then saved back into the database, either creating a new document or overwriting the existing one. In the example above errors are handled by writing them off into a seperate undeliverable container. The above example is for an Aggregate, which only need to handle internal data updates, ie - Events that update Aggregates will have all of the data needed to perform the state changes internal to the Event. This may not hold true for Projections, they may require getting additional data external to the Event. 

##### Multi Apply From A Single Event

Frequently with Projections you will need to apply a single Event to multiple Projections to update data. Use the `MultiApplyAndPersistAsync<P>()` method to facilitate this. You will need to get a reference to the bulk enabled Container and query for the Projections to update. This will frequently follow the pattern shown below:

```C#
public async Task Run([KafkaTrigger(<Trigger details go here>)] NostifyKafkaTriggerEvent triggerEvent,
        ILogger log)
{
    Event? newEvent = triggerEvent.GetEvent();
    try
    {
        if (newEvent != null)
        {
            //Update projection container
            Container projectionContainer = await _nostify.GetBulkProjectionContainerAsync<TestWithStatus>();
            // Get all items with this status id
            List<Guid> testsToUpdate = await projectionContainer.GetItemLinqQueryable<TestWithStatus>()
                .Where(i => i.testStatusId == newEvent.aggregateRootId)
                .Select(i => i.id)
                .ReadAllAsync();
            // Multi apply the event
            await _nostify.MultiApplyAndPersistAsync<TestWithStatus>(projectionContainer, newEvent, testsToUpdate);
        }                       

    }
    catch (Exception e)
    {
        await _nostify.HandleUndeliverableAsync(nameof(OnTestStatusUpdated_For_TestWithStatus), e.Message, newEvent);
    }

    
}
```

## Advanced Features

### Error Handling and Undeliverable Events

`nostify` provides robust error handling mechanisms for event processing failures. When an event handler fails to process an event, the framework can capture the failure details and store them for analysis and potential reprocessing.

#### UndeliverableEvent Class

The `UndeliverableEvent` class captures events that fail to process:

```C#
public class UndeliverableEvent
{
    public Guid id { get; set; }
    public string functionName { get; set; }
    public string errorMessage { get; set; }
    public Event undeliverableEvent { get; set; }
    public Guid aggregateRootId { get; set; }
}
```

#### Handling Undeliverable Events

Use the `HandleUndeliverableAsync` method in your event handlers to capture processing failures:

```C#
try
{
    // Event processing logic
    Container currentStateContainer = await _nostify.GetCurrentStateContainerAsync<Test>();
    await currentStateContainer.ApplyAndPersistAsync<Test>(newEvent);
}
catch (Exception e)
{
    await _nostify.HandleUndeliverableAsync(nameof(OnTestCreated), e.Message, newEvent);
}
```

#### Custom Exception Handling

The framework includes a `NostifyException` class for library-specific exceptions:

```C#
public class NostifyException : Exception
{
    public NostifyException(string message) : base(message) { }
}
```

### Bulk Operations and Performance

#### Bulk Container Operations

For high-throughput scenarios, `nostify` provides bulk operations that can process multiple items efficiently:

```C#
// Get a bulk-enabled container
Container bulkContainer = await _nostify.GetBulkProjectionContainerAsync<TestProjection>();

// Bulk delete operations
await bulkContainer.BulkDeleteAsync<TestProjection>(projectionIdsToDelete);

// Bulk delete from Kafka trigger events
await bulkContainer.BulkDeleteFromEventsAsync<TestProjection>(kafkaTriggerEvents);
```

#### Multi-Apply Operations

Apply a single event to multiple projections efficiently:

```C#
// Apply single event to multiple projections by ID
await _nostify.MultiApplyAndPersistAsync<TestProjection>(
    bulkContainer, 
    eventToApply, 
    projectionIds, 
    batchSize: 100
);

// Apply single event to multiple projection objects
await _nostify.MultiApplyAndPersistAsync<TestProjection>(
    bulkContainer, 
    eventToApply, 
    projectionsToUpdate, 
    batchSize: 100
);
```

#### Bulk Event Processing

Process multiple events in batches:

```C#
await _nostify.BulkPersistEventAsync(
    events, 
    batchSize: 100, 
    allowRetry: true, 
    publishErrorEvents: false
);

await _nostify.BulkApplyAndPersistAsync<TestProjection>(
    bulkContainer, 
    "id", 
    kafkaEvents, 
    allowRetry: true, 
    publishErrorEvents: false
);
```

### Advanced Querying and Cosmos Extensions

#### Safe Patch Operations

The framework provides safe patch operations with result tracking:

```C#
// Safe patch with result information
PatchItemResult result = await container.SafePatchItemAsync<TestAggregate>(
    id, 
    partitionKey, 
    patchOperations
);

if (result.PatchedSuccessfully)
{
    // Handle successful patch
}
else if (result.NotFound)
{
    // Handle item not found
}
```

#### Custom LINQ Query Extensions

Use the `NostifyLinqQuery` class for advanced query operations:

```C#
var linqQuery = new NostifyLinqQuery();
var feedIterator = linqQuery.GetFeedIterator(queryable);

// Process results with feed iterator
while (feedIterator.HasMoreResults)
{
    var response = await feedIterator.ReadNextAsync();
    foreach (var item in response)
    {
        // Process each item
    }
}
```

### Testing Utilities

The framework includes testing utilities to facilitate unit testing:

#### Mock HTTP Request Data

```C#
// Create empty HTTP request for testing
var request = MockHttpRequestData.Create();

// Create HTTP request with specific data
var testData = new { name = "Test", value = 42 };
var request = MockHttpRequestData.Create(testData);
```

#### Mock HTTP Response Data

```C#
public class MockHttpResponseData : HttpResponseData
{
    public HttpStatusCode StatusCode { get; set; }
    public HttpHeadersCollection Headers { get; }
    public Stream Body { get; set; }
    public HttpCookies Cookies { get; }
}
```

### Projection Initialization and External Data

#### Advanced External Data Handling

For projections requiring data from multiple external sources:

```C#
public async static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(
    List<TestProjection> projectionsToInit, 
    INostify nostify, 
    HttpClient httpClient = null, 
    DateTime? pointInTime = null)
{
    var externalEvents = new List<ExternalDataEvent>();
    
    // Get events from same service (different container)
    Container eventStore = await nostify.GetEventStoreContainerAsync();
    var sameServiceEvents = await ExternalDataEvent.GetEventsAsync<TestProjection>(
        eventStore, 
        projectionsToInit, 
        p => p.relatedIds,           // Single foreign ID
        p => p.nestedObjectIds       // List of foreign IDs
    );
    externalEvents.AddRange(sameServiceEvents);
    
    // Get events from external services via HTTP
    if (httpClient != null)
    {
        var externalServiceEvents = await ExternalDataEvent.GetEventsAsync(
            httpClient,
            "https://external-service/api/EventRequest",
            projectionsToInit,
            p => p.externalServiceId
        );
        externalEvents.AddRange(externalServiceEvents);
    }
    
    return externalEvents;
}
```

#### Projection Container Initialization

Initialize entire projection containers:

```C#
// Initialize all projections in a container
await nostify.ProjectionInitializer.InitContainerAsync<TestProjection, TestAggregate>(
    nostify, 
    httpClient, 
    partitionKeyPath: "/tenantId", 
    loopSize: 1000
);

// Initialize only uninitialized projections
await nostify.ProjectionInitializer.InitAllUninitialized<TestProjection>(
    nostify, 
    httpClient, 
    maxloopSize: 10
);
```

### Rehydration and Point-in-Time Queries

#### Aggregate Rehydration

Reconstruct aggregate state from event stream:

```C#
// Rehydrate to current state
TestAggregate aggregate = await _nostify.RehydrateAsync<TestAggregate>(aggregateId);

// Rehydrate to specific point in time
DateTime pointInTime = DateTime.UtcNow.AddDays(-7);
TestAggregate historicalAggregate = await _nostify.RehydrateAsync<TestAggregate>(
    aggregateId, 
    pointInTime
);
```

#### Projection Rehydration

Rehydrate projections with external data:

```C#
TestProjection projection = await _nostify.RehydrateAsync<TestProjection, TestAggregate>(
    projectionId, 
    httpClient
);
```

### Saga Pattern Implementation

`nostify` provides comprehensive support for the Saga pattern to handle long-running, distributed transactions.

#### Saga Class Structure

```C#
public class Saga : ISaga
{
    public Guid id { get; set; }
    public string name { get; set; }
    public SagaStatusEnum status { get; set; }
    public DateTime createdOn { get; set; }
    public DateTime? executionCompletedOn { get; set; }
    public DateTime? executionStart { get; set; }
    public DateTime? rollbackStartedOn { get; set; }
    public DateTime? rollbackCompletedOn { get; set; }
    public string? errorMessage { get; set; }
    public string? rollbackErrorMessage { get; set; }
    public List<SagaStep> steps { get; set; } = new List<SagaStep>();
}
```

#### Saga Status Enumeration

```C#
public enum SagaStatusEnum
{
    Created,
    Executing,
    Completed,
    RollingBack,
    RollbackCompleted,
    Failed,
    RollbackFailed
}
```

#### Saga Step Implementation

```C#
public class SagaStep : ISagaStep
{
    public Guid id { get; set; }
    public string name { get; set; }
    public int order { get; set; }
    public SagaStepStatusEnum status { get; set; }
    public DateTime? executionStart { get; set; }
    public DateTime? executionCompleted { get; set; }
    public DateTime? rollbackStart { get; set; }
    public DateTime? rollbackCompleted { get; set; }
    public string? errorMessage { get; set; }
    public string? rollbackErrorMessage { get; set; }
    public object? stepData { get; set; }
    public object? rollbackData { get; set; }
}
```

#### Using Sagas

```C#
// Create and start a saga
var saga = new OrderProcessingSaga();
saga.steps.Add(new SagaStep { name = "ReserveInventory", order = 1 });
saga.steps.Add(new SagaStep { name = "ProcessPayment", order = 2 });
saga.steps.Add(new SagaStep { name = "ShipOrder", order = 3 });

await saga.StartAsync(nostify);

// Handle successful step completion
await saga.HandleSuccessfulStepAsync(nostify, stepData);

// Handle failures and rollback
await saga.StartRollbackAsync(nostify);
```

### Container Management

#### Dynamic Container Creation

```C#
// Get containers with specific configuration
Container eventStore = await _nostify.GetEventStoreContainerAsync(allowBulk: true);
Container currentState = await _nostify.GetCurrentStateContainerAsync<TestAggregate>("/customPartitionKey");
Container bulkProjection = await _nostify.GetBulkProjectionContainerAsync<TestProjection>("/tenantId");

// Get custom containers
Container customContainer = await _nostify.GetContainerAsync(
    "CustomContainerName", 
    bulkEnabled: true, 
    partitionKeyPath: "/customKey"
);
```

#### Database Management

```C#
// Get database reference with bulk operations
DatabaseRef database = await _nostify.Repository.GetDatabaseAsync(allowBulk: true);

// Get database with specific throughput
DatabaseRef database = await _nostify.Repository.GetDatabaseAsync(allowBulk: true, throughput: 1000);

// Get containers with specific settings
Container container = await _nostify.Repository.GetContainerAsync(
    containerName: "Events",
    partitionKeyPath: "/tenantId",
    allowBulk: false,
    throughput: 400,
    verbose: true
);
```

### Validation System

The framework includes a validation system for ensuring data integrity:

#### RequiredForAttribute

Use the `RequiredForAttribute` to specify when fields are required based on event type:

```C#
public class TestEvent : Event
{
    [RequiredFor(EventTypeEnum.Created)]
    public string name { get; set; }
    
    [RequiredFor(EventTypeEnum.Updated, EventTypeEnum.Deleted)]
    public string description { get; set; }
}
```

#### Validation Extensions

Validate objects and events:

```C#
// Validate an object
bool isValid = myObject.IsValid();

// Get validation messages
var validationMessages = myObject.GetValidationMessages();

// Validate with specific event type
bool isValidForEventType = myEvent.IsValidForEventType(EventTypeEnum.Created);
```

## Performance Considerations

### Bulk Operations

- Always use bulk-enabled containers for high-throughput scenarios
- Configure appropriate batch sizes (typically 100-1000 items)
- Enable retry logic for transient failures
- Monitor RU consumption and adjust throughput accordingly

### Query Optimization

- Use partition keys effectively to avoid cross-partition queries
- Implement proper indexing strategies
- Use `ReadAllAsync()` for small result sets
- Use feed iterators for large result sets

### Memory Management

- Dispose of containers and clients properly
- Use appropriate batch sizes for bulk operations
- Consider using pagination for large data sets
- Monitor memory usage in long-running processes

### Event Store Optimization

- Use appropriate TTL settings for event data
- Implement event archiving strategies for long-term storage
- Consider event snapshotting for frequently accessed aggregates
- Monitor and optimize event store throughput
