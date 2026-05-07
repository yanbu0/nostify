using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using nostify;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Functions.Worker;
using Azure.Core.Serialization;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System;

namespace _ReplaceMe__Service;

public class Program
{
    private static void Main(string[] args)
    {
        var host = new HostBuilder()
        .ConfigureFunctionsWorkerDefaults(builder =>
        {
            builder.UseNewtonsoftJson();
        })
        .ConfigureServices((context, services) =>
        {
            services.AddHttpClient();
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.AddConsole(); // Explicitly add the console logger, update this if needed
                loggingBuilder.SetMinimumLevel(LogLevel.Debug); // Set the minimum log level to Debug if you want to see detailed logs from Nostify
            });

            var config = context.Configuration;

            //Note: This is the api key for the cosmos emulator by default
            string apiKey = config.GetValue<string>("CosmosApiKey");
            string dbName = config.GetValue<string>("CosmosDbName");
            string endPoint = config.GetValue<string>("CosmosEndPoint");
#if (eventHubs)
            string eventHubConnectionString = config.GetValue<string>("EventHubConnectionString");
#else
            string kafka = config.GetValue<string>("BrokerList");
#endif
            bool autoCreateContainers = config.GetValue<bool>("AutoCreateContainers");
            int defaultThroughput = config.GetValue<int>("DefaultContainerThroughput");
            bool verboseNostifyBuild = config.GetValue<bool>("VerboseNostifyBuild");

            // Build default retry options from settings (applied by all default handlers when allowRetry=true)
            var defaultRetryOptions = new RetryOptions(
                maxRetries: config.GetValue<int>("DefaultRetryMaxRetries", 3),
                delay: TimeSpan.FromMilliseconds(config.GetValue<double>("DefaultRetryDelayMs", 1000)),
                retryWhenNotFound: config.GetValue<bool>("DefaultRetryWhenNotFound", false)
            );

            var sp = services.BuildServiceProvider();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("nostify");

            var nostify = NostifyFactory.WithCosmos(
                                    cosmosApiKey: apiKey,
                                    cosmosDbName: dbName,
                                    cosmosEndpointUri: endPoint,
                                    createContainers: autoCreateContainers,
                                    containerThroughput: defaultThroughput,
                                    useGatewayConnection: false,
                                    defaultRetryOptions: defaultRetryOptions)
#if (eventHubs)
                                .WithEventHubs(eventHubConnectionString)
#else
                                .WithKafka(kafka)
#endif
                                .WithHttp(httpClientFactory)
                                .WithLogger(logger)
                                .Build<_ReplaceMe_>(verbose: verboseNostifyBuild);

            services.AddSingleton<INostify>(nostify);
            services.AddLogging();
        })
        .Build();

        host.Run();
    }

    
}

internal static class WorkerConfigurationExtensions
{

    /// <summary>
    /// The functions worker uses the Azure SDK's ObjectSerializer to abstract away all JSON serialization. This allows you to
    /// swap out the default System.Text.Json implementation for the Newtonsoft.Json implementation.
    /// To do so, add the Microsoft.Azure.Core.NewtonsoftJson nuget package and then update the WorkerOptions.Serializer property.
    /// This method updates the Serializer to use Newtonsoft.Json. Call /api/HttpFunction to see the changes.
    /// Not using Newtonsoft.Json will cause weird serialization issues with the HttpRequestData and HttpResponseData objects.
    /// This method should be called in the ConfigureFunctionsWorkerDefaults() method in the Program.cs
    /// </summary>
    public static IFunctionsWorkerApplicationBuilder UseNewtonsoftJson(this IFunctionsWorkerApplicationBuilder builder)
    {
        builder.Services.Configure<WorkerOptions>(workerOptions =>
        {
            workerOptions.Serializer = new NewtonsoftJsonObjectSerializer(SerializationSettings.NostifyDefault);
        });

        return builder;
    }
}
