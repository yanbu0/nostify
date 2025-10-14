using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using nostify;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Functions.Worker;
using Azure.Core.Serialization;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

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

            var config = context.Configuration;

            //Note: This is the api key for the cosmos emulator by default
            string apiKey = config.GetValue<string>("CosmosApiKey");
            string dbName = config.GetValue<string>("CosmosDbName");
            string endPoint = config.GetValue<string>("CosmosEndPoint");
            string kafka = config.GetValue<string>("BrokerList");
            bool autoCreateContainers = config.GetValue<bool>("AutoCreateContainers");
            int defaultThroughput = config.GetValue<int>("DefaultContainerThroughput");
            bool verboseNostifyBuild = config.GetValue<bool>("VerboseNostifyBuild");
            var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

            var nostify = NostifyFactory.WithCosmos(
                                cosmosApiKey: apiKey,
                                cosmosDbName: dbName,
                                cosmosEndpointUri: endPoint,
                                createContainers: autoCreateContainers,
                                containerThroughput: defaultThroughput,
                                useGatewayConnection: false)
                            .WithKafka(kafka)
                            .WithHttp(httpClientFactory)
                            .Build<_ReplaceMe_>(verbose: true);

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

