using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using nostify;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core.Serialization;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

namespace _ReplaceMe__Service;

public class Program
{
    private static void Main(string[] args)
    {
        var host = new HostBuilder()
        .ConfigureFunctionsWorkerDefaults()
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
    /// Calling ConfigureFunctionsWorkerDefaults() configures the Functions Worker to use System.Text.Json for all JSON
    /// serialization and sets JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    /// This method uses DI to modify the JsonSerializerOptions. Call /api/HttpFunction to see the changes.
    /// </summary>
    public static IFunctionsWorkerApplicationBuilder ConfigureSystemTextJson(this IFunctionsWorkerApplicationBuilder builder)
    {
        builder.Services.Configure<JsonSerializerOptions>(jsonSerializerOptions =>
        {
            jsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            jsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            jsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve;

            // override the default value
            jsonSerializerOptions.PropertyNameCaseInsensitive = false;
        });

        return builder;
    }

    /// <summary>
    /// The functions worker uses the Azure SDK's ObjectSerializer to abstract away all JSON serialization. This allows you to
    /// swap out the default System.Text.Json implementation for the Newtonsoft.Json implementation.
    /// To do so, add the Microsoft.Azure.Core.NewtonsoftJson nuget package and then update the WorkerOptions.Serializer property.
    /// This method updates the Serializer to use Newtonsoft.Json. Call /api/HttpFunction to see the changes.
    /// </summary>
    public static IFunctionsWorkerApplicationBuilder UseNewtonsoftJson(this IFunctionsWorkerApplicationBuilder builder)
    {
        builder.Services.Configure<WorkerOptions>(workerOptions =>
        {
            var settings = NewtonsoftJsonObjectSerializer.CreateJsonSerializerSettings();
            settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            settings.NullValueHandling = NullValueHandling.Ignore;

            workerOptions.Serializer = new NewtonsoftJsonObjectSerializer(settings);
        });

        return builder;
    }
}

