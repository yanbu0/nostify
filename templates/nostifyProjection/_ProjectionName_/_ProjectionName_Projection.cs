

using System.Net.Http.Json;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using nostify;

namespace _ReplaceMe__Service;
 
public class _ProjectionName_ : NostifyObject, IProjection, IHasExternalData<_ProjectionName_>
{
    private readonly ILogger<_ProjectionName_> _logger;
 
    public _ProjectionName_(ILogger<_ProjectionName_> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static string containerName => "_ProjectionName_";

    public bool initialized { get; set; } = false;

    public bool isDeleted { get; set; }

    //These are examples to show how to query data events and can be removed after creation
    //**********************************************************************************************
    
    // Single ID examples - for one-to-one relationships
    public Guid sameServiceAggregateExampleId { get; set; }
    public string sameServiceAggregateExampleName { get; set; }
    public Guid sameServiceAggregateExampleId2 { get; set; }
    public string sameServiceAggregateExampleName2 { get; set; }
    
    // List ID examples - for one-to-many relationships
    public List<Guid> sameServiceListExampleIds { get; set; } = new List<Guid>();
    public List<Guid> anotherListExampleIds { get; set; } = new List<Guid>();
    
    // Dependent ID examples - IDs populated by events (initially null/empty)
    public Guid dependentIdExample { get; set; } = Guid.Empty;
    public List<Guid> dependentListIdsExample { get; set; } = new List<Guid>();
    
    // External service examples
    public Guid externalAggregateExample1Id { get; set; }
    public string externalAggregateExample1Name { get; set; }
    public Guid externalAggregateExample2Id { get; set; }
    public string externalAggregateExample2Name { get; set; }
    public Guid externalAggregateExample3Id { get; set; }
    public string externalAggregateExample3Name { get; set; }
    
    // Dependent external ID examples - populated by external service events
    public Guid? dependentExternalIdExample { get; set; }


    //**********************************************************************************************

    // Apply handlers use the new typed EventType Apply overloads, matching the aggregate pattern.
    // These methods are invoked by the Nostify infrastructure via dynamic dispatch based on the
    // concrete EventType of the incoming event.

    /// <summary>
    /// Handles Create events for the projection.
    /// Populates the projection properties from the event payload.
    /// </summary>
    protected void Apply(Create__ReplaceMe_ eventType, IEvent eventToApply)
    {
        try
        {
            this.UpdateProperties<_ProjectionName_>(eventToApply.payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying Create event to projection {ProjectionName}. EventType: {EventType}, Event: {@Event}",
                containerName,
                eventType,
                eventToApply);
            throw;
        }
    }

    /// <summary>
    /// Handles Update events for the projection.
    /// Populates the projection properties from the event payload.
    /// </summary>
    protected void Apply(Update__ReplaceMe_ eventType, IEvent eventToApply)
    {
        try
        {
            this.UpdateProperties<_ProjectionName_>(eventToApply.payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying Update event to projection {ProjectionName}. EventType: {EventType}, Event: {@Event}",
                containerName,
                eventType,
                eventToApply);
            throw;
        }
    }

    /// <summary>
    /// Handles bulk create events for the projection.
    /// Populates the projection properties from the event payload.
    /// </summary>
    protected void Apply(BulkCreate__ReplaceMe_ eventType, IEvent eventToApply)
    {
        try
        {
            this.UpdateProperties<_ProjectionName_>(eventToApply.payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying BulkCreate event to projection {ProjectionName}. EventType: {EventType}, Event: {@Event}",
                containerName,
                eventType,
                eventToApply);
            throw;
        }
    }

    /// <summary>
    /// Handles bulk update events for the projection.
    /// Populates the projection properties from the event payload.
    /// </summary>
    protected void Apply(BulkUpdate__ReplaceMe_ eventType, IEvent eventToApply)
    {
        try
        {
            this.UpdateProperties<_ProjectionName_>(eventToApply.payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying BulkUpdate event to projection {ProjectionName}. EventType: {EventType}, Event: {@Event}",
                containerName,
                eventType,
                eventToApply);
            throw;
        }
    }

    /// <summary>
    /// Handles delete events for the projection.
    /// Marks the projection as deleted and sets a TTL for soft deletion.
    /// </summary>
    protected void Apply(Delete__ReplaceMe_ eventType, IEvent eventToApply)
    {
        this.isDeleted = true;
        this.ttl = 1;
    }

    /// <summary>
    /// Handles bulk delete events for the projection.
    /// Marks the projection as deleted and sets a TTL for soft deletion.
    /// </summary>
    protected void Apply(BulkDelete__ReplaceMe_ eventType, IEvent eventToApply)
    {
        this.isDeleted = true;
        this.ttl = 1;
    }

    public async static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<_ProjectionName_> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
    {
        // RECOMMENDED: Use ExternalDataEventFactory for cleaner, more maintainable code
        var factory = new ExternalDataEventFactory<_ProjectionName_>(
            nostify,
            projectionsToInit,
            httpClient,
            pointInTime);

        // === Primary Selectors (run first) ===
        
        // Get events from same service for related aggregates (single IDs)
        factory.WithSameServiceIdSelectors(
            p => p.sameServiceAggregateExampleId,
            p => p.sameServiceAggregateExampleId2);

        // Get events for one-to-many relationships (list IDs)
        factory.WithSameServiceListIdSelectors(
            p => p.sameServiceListExampleIds,
            p => p.anotherListExampleIds);

        // Get events from external services (if httpClient provided)
        if (httpClient != null)
        {
            // Add external service requestors for single and multiple IDs
            factory.WithEventRequestor(
                "https://externalDataExampleUrl/api/EventRequest",
                p => p.externalAggregateExample1Id);

            // For multiple IDs from the same external service
            factory.WithEventRequestor(
                "https://service1/api/EventRequest",
                p => p.externalAggregateExample2Id,
                p => p.externalAggregateExample3Id);
        }

        // === Dependent Selectors (run after initial events are applied) ===
        
        // Get events for IDs that are populated by the primary events
        // These selectors execute AFTER the initial events have been applied to projection copies
        factory.WithSameServiceDependantIdSelectors(
            p => p.dependentIdExample);  // This ID is null/empty initially, populated by events

        // Get events for list IDs that are populated by the primary events
        factory.WithSameServiceDependantListIdSelectors(
            p => p.dependentListIdsExample);  // This list is empty initially, populated by events

        // Get events from external services for IDs populated by primary events
        if (httpClient != null)
        {
            factory.WithDependantEventRequestor(
                "https://dependentServiceUrl/api/EventRequest",
                p => p.dependentExternalIdExample);  // Populated by initial local or external events
        }

        return await factory.GetEventsAsync();

        /* ALTERNATIVE: Legacy approach using ExternalDataEvent directly
        Container sameServiceEventStore = await nostify.GetEventStoreContainerAsync();
        List<ExternalDataEvent> externalDataEvents = await ExternalDataEvent.GetEventsAsync(sameServiceEventStore, 
            projectionsToInit, 
            p => p.sameServiceAggregateExampleId,
            p => p.sameServiceAggregateExampleId2);

        if (httpClient != null)
        {
            var externalEventsFromExternalService = await ExternalDataEvent.GetEventsAsync(httpClient,
                "https://externalDataExampleUrl/api/EventRequest",
                projectionsToInit,
                p => p.externalAggregateExample1Id
            );
            externalDataEvents.AddRange(externalEventsFromExternalService);

            var events = await ExternalDataEvent.GetMultiServiceEventsAsync(httpClient,
                    projectionsToInit,
                    new EventRequester<_ProjectionName_>($"https://service1/api/EventRequest",
                        p => p.externalAggregateExample2Id,
                        p => p.externalAggregateExample3Id
                    )
                );
            externalDataEvents.AddRange(events);
        }

        return externalDataEvents;
        */
    }
}