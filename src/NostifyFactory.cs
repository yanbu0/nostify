using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.EventHubs;
using Azure.ResourceManager.EventHubs.Models;
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

    /// <summary>
    /// The type of messaging system being used (Kafka or EventHubs)
    /// </summary>
    internal MessagingType messagingType { get; set; } = MessagingType.Kafka;

    /// <summary>
    /// The Event Hubs connection string (for Event Hubs messaging type)
    /// </summary>
    public string eventHubsConnectionString { get; set; }

    /// <summary>
    /// The Event Hubs namespace name (for Event Hubs topic creation)
    /// </summary>
    public string eventHubsNamespace { get; set; }

    /// <summary>
    /// The Azure subscription ID (for Event Hubs topic creation)
    /// </summary>
    public string azureSubscriptionId { get; set; }

    /// <summary>
    /// The Azure resource group name (for Event Hubs topic creation)
    /// </summary>
    public string azureResourceGroup { get; set; }

    /// <summary>
    /// The Azure tenant ID (for Event Hubs topic creation)
    /// </summary>
    public string azureTenantId { get; set; }

    /// <summary>
    /// The Azure client ID (for Event Hubs topic creation)
    /// </summary>
    public string azureClientId { get; set; }

    /// <summary>
    /// The Azure client secret (for Event Hubs topic creation)
    /// </summary>
    public string azureClientSecret { get; set; }
}

/// <summary>
/// Messaging system type
/// </summary>
internal enum MessagingType
{
    /// <summary>
    /// Apache Kafka
    /// </summary>
    Kafka,
    
    /// <summary>
    /// Azure Event Hubs
    /// </summary>
    EventHubs
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
        config.messagingType = MessagingType.Kafka;
        return config;
    }

    /// <summary>
    /// Creates a new instance of Nostify using Kafka.
    /// </summary>
    public static NostifyConfig WithKafka(string kafkaUrl, string kafkaUserName = null, string kafkaPassword = null)
    {
        NostifyConfig config = new NostifyConfig();
        return config.WithKafka(kafkaUrl, kafkaUserName, kafkaPassword);
    }

    /// <summary>
    /// Creates a new instance of Nostify using Kafka.
    /// </summary>
    public static NostifyConfig WithKafka(this NostifyConfig config, string kafkaUrl, string kafkaUserName = null, string kafkaPassword = null)
    {
        config.kafkaUrl = kafkaUrl;
        config.kafkaUserName = kafkaUserName;
        config.kafkaPassword = kafkaPassword;
        config.producerConfig.BootstrapServers = kafkaUrl;
        config.producerConfig.ClientId = $"Nostify-{config.cosmosDbName}-{Guid.NewGuid()}";
        config.producerConfig.AllowAutoCreateTopics = true;
        config.messagingType = MessagingType.Kafka;

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
    public static NostifyConfig WithEventHubs(string eventHubsConnectionString)
    {
        NostifyConfig config = new NostifyConfig();
        return config.WithEventHubs(eventHubsConnectionString);
    }

    /// <summary>
    /// Creates a new instance of Nostify using Azure Event Hubs.
    /// </summary>
    public static NostifyConfig WithEventHubs(this NostifyConfig config, string eventHubsConnectionString)
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
        config.kafkaUrl = endpoint;
        config.producerConfig.BootstrapServers = endpoint;
        config.producerConfig.ClientId = $"Nostify-{config.cosmosDbName}-{Guid.NewGuid()}";
        config.producerConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
        config.producerConfig.SaslMechanism = SaslMechanism.Plain;
        config.producerConfig.SaslUsername = "$ConnectionString";
        config.producerConfig.SaslPassword = eventHubsConnectionString;
        config.messagingType = MessagingType.EventHubs;
        config.eventHubsConnectionString = eventHubsConnectionString;

        // Extract namespace from endpoint (remove port if present)
        config.eventHubsNamespace = endpoint.Contains(":") ? endpoint.Substring(0, endpoint.IndexOf(":")) : endpoint;
        if (config.eventHubsNamespace.EndsWith(".servicebus.windows.net"))
        {
            config.eventHubsNamespace = config.eventHubsNamespace.Substring(0, config.eventHubsNamespace.IndexOf("."));
        }

        return config;
    }

    /// <summary>
    /// Adds Azure credentials for Event Hubs topic creation. Only needed if using Build&lt;T&gt; to auto-create topics.
    /// </summary>
    /// <param name="config">The Nostify configuration</param>
    /// <param name="subscriptionId">Azure subscription ID</param>
    /// <param name="resourceGroup">Azure resource group name</param>
    /// <param name="tenantId">Azure tenant ID</param>
    /// <param name="clientId">Azure client ID (Service Principal)</param>
    /// <param name="clientSecret">Azure client secret</param>
    /// <returns>The configuration for method chaining</returns>
    public static NostifyConfig WithEventHubsManagement(this NostifyConfig config, string subscriptionId, string resourceGroup, string tenantId, string clientId, string clientSecret)
    {
        config.azureSubscriptionId = subscriptionId;
        config.azureResourceGroup = resourceGroup;
        config.azureTenantId = tenantId;
        config.azureClientId = clientId;
        config.azureClientSecret = clientSecret;
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
    /// Builds the Nostify instance. Will autocreate topics in Kafka or Event Hubs for each NostifyCommand found in the assembly of T.
    /// </summary>
    /// <param name="config">The Nostify configuration settings.</param>
    /// <param name="verbose">If true, will write to console the steps taken to create the containers and topics</param>
    public static INostify Build<T>(this NostifyConfig config, bool verbose = false) where T : IAggregate
    {
        //Find all NostifyCommand instances in this assembly of T and create a topic for each
        var assembly = typeof(T).Assembly;
        var commandTypes = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(NostifyCommand)));
        if (verbose) Console.WriteLine($"Found {string.Join(", ", commandTypes.Select(c => c.Name))} command definitions in assembly {assembly.FullName}");
        
        //Get any static properties of each commandType that inherit type NostifyCommand        
        var commandProperties = commandTypes
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.FieldType.IsSubclassOf(typeof(NostifyCommand))));

        if (verbose) Console.WriteLine($"Found {string.Join(", ", commandProperties.Select(c => c.Name))} commands");

        List<string> topicNames = new List<string>();
        foreach (var commandType in commandProperties)
        {
            //Get the name property value of the commandType
            var topic = commandType.GetValue(null).GetType().GetProperty("name").GetValue(commandType.GetValue(null)).ToString();
            topicNames.Add(topic);
        }

        if (config.messagingType == MessagingType.Kafka)
        {
            CreateKafkaTopics(config, topicNames, verbose);
        }
        else if (config.messagingType == MessagingType.EventHubs)
        {
            CreateEventHubs(config, topicNames, verbose);
        }

        var nostify = Build(config);

        if (verbose) Console.WriteLine($"Creating containers for {typeof(T).Assembly.FullName}: {config.createContainers}");
        if (config.createContainers)
        {
            nostify.CreateContainersAsync<T>(false, config.containerThroughput, verbose).Wait();
        }

        return nostify;
    }

    private static void CreateKafkaTopics(NostifyConfig config, List<string> topicNames, bool verbose)
    {
        //Create Confluent admin client
        if (verbose) Console.WriteLine("Building Kafka Admin Client");
        var adminClientConfig = new AdminClientConfig(config.producerConfig);
        var adminClient = new AdminClientBuilder(adminClientConfig).Build();
        if (verbose) Console.WriteLine("Kafka Admin Client built");

        List<TopicSpecification> topics = new List<TopicSpecification>();
        foreach (var topic in topicNames)
        {
            var topicSpec = new TopicSpecification { Name = topic, NumPartitions = 6 };
            topics.Add(topicSpec);
        }
        
        //Filter topics to only create new topics
        var existingTopics = adminClient.GetMetadata(TimeSpan.FromSeconds(10)).Topics;
        topics = topics.Where(t => !existingTopics.Any(et => et.Topic == t.Name)).ToList();
        if (verbose) Console.WriteLine($"Creating Kafka topics: {string.Join(", ", topics.Select(t => t.Name))}");

        //Create any new topics needed
        if (topics.Count > 0)
        {
            adminClient.CreateTopicsAsync(topics).Wait();
            var currentTopics = adminClient.GetMetadata(TimeSpan.FromSeconds(10)).Topics;
            if (verbose) Console.WriteLine($"Current Kafka topics: {string.Join(", ", currentTopics.Select(t => t.Topic))}");
        }
        else
        {
            if (verbose) Console.WriteLine("All Kafka topics already exist");
        }
    }

    private static void CreateEventHubs(NostifyConfig config, List<string> topicNames, bool verbose)
    {
        // Check if Azure credentials are provided
        if (string.IsNullOrWhiteSpace(config.azureSubscriptionId) ||
            string.IsNullOrWhiteSpace(config.azureResourceGroup) ||
            string.IsNullOrWhiteSpace(config.azureTenantId) ||
            string.IsNullOrWhiteSpace(config.azureClientId) ||
            string.IsNullOrWhiteSpace(config.azureClientSecret) ||
            string.IsNullOrWhiteSpace(config.eventHubsNamespace))
        {
            if (verbose)
            {
                Console.WriteLine("Event Hubs topic creation skipped: Azure credentials not provided.");
                Console.WriteLine("To auto-create Event Hubs, call .WithEventHubsManagement() with Azure credentials.");
                Console.WriteLine($"Topics needed: {string.Join(", ", topicNames)}");
            }
            return;
        }

        if (verbose) Console.WriteLine("Building Azure Event Hubs Admin Client");

        try
        {
            // Create Azure credential
            var credential = new ClientSecretCredential(
                config.azureTenantId,
                config.azureClientId,
                config.azureClientSecret);

            // Create ARM client
            var armClient = new ArmClient(credential);
            var subscription = armClient.GetSubscriptionResource(new Azure.Core.ResourceIdentifier($"/subscriptions/{config.azureSubscriptionId}"));
            
            // Get the Event Hubs namespace
            var resourceGroupResource = subscription.GetResourceGroup(config.azureResourceGroup).Value;
            var eventHubsNamespaceCollection = resourceGroupResource.GetEventHubsNamespaces();
            var namespaceResource = eventHubsNamespaceCollection.Get(config.eventHubsNamespace).Value;

            if (verbose) Console.WriteLine($"Connected to Event Hubs namespace: {config.eventHubsNamespace}");

            // Get existing Event Hubs
            var existingEventHubs = namespaceResource.GetEventHubs().Select(eh => eh.Data.Name).ToList();
            var eventHubsToCreate = topicNames.Where(t => !existingEventHubs.Contains(t)).ToList();

            if (verbose) Console.WriteLine($"Creating Event Hubs: {string.Join(", ", eventHubsToCreate)}");

            // Create Event Hubs
            foreach (var eventHubName in eventHubsToCreate)
            {
                var eventHubData = new EventHubData()
                {
                    PartitionCount = 6, // Match Kafka default partition count
                    MessageRetentionInDays = 7 // Default retention
                };

                var createOperation = namespaceResource.GetEventHubs().CreateOrUpdate(
                    Azure.WaitUntil.Completed,
                    eventHubName,
                    eventHubData);

                if (verbose) Console.WriteLine($"Created Event Hub: {eventHubName}");
            }

            if (eventHubsToCreate.Count == 0)
            {
                if (verbose) Console.WriteLine("All Event Hubs already exist");
            }
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                Console.WriteLine($"Error creating Event Hubs: {ex.Message}");
                Console.WriteLine("Event Hubs must be created manually or with valid Azure credentials.");
            }
            throw new InvalidOperationException($"Failed to create Event Hubs. Ensure Azure credentials are correct and the service principal has appropriate permissions. Error: {ex.Message}", ex);
        }
    }
}
