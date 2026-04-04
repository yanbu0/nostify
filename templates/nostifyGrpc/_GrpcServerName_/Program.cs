using _GrpcServerName_.Security;
using _GrpcServerName_.Services;
using nostify;

var builder = WebApplication.CreateBuilder(args);

// Configure gRPC with optional API key interceptor based on Security:Mode
var securityMode = builder.Configuration["Security:Mode"] ?? "None";

builder.Services.AddGrpc(options =>
{
    if (securityMode.Equals("apikey", StringComparison.OrdinalIgnoreCase))
    {
        options.Interceptors.Add<ApiKeyInterceptor>();
    }
});

// Build INostify instances for each configured service.
// Add more services by adding entries under "Services" in appsettings.json.
// Program.cs reads them dynamically — no code changes needed.
var servicesSection = builder.Configuration.GetSection("Services");
var serviceMap = new Dictionary<string, INostify>(StringComparer.OrdinalIgnoreCase);

var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

foreach (var serviceSection in servicesSection.GetChildren())
{
    var serviceName = serviceSection.Key;
    var cosmosApiKey = serviceSection["CosmosApiKey"];
    var cosmosDbName = serviceSection["CosmosDbName"];
    var cosmosEndPoint = serviceSection["CosmosEndPoint"];

    if (string.IsNullOrEmpty(cosmosApiKey) ||
        string.IsNullOrEmpty(cosmosDbName) ||
        string.IsNullOrEmpty(cosmosEndPoint))
    {
        // Skip services without complete Cosmos configuration
        continue;
    }

    var logger = loggerFactory.CreateLogger($"nostify.{serviceName}");

    var nostify = NostifyFactory.WithCosmos(
            cosmosApiKey: cosmosApiKey,
            cosmosDbName: cosmosDbName,
            cosmosEndpointUri: cosmosEndPoint,
            createContainers: false)
        .WithLogger(logger)
        .Build();

    serviceMap[serviceName] = nostify;
}

builder.Services.AddSingleton<IDictionary<string, INostify>>(serviceMap);

var app = builder.Build();

// Log registered services on startup
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("_GrpcServerName_");
startupLogger.LogInformation("gRPC server starting with {ServiceCount} service(s) registered: {Services}",
    serviceMap.Count,
    string.Join(", ", serviceMap.Keys));
startupLogger.LogInformation("Security mode: {SecurityMode}", securityMode);

app.MapGrpcService<UnifiedGrpcEventRequestService>();

app.Run();

// Make the implicit Program class public for WebApplicationFactory in tests
public partial class Program { }
