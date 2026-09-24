using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter.Xml;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace nostify;

///<summary>
///Configuration settings for Nostify
///</summary>
public class NostifyConfig
{
    /// <summary>
    /// The API key for accessing the Cosmos DB, or <see langword="null"/> until Cosmos is configured.
    /// </summary>
    public string? cosmosApiKey { get; set; }

    /// <summary>
    /// The name of the Cosmos DB, or <see langword="null"/> until Cosmos is configured.
    /// </summary>
    public string? cosmosDbName { get; set; }

    /// <summary>
    /// The endpoint URI for the Cosmos DB, or <see langword="null"/> until Cosmos is configured.
    /// </summary>
    public string? cosmosEndpointUri { get; set; }

    /// <summary>
    /// The URL for the Kafka server, or <see langword="null"/> until Kafka is configured.
    /// </summary>
    public string? kafkaUrl { get; set; }

    /// <summary>
    /// Number of partitions to use when automatically creating Kafka topics.
    /// </summary>
    public int kafkaTopicAutoCreatePartitions { get; set; } = 2;

    /// <summary>
    /// The optional username for accessing Kafka.
    /// </summary>
    public string? kafkaUserName { get; set; }

    /// <summary>
    /// The optional password for accessing Kafka.
    /// </summary>
    public string? kafkaPassword { get; set; }

    /// <summary>
    /// The default partition key path for Cosmos DB, or <see langword="null"/> to use <c>/tenantId</c>.
    /// </summary>
    public string? defaultPartitionKeyPath { get; set; }

    /// <summary>
    /// The default tenant ID.
    /// </summary>
    public Guid defaultTenantId { get; set; }

    /// <summary>
    /// The configuration settings for the Kafka producer.
    /// </summary>
    public ProducerConfig producerConfig = new ProducerConfig();

    /// <summary>
    /// If true, create database and Aggregate/Projection containers
    /// </summary>
    public bool createContainers { get; set; }

    /// <summary>
    /// The throughput for the containers.
    /// </summary>
    public int? containerThroughput { get; set; }

    /// <summary>
    /// If true, use the gateway connection instead of direct.
    /// </summary>
    public bool useGatewayConnection { get; set; }

    /// <summary>
    /// The default retry options applied by default handlers when <c>allowRetry</c> is <c>true</c>
    /// and no explicit <see cref="RetryOptions"/> are provided. Defaults to <c>new RetryOptions()</c>
    /// (3 retries, 1 s delay, exponential backoff) when not set or when <c>null</c> is passed.
    /// </summary>
    public RetryOptions DefaultRetryOptions { get; set; } = new RetryOptions();

    /// <summary>
    /// The IHttpClientFactory instance for creating HttpClient instances to make HTTP requests.
    /// </summary>
    public IHttpClientFactory? httpClientFactory { get; set; }

    /// <summary>
    /// Optional logger instance for structured logging throughout the Nostify framework.
    /// When set, replaces Console.WriteLine calls with structured log output.
    /// </summary>
    public ILogger? logger { get; set; }

    /// <summary>
    /// When true, <see cref="NostifyFactory.Build{T}"/> will auto-create
    /// <c>{aggregateType}_EventRequest</c> Kafka topics for every <see cref="IAggregate"/>
    /// found in the assembly.  Set via <see cref="NostifyFactory.WithAsyncEventRequest"/>.
    /// Default is <c>false</c>.
    /// </summary>
    public bool autoCreateEventRequestTopics { get; set; }

}

///<summary>
///Nostify factory class
///</summary>
public static class NostifyFactory
{
    private static readonly Action<ILogger, Exception?> LogLoggerConfigured =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(LogLoggerConfigured)),
            "ILogger configured for Nostify. Structured logging enabled.");

    private static readonly Action<ILogger, string, Exception?> LogStartupDiagnostic =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(2, nameof(LogStartupDiagnostic)),
            "{StartupMessage}");

    private static readonly Action<ILogger, string, Exception?> LogStartupFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(LogStartupFailure)),
            "{StartupError}");
    /// <summary>
    /// Creates a new instance of Nostify using Cosmos.
    /// </summary>
    public static NostifyConfig WithCosmos(string cosmosApiKey, string cosmosDbName, string cosmosEndpointUri, bool? createContainers = false, int? containerThroughput = null, bool useGatewayConnection = false, RetryOptions? defaultRetryOptions = null)
    {
        NostifyConfig config = new NostifyConfig();
        return config.WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri, createContainers, containerThroughput, useGatewayConnection, defaultRetryOptions);
    }

    /// <summary>
    /// Creates a new instance of Nostify using Cosmos.
    /// </summary>
    public static NostifyConfig WithCosmos(this NostifyConfig config, string cosmosApiKey, string cosmosDbName, string cosmosEndpointUri, bool? createContainers = false, int? containerThroughput = null, bool useGatewayConnection = false, RetryOptions? defaultRetryOptions = null)
    {
        config.cosmosApiKey = cosmosApiKey;
        config.cosmosDbName = cosmosDbName;
        config.cosmosEndpointUri = cosmosEndpointUri;
        config.createContainers = createContainers ?? false;
        config.containerThroughput = containerThroughput;
        config.useGatewayConnection = useGatewayConnection;
        config.DefaultRetryOptions = defaultRetryOptions ?? new RetryOptions();
        return config;
    }

    /// <summary>
    /// Creates a new instance of Nostify using Kafka.
    /// </summary>
    public static NostifyConfig WithKafka(ProducerConfig producerConfig)
    {
        NostifyConfig config = new NostifyConfig();
        return config.WithKafka(producerConfig);
    }

    /// <summary>
    /// Creates a new instance of Nostify using Kafka.
    /// </summary>
    public static NostifyConfig WithKafka(this NostifyConfig config, ProducerConfig producerConfig)
    {
        ArgumentNullException.ThrowIfNull(producerConfig);

        config.producerConfig = producerConfig;
        config.kafkaUrl = producerConfig.BootstrapServers;
        return config;
    }

    /// <summary>
    /// Creates a new instance of Nostify using Kafka.
    /// </summary>
    public static NostifyConfig WithKafka(string kafkaUrl, string? kafkaUserName = null, string? kafkaPassword = null, int kafkaTopicAutoCreatePartitions = 2)
    {
        NostifyConfig config = new NostifyConfig();
        return config.WithKafka(kafkaUrl, kafkaUserName, kafkaPassword, kafkaTopicAutoCreatePartitions);
    }

    /// <summary>
    /// Creates a new instance of Nostify using Kafka.
    /// </summary>
    public static NostifyConfig WithKafka(this NostifyConfig config, string kafkaUrl, string? kafkaUserName = null, string? kafkaPassword = null, int kafkaTopicAutoCreatePartitions = 2)
    {
        config.kafkaTopicAutoCreatePartitions = kafkaTopicAutoCreatePartitions;

        config.kafkaUrl = kafkaUrl;
        config.kafkaUserName = kafkaUserName;
        config.kafkaPassword = kafkaPassword;
        config.producerConfig.BootstrapServers = kafkaUrl;
        config.producerConfig.ClientId = $"Nostify-{config.cosmosDbName}-{Guid.NewGuid()}";

        bool isDeployed = !string.IsNullOrWhiteSpace(config.kafkaUserName) && !string.IsNullOrWhiteSpace(config.kafkaPassword);
        if (isDeployed)
        {
            config.producerConfig.SaslUsername = config.kafkaUserName;
            config.producerConfig.SaslPassword = config.kafkaPassword;
            config.producerConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
            config.producerConfig.SaslMechanism = SaslMechanism.Plain;
            config.producerConfig.ApiVersionRequest = true;
        }

        return config;
    }

    /// <summary>
    /// Creates a new instance of Nostify using Azure Event Hubs.
    /// </summary>
    public static NostifyConfig WithEventHubs(string eventHubsConnectionString, bool diagnosticLogging = false, int kafkaTopicAutoCreatePartitions = 2)
    {
        NostifyConfig config = new NostifyConfig();
        return config.WithEventHubs(eventHubsConnectionString, diagnosticLogging, kafkaTopicAutoCreatePartitions);
    }

    /// <summary>
    /// Creates a new instance of Nostify using Azure Event Hubs.
    /// </summary>
    public static NostifyConfig WithEventHubs(this NostifyConfig config, string eventHubsConnectionString, bool diagnosticLogging = false, int kafkaTopicAutoCreatePartitions = 2)
    {
        // Parse Event Hubs connection string to extract namespace
        var connectionStringParts = eventHubsConnectionString.Split(';');
        string endpoint = connectionStringParts.FirstOrDefault(p => p.StartsWith("Endpoint=", StringComparison.Ordinal))?.Replace("Endpoint=sb://", "", StringComparison.Ordinal).Replace("/", "", StringComparison.Ordinal) ?? "";

        // Add port 9093 for Kafka protocol
        if (!endpoint.Contains(':'))
        {
            endpoint = $"{endpoint}:9093";
        }

        // Configure for Event Hubs using Kafka protocol
        config.kafkaTopicAutoCreatePartitions = kafkaTopicAutoCreatePartitions;
        config.kafkaUrl = endpoint;
        config.producerConfig.BootstrapServers = endpoint;
        config.producerConfig.ClientId = $"Nostify-{config.cosmosDbName}-{Guid.NewGuid()}";
        config.producerConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
        config.producerConfig.SaslMechanism = SaslMechanism.Plain;
        config.producerConfig.SaslUsername = "$ConnectionString";
        config.producerConfig.SaslPassword = eventHubsConnectionString;
        if (diagnosticLogging)
        {
            config.producerConfig.Debug = "security,broker,protocol"; // Enable specific debug logging
        }

        return config;
    }

    /// <summary>
    /// Creates a new instance of Nostify using an IHttpClientFactory for making HTTP requests internal to Nostify, such as Projection init methods.
    /// You should use the DI injected HttpClient in your own services.
    /// </summary>
    public static NostifyConfig WithHttp(this NostifyConfig config, IHttpClientFactory httpClientFactory)
    {
        config.httpClientFactory = httpClientFactory;
        return config;
    }

    /// <summary>
    /// Configures an <see cref="ILogger"/> for structured logging throughout the Nostify framework.
    /// When set, replaces Console.WriteLine output with structured log calls at appropriate levels
    /// (Debug for verbose/startup diagnostics, Warning for non-critical issues, Error for failures).
    /// </summary>
    /// <param name="config">The Nostify configuration settings.</param>
    /// <param name="logger">The logger instance to use for structured logging.</param>
    /// <returns>The configuration instance for fluent chaining.</returns>
    public static NostifyConfig WithLogger(this NostifyConfig config, ILogger logger)
    {
        config.logger = logger;
        LogLoggerConfigured(config.logger, null);

        return config;
    }

    /// <summary>
    /// Enables automatic creation of <c>{aggregateType}_EventRequest</c> Kafka topics
    /// for every <see cref="IAggregate"/> found in the assembly when
    /// <see cref="Build{T}"/> is called.  Required when using
    /// <c>AsyncEventRequester&lt;T&gt;</c> / <c>ExternalDataEventFactory</c>
    /// Kafka-based async event request/response.
    /// </summary>
    /// <param name="config">The Nostify configuration settings.</param>
    /// <returns>The configuration instance for fluent chaining.</returns>
    public static NostifyConfig WithAsyncEventRequest(this NostifyConfig config)
    {
        config.autoCreateEventRequestTopics = true;
        return config;
    }

    /// <summary>
    /// Builds the Nostify instance. Use generic method if wanting verbose output and/or autocreate topics.
    /// </summary>
    public static INostify Build(this NostifyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        string cosmosApiKey = RequireConfigurationValue(config.cosmosApiKey, nameof(config.cosmosApiKey), "WithCosmos");
        string cosmosDbName = RequireConfigurationValue(config.cosmosDbName, nameof(config.cosmosDbName), "WithCosmos");
        string cosmosEndpointUri = RequireConfigurationValue(config.cosmosEndpointUri, nameof(config.cosmosEndpointUri), "WithCosmos");
        string kafkaUrl = RequireConfigurationValue(config.producerConfig.BootstrapServers, nameof(config.kafkaUrl), "WithKafka or WithEventHubs");

        var Repository = new NostifyCosmosClient(cosmosApiKey,
            cosmosDbName,
            cosmosEndpointUri,
            UseGatewayConnection: config.useGatewayConnection,
            DefaultContainerThroughput: config.containerThroughput ?? -1,
            DefaultDbThroughput: config.containerThroughput ?? -1,
            logger: config.logger
        );
        var DefaultPartitionKeyPath = config.defaultPartitionKeyPath ?? "/tenantId";
        var DefaultTenantId = config.defaultTenantId;
        var KafkaProducer = new ProducerBuilder<string, string>(config.producerConfig).Build();
        var HttpClientFactory = config.httpClientFactory;

        // Build base consumer config from producer settings (without GroupId — set per consumer)
        ConsumerConfig? baseConsumerConfig = null;
        if (!string.IsNullOrEmpty(kafkaUrl))
        {
            baseConsumerConfig = new ConsumerConfig
            {
                BootstrapServers = config.producerConfig.BootstrapServers,
                AutoOffsetReset = AutoOffsetReset.Latest,
                EnableAutoCommit = true
            };

            // Mirror SASL settings from producer config if present
            if (config.producerConfig.SecurityProtocol.HasValue)
            {
                baseConsumerConfig.SecurityProtocol = config.producerConfig.SecurityProtocol;
            }
            if (config.producerConfig.SaslMechanism.HasValue)
            {
                baseConsumerConfig.SaslMechanism = config.producerConfig.SaslMechanism;
            }
            if (!string.IsNullOrEmpty(config.producerConfig.SaslUsername))
            {
                baseConsumerConfig.SaslUsername = config.producerConfig.SaslUsername;
            }
            if (!string.IsNullOrEmpty(config.producerConfig.SaslPassword))
            {
                baseConsumerConfig.SaslPassword = config.producerConfig.SaslPassword;
            }
        }

        return new Nostify(
            Repository,
            DefaultPartitionKeyPath,
            DefaultTenantId,
            kafkaUrl,
            KafkaProducer,
            HttpClientFactory,
            config.logger,
            baseConsumerConfig,
            config.DefaultRetryOptions
        );
    }

    private static string RequireConfigurationValue(string? value, string propertyName, string configurationMethod)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{propertyName} is not configured. Call {configurationMethod}() before Build().");
        }

        return value;
    }


    /// <summary>
    /// Builds the Nostify instance. Will autocreate topics in Kafka for each EventType found in the assembly of T.
    /// </summary>
    /// <param name="config">The Nostify configuration settings.</param>
    /// <param name="verbose">
    /// If true and no <see cref="ILogger"/> has been configured, writes startup diagnostics to the console while creating topics
    /// and containers. When a logger is configured it always wins, which avoids duplicating output when that logger already
    /// writes to the console.
    /// </param>
    public static INostify Build<T>(this NostifyConfig config, bool verbose = false) where T : IAggregate
    {
        try
        {
            LogDebugOrVerboseConsole(
                config,
                verbose,
                "******* Logger is null. Will try to fall back to console logging. Enable logging by using .WithLogger(yourLogger). There isn't really a reason not to enable logging, you should do it. *********",
                "ILogger is available and will be used for logging.");

            // Build a Kafka admin client first so topic discovery and container initialization share the same startup flow.
            LogDebugOrVerboseConsole(config, verbose, "Building Admin Client");
            var adminClientConfig = new AdminClientConfig(config.producerConfig);
            var adminClient = new AdminClientBuilder(adminClientConfig).Build();
            LogDebugOrVerboseConsole(config, verbose, "Admin Client built");

            var assembly = typeof(T).Assembly;
            List<TopicSpecification> topics = GetAutoCreateTopicSpecifications(assembly, config, verbose);

            // Filter topic candidates against broker metadata so repeated startup stays idempotent.
            var existingTopics = adminClient.GetMetadata(TimeSpan.FromSeconds(10)).Topics;
            topics = topics.Where(t => !existingTopics.Any(et => et.Topic.Equals(t.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            LogDebugOrVerboseConsole(
                config,
                verbose,
                $"Creating topics: {string.Join(", ", topics.Select(t => t.Name))}",
                "Creating topics: {Topics}",
                string.Join(", ", topics.Select(t => t.Name)));

            // Only issue create calls when something is missing; otherwise Kafka startup remains a metadata-only check.
            if (topics.Count > 0)
            {
                adminClient.CreateTopicsAsync(topics).Wait();
                var currentTopics = adminClient.GetMetadata(TimeSpan.FromSeconds(10)).Topics;
                LogDebugOrVerboseConsole(
                    config,
                    verbose,
                    $"Current topics: {string.Join(", ", currentTopics.Select(t => t.Topic))}",
                    "Current topics: {Topics}",
                    string.Join(", ", currentTopics.Select(t => t.Topic)));
            }

            var nostify = Build(config);

            LogDebugOrVerboseConsole(
                config,
                verbose,
                $"Creating containers for {typeof(T).Assembly.FullName}: {config.createContainers}",
                "Creating containers for {Assembly}: {CreateContainers}",
                typeof(T).Assembly.FullName ?? typeof(T).Assembly.GetName().Name ?? typeof(T).Name,
                config.createContainers);
            if (config.createContainers)
            {
                nostify.CreateContainersAsync<T>(false, config.containerThroughput, verbose).Wait();
            }

            return nostify;
        }
        catch (Exception ex)
        {
            LogErrorOrConsole(config, ex, "Error building Nostify with autocreate topics");
            throw new NostifyException("Error building Nostify with autocreate topics " + ex.Message + " " + ex.InnerException?.Message);
        }

    }

    /// <summary>
    /// Collects every topic that <see cref="Build{T}(NostifyConfig, bool)"/> should auto-create for an assembly.
    /// This includes event topics discovered from concrete <see cref="EventType"/> definitions plus optional async request/response topics for aggregates when
    /// <see cref="NostifyConfig.autoCreateEventRequestTopics"/> is enabled.
    /// </summary>
    internal static List<TopicSpecification> GetAutoCreateTopicSpecifications(Assembly assembly, NostifyConfig config, bool verbose = false)
    {
        var eventTypeDefinitions = assembly.GetTypes()
            .Where(t => typeof(EventType).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Where(t => t != typeof(LegacyNostifyCommandEventType))
            .Select(t => new
            {
                Type = t,
                Name = GetTopicName(t, config, verbose)
            })
            .ToList();

        var duplicateNames = eventTypeDefinitions
            .GroupBy(definition => definition.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                Name = group.Key,
                Types = group
                    .Select(definition => definition.Type)
                    .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal)
                    .ToArray()
            })
            .ToArray();

        if (duplicateNames.Length > 0)
        {
            var conflicts = string.Join(
                "; ",
                duplicateNames.Select(duplicate =>
                    $"'{duplicate.Name}': {string.Join(", ", duplicate.Types.Select(type => $"'{type.AssemblyQualifiedName}'"))}"));
            throw new InvalidOperationException(
                $"Multiple concrete EventType definitions declare duplicate logical names in assembly '{assembly.FullName}': {conflicts}. " +
                "EventType names must be unique using ordinal, case-sensitive comparison.");
        }

        var eventTypeNames = eventTypeDefinitions
            .Select(definition => definition.Name)
            .ToList();

        LogDebugOrVerboseConsole(
            config,
            verbose,
            $"Found {string.Join(", ", eventTypeNames)} EventType definitions in assembly {assembly.FullName}",
            "Found {EventTypes} EventType definitions in assembly {Assembly}",
            string.Join(", ", eventTypeNames),
            assembly.FullName ?? assembly.GetName().Name ?? nameof(assembly));

        List<TopicSpecification> topics = eventTypeNames
            .Select(eventTypeName => new TopicSpecification { Name = eventTypeName, NumPartitions = config.kafkaTopicAutoCreatePartitions, ReplicationFactor = 1 })
            .ToList();

        if (config.autoCreateEventRequestTopics)
        {
            var aggregateTypes = assembly.GetTypes().Where(t => typeof(IAggregate).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            foreach (var aggType in aggregateTypes)
            {
                var aggTypeProp = aggType.GetProperty("aggregateType", BindingFlags.Public | BindingFlags.Static);
                if (aggTypeProp != null)
                {
                    var aggTypeName = aggTypeProp.GetValue(null)?.ToString();
                    if (!string.IsNullOrEmpty(aggTypeName))
                    {
                        var eventRequestTopic = $"{aggTypeName}_EventRequest";
                        topics.Add(new TopicSpecification { Name = eventRequestTopic, NumPartitions = config.kafkaTopicAutoCreatePartitions, ReplicationFactor = 1 });
                        LogDebugOrVerboseConsole(
                            config,
                            verbose,
                            $"Adding EventRequest topic: {eventRequestTopic}",
                            "Adding EventRequest topic: {Topic}",
                            eventRequestTopic);

                        var eventRequestResponseTopic = $"{aggTypeName}_EventRequestResponse";
                        topics.Add(new TopicSpecification { Name = eventRequestResponseTopic, NumPartitions = config.kafkaTopicAutoCreatePartitions, ReplicationFactor = 1 });
                        LogDebugOrVerboseConsole(
                            config,
                            verbose,
                            $"Adding EventRequestResponse topic: {eventRequestResponseTopic}",
                            "Adding EventRequestResponse topic: {Topic}",
                            eventRequestResponseTopic);
                    }
                }
            }
        }

        return topics
            .GroupBy(topic => topic.Name)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>
    /// Resolves the Kafka topic name for a concrete <see cref="EventType"/> definition.
    /// </summary>
    private static string GetTopicName(Type eventTypeClass, NostifyConfig config, bool verbose)
    {
        var eventType = EventType.GetRequiredInstance(eventTypeClass);
        var topicName = eventType.name;
        if (string.IsNullOrWhiteSpace(topicName))
        {
            throw new InvalidOperationException(
                $"Event type '{eventTypeClass.FullName}' has an empty event type name.");
        }

        LogDebugOrVerboseConsole(
            config,
            verbose,
            $"Using EventType definition from {eventTypeClass.FullName} with logical topic name {topicName} for auto-topic discovery.",
            "Using EventType definition from {EventType} with logical topic name {Topic} for auto-topic discovery.",
            eventTypeClass.FullName ?? eventTypeClass.Name,
            topicName);

        return topicName;
    }

    /// <summary>
    /// Writes startup diagnostics through <see cref="ILogger"/> when available, otherwise falls back to console output only
    /// when verbose startup tracing is explicitly requested.
    /// </summary>
    private static void LogDebugOrVerboseConsole(NostifyConfig config, bool verbose, string consoleMessage, string? loggerMessage = null, params object?[] loggerArgs)
    {
        if (config.logger != null)
        {
            // consoleMessage is already rendered for the console fallback and avoids dynamic log templates.
            LogStartupDiagnostic(config.logger, consoleMessage, null);
            return;
        }

        if (verbose)
        {
            Console.WriteLine(consoleMessage);
        }
    }

    /// <summary>
    /// Emits startup failures through the configured logger or the console fallback when no logger has been registered.
    /// </summary>
    private static void LogErrorOrConsole(NostifyConfig config, Exception ex, string message)
    {
        if (config.logger != null)
        {
            LogStartupFailure(config.logger, message, ex);
            return;
        }

        Console.WriteLine(message + ": " + ex.Message + " " + ex.InnerException?.Message);
    }
}