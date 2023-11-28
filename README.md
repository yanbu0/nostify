# nostify
Dirtball simple, easy to use, HIGHLY opinionated .Net framework for Azure to spin up microservices that can scale to global levels.

"Whole ass one thing, don't half ass two things" - Ron Swanson

This framework is intended to simplify the implementation of the ES/CQRS, microservice, and materialzed view patterns in a specific tech stack.  It also assumes some basic familiarity with domain driven design.

When should I NOT use this?<br/>
The framework makes numerous assumptions to cut down on complexity.  It has dependencies on Azure components, notably Cosmos.  If you need to accomodate a wide range of possible technology stacks, or want lots of flexibility in how to implement, this may not be for you.  If you are going to ignore the tech stack requirements there are other libraries you should look at.

When should I use this?<br/>
You should consider using this if you are using .Net and Azure and want to follow a strong set of guidelines to quickly and easily spin up services that can massively scale without spending tons of time architechting it yourself.<br/>
<br/>
<strong>Current Status</strong>
- Brought Kafka into the mix
- Documentation still in process below!
<br/>

<strong>Getting Started</strong><br/>
To run locally you will need to install some dependencies:<br/>
- Azurite: npm install azurite<br/>
- Azurite VS Code Extension: https://marketplace.visualstudio.com/items?itemName=Azurite.azurite<br/>
- Docker Desktop: https://www.docker.com/products/docker-desktop/<br/>
- Confluent CLI: https://docs.confluent.io/confluent-cli/current/install.html<br/>
- Cosmos Emulator: https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-develop-emulator?tabs=windows%2Ccsharp&pivots=api-nosql<br/><br/>

To spin up a nostify project:
```
dotnet new -i nostify
dotnet new nostify -ag <Your_Aggregate_Name>
dotnet restore
```
This will install the templates, create the default project based off your Aggregate, and install all the necessary libraries.
<br/>
<br/>
<strong>Architecture</strong><br/>
<br/>
The library is designed to be used in a microservice pattern (although not necessarily required) using an Azure Function App api and Cosmos as the data store.<br/><br/>
You should set up a Function App and Cosmos per Aggregate Microservice.<br/><br/>
![image](https://user-images.githubusercontent.com/26099646/122621129-19d87580-d041-11eb-8a3c-bee2f582fbe4.png)

<br/>
Read models that contain data from multiple Aggregates can be updated by Event Handlers from other microservices.  Why would this happen?  Well say you have a Bank Account record.  If we were using a relational database for a data store we'd have to either run two queries or do a join to get the Bank Account and the name of the Account Manager.  Using the CQRS model, we can "pre-render" a projection that contains both the account info and the account manager info without having to join tables together.  This example is obviously very simple, but in a complex environment where you're joining together dozens of tables to create a DTO to send to the user interface and returning 100's of thousands or millions of records, this type of archtecture can dramatically improve BOTH system performance and throughput.

![image](https://user-images.githubusercontent.com/26099646/170121000-98b0065f-a34c-40f7-8f25-8770d73e5a68.png)
<br/>
<strong>Why????</strong><br/>
When is comes to scaling there are two things to consider: speed and throughput.  "Speed" meaning the quickness of the individual action, and "throughput" meaning the number of concurrent actions that can be performed at the same time.  Using nostify addresses both of those concerns.<br/><br/>

Speed really comes into play only on the query side for most applications.  Thats a large part of the concept behind the CQRS pattern.  By seperating the command side from the query side you essentially deconstruct the datastore that would traditionally be utilizing a RDBMS in order to create materialized views of various projections of the aggregate.  Think of these views as "pre-rendered" views in a traditional relational database.  In a traditional database a view simplifies queries but still runs the joins in real time when data is requested.  By materializing the view, we denormalize the data and accept the increased complexity associated with keeping the data accurate in order to massively decrease the performance cost of querying that data.  In addition, we gain flexibility by being able to appropriately resource each container to give containers being queried the hardest more resources.<br/><br/>

Throughput is the other half of the equation. If you were using physical architechture, you'd have an app server talking to a seperate database server serving up your application.  The app server say has 4 processors with 8 cores each, so there is a limitation on the number of concurrent tasks that can be performed.  We can enhance throughput through proper coding, using parallel processing, and non-blocking code, but there is at a certain point a physical limit to the number of things that can be happening at once.  With nostify and the use of Azure Functions, this limitation is removed other than by cost.  If 1000 queries hit at the same moment in time, 1000 instances of an Azure Function spin up to handle it.
<br/>
<br/>
<strong>Setup</strong><br/>
<br/>
Use dependency injection to add a singleton instance of the Nostify class:<br/>

```
[assembly: FunctionsStartup(typeof(nostify_example.Startup))]

namespace nostify_example
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            //Adding HttpClient isn't needed for nostify, but you will almost certianly use it
            builder.Services.AddHttpClient();

            builder.Services.AddSingleton<Nostify>((s) => {
                string apiKey = "<your cosmos api key here";
                string dbName = "<name of the cosmos db here>";
                string endPoint = "<url of the cosmos endpoint here>";
                var nostify = new Nostify(apiKey,dbName,endPoint);
                return nostify;
            });
        }
    }
}

```
<br/>

If you used the `dotnet new nostify -ag <AggregateName>` template setup (which you should), in the Nostify/Aggregates folder you will find a class file already stubbed out.  The AggregateCommand base class contains default implementations for Create, Update, and Delete.  The UpdateProperties<T>() method will update any properties of the Aggregate with the value of the Event payload with the same property name.
    
```
    
     public class testCommand : AggregateCommand
    {


        public testCommand(string name)
        : base(name)
        {

        }
    }

    public class test : Aggregate
    {
        public test()
        {
        }

        new public string aggregateType => "test";

        public override void Apply(Event pe)
        {
            if (pe.command == AggregateCommand.Create || pe.command == AggregateCommand.Update)
            {
                this.UpdateProperties<test>(pe.payload);
            }
            else if (pe.command == AggregateCommand.Delete)
            {
                this.isDeleted = true;
            }
        }
    }
```
    
<strong>Example Repo Walkthrough</strong>
    
In the example repo you will find a simple BankAccount example.  We will walk through it below.  First create a directory for your example.  Navigate to the location you'd like to keep the code and from powershell or cmd:
    
    mkdir Nostify_Example
    cd .\Nostify_Example
    mkdir BankAccount
    cd .\BankAccount
    dotnet new nostify -ag BankAccount
    dotnet restore

    

This will create a project folder to hold all of your microservices, and a folder for your BankAccount service.  
    

If we will look at the BankAccount.cs file in the Aggregate folder that was created by the cli, we'll see it contains two classes which form the basis of everything we will do with this service: BankAccountCommand and BankAccount.  BankAccountCommand implements the AggregateCommand class which already defines the Create, Update, and Delete commands which will be needed in the vast majority of scenarios.  However, you need to define the commands/events beyond that.  The other is the base BankAccount which has a rudametary `Apply()` method and implements the Aggregate abstract class, which adds a few basic properties you will need to define and implements the NostifyObject abstract class which  gives you the `UpdateProperties<T>()` method.
    
First we will go in and add some basic properties to BankAccount and add a Transaction class to define a bank account transaction:
```
   public class BankAccount : Aggregate
   {
        public BankAccount()
        {
            this.transactions = new List<Transaction>();
        }

        public int accountId { get; set; }
        public Guid accountManagerId { get; set; }
        public string customerName { get; set; }
        public List<Transaction> transactions { get; set; }
        new public static string aggregateType => "BankAccount";

        public override void Apply(Event pe)
        {
            if (pe.command == AggregateCommand.Create || pe.command == AggregateCommand.Update)
            {
                this.UpdateProperties<BankAccount>(pe.payload);
            }
            else if (pe.command == AggregateCommand.Delete)
            {
                this.isDeleted = true;
            }
        }
    }

    public class Transaction
    {
        public decimal amount { get; set; }
    }


```
Now we can take a look at adding custom commands. Create, Update, and Delete are already registered inside the base class so we don't need to add them. However, a bank account might need to process a Transaction for example, so we add the definition in the BankAccountCommand class.  This registers the command with nostify to allow you to handle it in `Apply()`:<br/>

```
    public class BankAccountCommand : NostifyCommand
    {


        public static readonly BankAccountCommand ProcessTransaction = new BankAccountCommand("Process Transaction");


        public BankAccountCommand(string name)
        : base(name)
        {

        }
    }
```
Then we add a handler for it in the `Apply()` method:
```
    public override void Apply(Event pe)
    {
        if (pe.command == BankAccountCommand.Create || pe.command == BankAccountCommand.Update)
        {
            this.UpdateProperties<BankAccount>(pe.payload);
        }
        else if (pe.command == BankAccountCommand.ProcessTransaction)
        {
            Transaction transaction = ((JObject)pe.payload).ToObject<Transaction>();
            this.transactions.Add(transaction);
        }
        else if (pe.command == BankAccountCommand.Delete)
        {
            this.isDeleted = true;
        }
    }

```
If you have numerous custom commands and the if-else tree gets complex, it can be refactored into a switch statement or a `Dictionary<AggregateCommand, Action>()` for easier maintinance.
<br/>
<strong>Command Functions</strong><br/>
Commands now become easy to compose.  Using the nostify cli results in Create, Update, and Delete commands being stubbed in.  In this simplified code we don't do any checking to see if the account already exists or any other validation you would do in a real app.  All that is required is to instantiate an instance of the `Event` class and call `PersistAsync()` to write the event to the event store.
<br/>

``` 
    public class CreateAccount
    {

        private readonly HttpClient _client;
        private readonly Nostify _nostify;
        public CreateAccount(HttpClient httpClient, Nostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;
        }

        [FunctionName("CreateAccount")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] BankAccount account, HttpRequest httpRequest,
            ILogger log)
        {
            var peContainer = await _nostify.GetEventsContainerAsync();
            Guid aggId = Guid.NewGuid();
            account.id = aggId;

            Event pe = new Event(NostifyCommand.Create, account.id, account);
            await _nostify.PersistAsync(pe);

            return new OkObjectResult(new{ message = $"Account {account.id} for {account.customerName} was created"});
        }
    }
```
The standard Update command is also very simple.  You shouldn't have to modify much if at all.  It accepts a `dynamic` object so you can pass an object from the front end that contains only the properties that are being updated.  This is handy when you may have multiple users updating the same aggregate at the same time and don't want to overwrite changes by passing the entire object.  Nostify will match the property to on that exists on the Aggregate Root and update that in the `Apply()` method.  The default implementation will then update the `currentState` container.
<br/>
    
```
    public class UpdateBankAccount
    {

        private readonly HttpClient _client;
        private readonly Nostify _nostify;
        public UpdateBankAccount(HttpClient httpClient, Nostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;
        }

        [FunctionName("UpdateBankAccount")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] dynamic upd, HttpRequest httpRequest,
            ILogger log)
        {
            Guid aggRootId = Guid.Parse(upd.id.ToString());
            Event pe = new Event(NostifyCommand.Update, aggRootId, upd);
            await _nostify.PersistAsync(pe);

            return new OkObjectResult(new{ message = $"Account {upd.id} was updated"});
        }
    }
```
    
<br/>
Custom AggregateCommands are composed the same way.  In the Commands folder, add a new ProcessTransaction.cs file.  Add the code below to allow a post with a couple of query parameters to add a transaction to a BankAccount:<br/>
    
```
    public class ProcessTransaction
    {

        private readonly HttpClient _client;
        private readonly Nostify _nostify;
        public ProcessTransaction(HttpClient httpClient, Nostify nostify)
        {
            this._client = httpClient;
            this._nostify = nostify;
        }

        [FunctionName("ProcessTransaction")]
        public async Task Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            Guid accountId = Guid.Parse(req.Query["id"]);
            decimal amt = decimal.Parse(req.Query["amount"]);
            var trans = new Transaction()
            {
                amount = amt
            };

            AggregateCommand command = BankAccountCommand.AddTransaction;

            Event pe = new Event(command, accountId, trans);
            await _nostify.PersistAsync(pe);
        }
    }
```
