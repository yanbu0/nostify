
# nostify

Dirtball simple, easy to use, HIGHLY opinionated .Net framework for Azure to spin up microservices that can scale to global levels.

"Whole ass one thing, don't half ass two things" - Ron Swanson

This framework is intended to simplify the implementation of the ES/CQRS, microservice, and materialized view patterns in a specific tech stack. It also assumes some basic familiarity with domain driven design.

When should I NOT use this?

The framework makes numerous assumptions to cut down on complexity. It has dependencies on Azure components, notably Cosmos. If you need to accommodate a wide range of possible technology stacks, or want lots of flexibility in how to implement, this may not be for you. If you are going to ignore the tech stack requirements there are other libraries you should look at.

When should I use this?

You should consider using this if you are using .Net and Azure and want to follow a strong set of guidelines to quickly and easily spin up services that can massively scale without spending tons of time architecting it yourself.

### Current Status

- Brought Kafka into the mix

- Documentation still in process below!

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

![image](https://github.com/yanbu0/nostify/assets/26099646/be657901-89c0-4310-9502-61b2125368ab)

Projections that contain data from multiple Aggregates can be updated by Event Handlers from other microservices. Why would this happen? Well say you have a Bank Account record. If we were using a relational database for a data store we'd have to either run two queries or do a join to get the Bank Account and the name of the Account Manager. Using the CQRS model, we can "pre-render" a projection that contains both the account info and the account manager info without having to join tables together. This example is obviously very simple, but in a complex environment where you're joining together dozens of tables to create a DTO to send to the user interface and returning 100's of thousands or millions of records, this type of architecture can dramatically improve BOTH system performance and throughput.

![image](https://github.com/yanbu0/nostify/assets/26099646/fe8741c4-6547-482e-a03b-2b2635925602)

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
- All state changes must be applied using the `Apply()` method, either directly or by using the `ApplyAndPersist()` method.  The current state projection of an aggregate is simply the sum of all the events in the event store applied to a new instance of that aggregate object.
- In `nostify` only the changed properties should be included when creating an `Event`, best practice is to not send the entire aggregate.
- A service generally contains one or more aggregates.  A read-only service, such as for BI purposes might not.  Domain boundaries should be respected when grouping aggregates together in a service.  IE - grouping a `WorkOrder` aggregate in the same service as a `WorkOrderStatus` aggregate (which might be an aggregate if your application allows users to add and update them) might make sense, where as putting a `PurchaseRequest` and a `WorkOrder` in the same service might not.  This is an art not a science so do what makes sense to your application.  It is theoretically possible to completely abandon the microservice concept and group an entire application into a single service, but probably not a good idea for scalability and maintainability.

#### Create

A "base" aggregate is created when you use the dotnet cli to create a new `nostify` service

```powershell
dotnet new nostify -ag <Your_Aggregate_Name> -p <Port Number To Run on Locally>
```

If you are adding a new aggregate to an existing service, run the below cli command from the Aggregates directory.

```powershell
dotnet new nostifyAggregate -ag <Your_Aggregate_Name>
```

### Projection

A projection encapsulates the logic surrounding the "Q" or Query part of the CQRS pattern.

The current state of an aggregate is the sum of all the events for that aggregate in the event store.  However, querying the entire stream of events and applying them every time you want to send the current state of an aggregate to the user interface can be inefficient, even more so if you are trying to compose a DTO with properties from multiple aggregates across services.

The solution to this in `nostify` is the projection pattern.  A projection defines a set of properties from one or more aggregates and then stores the current state of those properties as updated by the event stream.  Every projection will have a "base" aggregate that will be the trigger to create a new persisted instance of the projection, and will define any necessary queries for getting data from external aggregates.  

#### Rules

- No command is ever performed on a projection, they are updated by subscribing to and handling events on their component aggregates.
- A projection will have a "base" aggregate and the `id` property of the projection will match the `id` property of the base aggregate.
- A projection must implement the `NostifyObject` abstract class and the `IProjection` interface.
- The `IProjection.SeedExternalDataAsync()` method must be implemented to handle getting data from aggregates other than the base aggregate on create.  It is called in the event handler for create of the base aggregate.
- The `IProjection.InitContainerAsync()` method must contain logic to query all the data necessary to populate the entire projection container, from base aggregate and external aggregates.  It is called manually when first creating the container, or when having to recreate the container to remedy data corruption.

#### Create

A projection must be added to an existing service.  Base aggregate must already exist. From the Projections directory:

```powershell
dotnet new nostifyProjection -ag <Base_Aggregate_Name> --projectionName <Projection_Name>
```

### Event

An `Event` captures a state change to the application.  Generally, this is caused by the user issuing a command such as "save".  When a command comes in from the front end to the endpoint, the http triggers a command handler function which validates the command, composes an `Event` and persists it to the event store.  Note that while a `Command` is always an `Event`, an `Event` is not necessarily always a `Command`.  It is possible for an `Event` to originate elsewhere, say from an IoT device for example.

In a typical scenario, the `Event` is created in the command handler, and then saved to the event store:

```C#
Event pe = new Event(TestCommand.Create, newId, newTest);
await _nostify.PersistEventAsync(pe);
```

When a command becomes an `Event` the text name of the command becomes the topic name published to Kafka, so for this example the event handler, `OnTestCreated` would subscribe to the `Create_Test` topic.

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
    if (eventToApply.command == _ReplaceMe_Command.Create || eventToApply.command == _ReplaceMe_Command.Update)
    {
        this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
    }
    else if (eventToApply.command == _ReplaceMe_Command.Delete)
    {
        this.isDeleted = true;
    }
}
```

## Setup

The template will use dependency injection to add a singleton instance of the Nostify class and adds HttpClient by default. You may need to edit these to match your configuration:

```C#

public  class  Program
{

  private  static  void  Main(string[] args)
  {
    var  host = new  HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
      services.AddHttpClient();   

      var  config = context.Configuration;

      //Note: This is the api key for the cosmos emulator by default
      string  apiKey = config.GetValue<string>("apiKey");
      string  dbName = config.GetValue<string>("dbName");
      string  endPoint = config.GetValue<string>("endPoint");
      string  kafka = config.GetValue<string>("BrokerList");
      string  aggregateRootCurrentStateContainer = "SiteCurrentState";

      var  nostify = new  Nostify(apiKey, dbName, endPoint, kafka, aggregateRootCurrentStateContainer);

      services.AddSingleton<INostify>(nostify);

      services.AddLogging();
    })
    .Build();
    
    host.Run();
  }
}
```

By default, the template will contain the single Aggregate specified. In the Aggregates folder you will find Aggregate and AggregateCommand class files already stubbed out. The AggregateCommand base class contains default implementations for Create, Update, and Delete. The `UpdateProperties<T>()` method will update any properties of the Aggregate with the value of the Event payload with the same property name.

```C#

public  class  TestCommand : NostifyCommand
{
  ///<summary>
  ///Base Create Command
  ///</summary>
  public  static  readonly  TestCommand  Create = new  TestCommand("Create_Test", true);
  ///<summary>
  ///Base Update Command
  ///</summary>
  public  static  readonly  TestCommand  Update = new  TestCommand("Update_Test");
  ///<summary>
  ///Base Delete Command
  ///</summary>
  public  static  readonly  TestCommand  Delete = new  TestCommand("Delete_Test");    

  public  TestCommand(string  name, bool  isNew = false)
  : base(name, isNew)
  {
  }

}
```

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
  [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Test/{aggregateId:guid}")] HttpRequestData  req,
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

Other projections added will need to contain their own logic for handling the create event.  The event handler for update naming convention is: `On<AggregateName>Created`.  For example: "OnLocationCreated".


### Update Aggregate

Updating the aggregate and its base current state projection is also handled by the templates. The template logic flow is:

- Update command comes in via http patch from user interface.  Typically this would indicate a user saved an existing record.
- Body of the request must contain a JSON object with a property `id` in the default template.
- The default event handler for the current state container will query the container for the aggregate id, get the current state, apply the update, then save the update to the container.  This is contained in the `ApplyAndPersistAsync<T>()` method.
- Body of patch must contain JSON with all the properties to update. The `Apply()` method in the aggregate will call `UpdateProperties<T>()` which will match any properties in the JSON to the aggregate and set them automatically.  Any properties not in the JSON or properties in the JSON that do not match the aggregate will be ignored.  As such it is only necessary to send the properties that you want to set over the wire.

The function handling the update event naming convention is: `On<AggregateName>Updated`, for example: "OnLocationUpdated".

### Delete Aggregate

Deleting an aggregate by default does not actually remove it from the current state container, it simple sets the `isDeleted` property to true.  Template logic flow is:

- Delete command comes in via http delete from user interface.  This indicates the user chose to delete an existing record.
- Url must contain a guid that matches the `id` property of the aggregate instance to mark as deleted.
- The command handler creates an `Event` and persists it to the event store.
- The `Event` is published to Kafka and then the event handler function applies the `Event` to the current state, which sets the `isDeleted` property to true.
- Queries to the current state container should take into account that it may contain deleted aggregates.

### Create New Projection Container

When adding a new `Projection` you must take into account the `Aggregate` records that may already exist and account for them in the `InitContainerAsync()` method.  When the projection is first added, it implements `NosifyObject` and `IProjection` in the template.  `IProjection` requires the `InitContainerAsync()` method, but, since we don't know the details of the projection at the time of create, the template simply contains a `throw new NotImplementedException()`.  

You must implement the method such that calling it gets all of the data necessary to either create or re-create the projection container.  An example for a projection called `LocationWithSite` with the "base" aggregate of `Location` that also references an aggregate `Site` that is contained in a seperate service:

```C#
public static async Task InitContainerAsync(INostify nostify, HttpClient? httpClient = null)
{
    var lwsContainer = await nostify.GetBulkProjectionContainerAsync<LocationWithSite>();

    List<SiteName> allSitesWithName = await httpClient.GetFromJsonAsync<List<SiteName>>("http://localhost:7071/api/Site") ?? new List<SiteName>();
    List<Location> locations = await (await nostify.GetCurrentStateContainerAsync<Location>()).GetItemLinqQueryable<Location>().ReadAllAsync();

    var lwsList = new List<LocationWithSite>();
    locations.ForEach(l =>{
        var lws = new LocationWithSite(){
            id = l.id,
            siteId = l.siteId,
            isDeleted = l.isDeleted,
            locationName = l.locationName,
            siteName = allSitesWithName.FirstOrDefault(s => s.id == l.siteId)?.siteName
        };
        
        lwsList.Add(lws);
    });

    await nostify.DoBulkUpsertAsync(lwsContainer, lwsList);
}
```

Note that the code gets the projection container with "bulk" mode enabled to be able to handle large amounts of data writes.  It then queries the `Site` service and gets all.  If you have substaintial amounts of data, you may need to loop through multiple queries. (If there are more than one aggregate other than the base aggregate in your implementation, you will need additional queries.) After that, it grabs all instances of the base aggregate and then composes a `List<LocationWithSite>` with the appriopriate values.  It then saves them to the container, and is able to use the `DoBulkUpsert()` which will write to the container across many threads.

Now that the method is implemented, the container can be initialized and populated with data by calling `LocationWithSite.InitContainerAsync()`.  The template creates an http triggered function to do this for you with a simple http post.

### Create New Projection

A `Projection` will have a "base" aggregate when it is defined. Projection create event handlers should subscribe to the create event of the base aggregate.

Adding a new instance of a projection requires implementing a method in the `IProjection` interface: `SeedExternalDataAsync()`. This method grabs all data from aggregates external to the base aggregate for this particular instance. This is different from the Init function which grabs for all since we're only creating a single record in the projection container.

This method must query any external data needed and return an `Event` to update the projection.  For our example of `LocationWithSite`:

```C#
public async Task<Event> SeedExternalDataAsync(INostify nostify, HttpClient? httpClient = null)
{
    string? siteName = null;
    if (siteId != null)
    {
        //Site Name
        var siteWithName = await httpClient.GetFromJsonAsync<SiteName>($"http://localhost:7071/api/Site/{siteId}");
        siteName = siteWithName?.siteName;
    }
    Event e = new Event(LocationCommand.Update, id, new { id, siteName });
    return e;
}
```

This method is called in the event handler function to update the projection with any exsiting external data and then applied and saved to the projection container along with the `Event` signifying the creation of the `Location`

```C#
 //Get projection container
  Container projectionContainer = await _nostify.GetProjectionContainerAsync<LocationWithSite>();

  //Create projection
  LocationWithSite proj = new LocationWithSite();
  //Apply
  proj.Apply(newEvent);
  //Get external data
  Event externalData = await proj.SeedExternalDataAsync(_nostify, _httpClient);
  //Update projection container
  await projectionContainer.ApplyAndPersistAsync<LocationWithSite>(new List<Event>(){locationCreated,externalData});
  ```

  The naming convention of the event handler is: `On<Base Aggregate Name>Created_For_<Projection Name>`.  For example: "OnLocationCreated_For_LocationWithSite".

### Update Projection

### Delete Projection



