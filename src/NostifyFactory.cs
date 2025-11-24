using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace nostify;

///<summary>
///Configuration settings for Nostify
///</summary>
public class NostifyConfig
{
    /// <summary>
    /// The API key for accessing the Cosmos DB.
    /// </summary>
    public string cosmosApiKey { get; set; }

    /// <summary>
    /// The name of the Cosmos DB.
    /// </summary>
    public string cosmosDbName { get; set; }

    /// <summary>
    /// The endpoint URI for the Cosmos DB.
    /// </summary>
    public string cosmosEndpointUri { get; set; }

    /// <summary>
    /// The URL for the Kafka server.
    /// </summary>
    public string kafkaUrl { get; set; }

    /// <summary>
    /// Number of partitions to use when automatically creating Kafka topics.
    /// </summary>
    public int kafkaTopicAutoCreatePartitions { get; set; } = 2;

    /// <summary>
    /// The username for accessing Kafka.
    /// </summary>
    public string kafkaUserName { get; set; }

    /// <summary>
    /// The password for accessing Kafka.
    /// </summary>
    public string kafkaPassword { get; set; }

    /// <summary>
    /// The default partition key path for Cosmos DB.
    /// </summary>
    public string defaultPartitionKeyPath { get; set; }

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
    /// The IHttpClientFactory instance for creating HttpClient instances to make HTTP requests.
    /// </summary>
    public IHttpClientFactory? httpClientFactory { get; set; } = null;

}

///<summary>
///Nostify factory class
///</summary>
public static class NostifyFactory
{
    /// <summary>
    /// Creates a new instance of Nostify using Cosmos.
    /// </summary>
    public static NostifyConfig WithCosmos(string cosmosApiKey, string cosmosDbName, string cosmosEndpointUri, bool? createContainers = false, int? containerThroughput = null, bool useGatewayConnection = false)
    {
        NostifyConfig config = new NostifyConfig();
        return config.WithCosmos(cosmosApiKey, cosmosDbName, cosmosEndpointUri, createContainers, containerThroughput, useGatewayConnection);
    }

    /// <summary>
    /// Creates a new instance of Nostify using Cosmos.
    /// </summary>
    public static NostifyConfig WithCosmos(this NostifyConfig config, string cosmosApiKey, string cosmosDbName, string cosmosEndpointUri, bool? createContainers = false, int? containerThroughput = null, bool useGatewayConnection = false)
    {
        config.cosmosApiKey = cosmosApiKey;
        config.cosmosDbName = cosmosDbName;
        config.cosmosEndpointUri = cosmosEndpointUri;
        config.createContainers = createContainers ?? false;
        config.containerThroughput = containerThroughput;
        config.useGatewayConnection = useGatewayConnection;
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
        config.producerConfig = producerConfig;
        return config;
    }

    /// <summary>
    /// Creates a new instance of Nostify using Kafka.
    /// </summary>
    public static NostifyConfig WithKafka(string kafkaUrl, string kafkaUserName = null, string kafkaPassword = null, int kafkaTopicAutoCreatePartitions = 2)
    {
        NostifyConfig config = new NostifyConfig();
        return config.WithKafka(kafkaUrl, kafkaUserName, kafkaPassword, kafkaTopicAutoCreatePartitions);
    }

    /// <summary>
    /// Creates a new instance of Nostify using Kafka.
    /// </summary>
    public static NostifyConfig WithKafka(this NostifyConfig config, string kafkaUrl, string kafkaUserName = null, string kafkaPassword = null, int kafkaTopicAutoCreatePartitions = 2)
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
    public static NostifyConfig WithEventHubs(string eventHubsConnectionString, bool diagnosticLogging = false, int kafkaTopicAutoCreatePartitions = 2  )
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
        string endpoint = connectionStringParts.FirstOrDefault(p => p.StartsWith("Endpoint="))?.Replace("Endpoint=sb://", "").Replace("/", "") ?? "";

        // Add port 9093 for Kafka protocol
        if (!endpoint.Contains(":"))
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
    /// Builds the Nostify instance. Use generic method if wanting verbose output and/or autocreate topics.
    /// </summary>
    public static INostify Build(this NostifyConfig config)
    {
        var Repository = new NostifyCosmosClient(config.cosmosApiKey,
            config.cosmosDbName,
            config.cosmosEndpointUri,
            UseGatewayConnection: config.useGatewayConnection,
            DefaultContainerThroughput: config.containerThroughput ?? -1,
            DefaultDbThroughput: config.containerThroughput ?? -1
        );
        var DefaultPartitionKeyPath = config.defaultPartitionKeyPath;
        var DefaultTenantId = config.defaultTenantId;
        var KafkaUrl = config.kafkaUrl;
        var KafkaProducer = new ProducerBuilder<string, string>(config.producerConfig).Build();
        var HttpClientFactory = config.httpClientFactory;

        return new Nostify(
            Repository,
            DefaultPartitionKeyPath,
            DefaultTenantId,
            KafkaUrl,
            KafkaProducer,
            HttpClientFactory
        );
    }


    /// <summary>
    /// Builds the Nostify instance. Will autocreate topics in Kafka for each NostifyCommand found in the assembly of T.
    /// </summary>
    /// <param name="config">The Nostify configuration settings.</param>
    /// <param name="verbose">If true, will write to console the steps taken to create the containers and topics</param>
    public static INostify Build<T>(this NostifyConfig config, bool verbose = false) where T : IAggregate
    {

        //Create Confluent admin client
        if (verbose) Console.WriteLine("Building Admin Client");
        var adminClientConfig = new AdminClientConfig(config.producerConfig);
        var adminClient = new AdminClientBuilder(adminClientConfig).Build();
        if (verbose) Console.WriteLine("Admin Client built");

        //Find all NostifyCommand instances in this assembly of T and create a topic for each
        var assembly = typeof(T).Assembly;
        var commandTypes = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(NostifyCommand)));
        if (verbose) Console.WriteLine($"Found {string.Join(", ", commandTypes.Select(c => c.Name))} command definitions in assembly {assembly.FullName}");
        
        //Get any static properties of each commandType that inherit type NostifyCommand        
        var commandProperties = commandTypes
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.FieldType.IsSubclassOf(typeof(NostifyCommand))));

        if (verbose) Console.WriteLine($"Found {string.Join(", ", commandProperties.Select(c => c.Name))} commands");

        List<TopicSpecification> topics = new List<TopicSpecification>();
        foreach (var commandType in commandProperties)
        {
            //Get the name property value of the commandType
            var topic = commandType.GetValue(null).GetType().GetProperty("name").GetValue(commandType.GetValue(null)).ToString();
            var topicSpec = new TopicSpecification { Name = topic, NumPartitions = config.kafkaTopicAutoCreatePartitions, ReplicationFactor = 1 };
            topics.Add(topicSpec);
        }
        //Filter topics to only create new topics
        var existingTopics = adminClient.GetMetadata(TimeSpan.FromSeconds(10)).Topics;
        topics = topics.Where(t => !existingTopics.Any(et => et.Topic.ToLower() == t.Name.ToLower())).ToList();
        if (verbose) Console.WriteLine($"Creating topics: {string.Join(", ", topics.Select(t => t.Name))}");

        //Create any new topics needed
        if (topics.Count > 0)
        {
            adminClient.CreateTopicsAsync(topics).Wait();
            var currentTopics = adminClient.GetMetadata(TimeSpan.FromSeconds(10)).Topics;
            if (verbose) Console.WriteLine($"Current topics: {string.Join(", ", currentTopics.Select(t => t.Topic))}");
        }

        var nostify = Build(config);

        if (verbose) Console.WriteLine($"Creating containers for {typeof(T).Assembly.FullName}: {config.createContainers}");
        if (config.createContainers)
        {
            nostify.CreateContainersAsync<T>(false, config.containerThroughput, verbose).Wait();
        }

        return nostify;
    }
}