using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using nostify;
using Microsoft.Extensions.Configuration;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>{
        services.AddHttpClient();

        var config = context.Configuration;

        //Note: This is the api key for the cosmos emulator by default
        string apiKey = config.GetValue<string>("apiKey");
        string dbName = config.GetValue<string>("dbName");
        string endPoint = config.GetValue<string>("endPoint");
        string kafka = config.GetValue<string>("BrokerList");

        var nostify = new Nostify(apiKey,dbName,kafka,endPoint);

        if (context.HostingEnvironment.IsDevelopment())
        {
            //Creates persistedEvents container so Event Handlers can attach without throwing errors during testing, may be removed once persistedEvents container exists
            var _ = nostify.GetPersistedEventsContainerAsync().GetAwaiter().GetResult();
        }

        services.AddSingleton<INostify>(nostify);
        services.AddLogging();
    })
    .Build();

host.Run();