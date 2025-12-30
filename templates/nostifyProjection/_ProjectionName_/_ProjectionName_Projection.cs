

using System.Net.Http.Json;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using nostify;

namespace _ReplaceMe__Service;

public class _ProjectionName_ : NostifyObject, IProjection, IHasExternalData<_ProjectionName_>
{
    public _ProjectionName_()
    {
        
    }   

    public static string containerName => "_ProjectionName_";

    public bool initialized { get; set; } = false;

    public bool isDeleted { get; set; }

    //These are examples to show how to query data events and can be removed after creation
    //**********************************************************************************************
    public Guid sameServiceAggregateExampleId { get; set; }
    public string sameServiceAggregateExampleName { get; set; }
    public Guid sameServiceAggregateExampleId2 { get; set; }
    public string sameServiceAggregateExampleName2 { get; set; }
    public Guid externalAggregateExample1Id { get; set; }
    public string externalAggregateExample1Name { get; set; }
    public Guid externalAggregateExample2Id { get; set; }
    public string externalAggregateExample2Name { get; set; }
    public Guid externalAggregateExample3Id { get; set; }
    public string externalAggregateExample3Name { get; set; }
    public Guid externalAggregateExample4Id { get; set; }
    public string externalAggregateExample4Name { get; set; }
    public Guid externalAggregateExample5Id { get; set; }
    public string externalAggregateExample5Name { get; set; }
    public Guid externalAggregateExample6Id { get; set; }
    public string externalAggregateExample6Name { get; set; }


    //**********************************************************************************************

    public override void Apply(IEvent eventToApply)
    {
        //Should update the command tree below to not use string matching
        if (eventToApply.command.name.Equals("Create__ReplaceMe_") 
                || eventToApply.command.name.Equals("Update__ReplaceMe_"))
        {
            this.UpdateProperties<_ProjectionName_>(eventToApply.payload);
        }
        else if (eventToApply.command.name.Equals("Delete__ReplaceMe_"))
        {
            this.isDeleted = true;
            this.ttl = 1;
        }
    }

    public async static Task<List<ExternalDataEvent>> GetExternalDataEventsAsync(List<_ProjectionName_> projectionsToInit, INostify nostify, HttpClient? httpClient = null, DateTime? pointInTime = null)
    {
        // RECOMMENDED: Use ExternalDataEventFactory for cleaner, more maintainable code
        var factory = new ExternalDataEventFactory<_ProjectionName_>(
            nostify,
            projectionsToInit,
            httpClient,
            pointInTime);

        // Get events from same service for related aggregates
        factory.WithSameServiceIdSelectors(
            p => p.sameServiceAggregateExampleId,
            p => p.sameServiceAggregateExampleId2);

        // Get events from external services (if httpClient provided)
        if (httpClient != null)
        {
            // Add external service requestors
            factory.WithEventRequestor(
                "https://externalDataExampleUrl/api/EventRequest",
                p => p.externalAggregateExample1Id);

            // For multiple external services, chain additional requestors
            factory.WithEventRequestor(
                "https://service1/api/EventRequest",
                p => p.externalAggregateExample2Id,
                p => p.externalAggregateExample3Id,
                p => p.externalAggregateExample4Id);
            
            factory.WithEventRequestor(
                "https://service2/api/EventRequest",
                p => p.externalAggregateExample5Id);
            
            factory.WithEventRequestor(
                "https://service3/api/EventRequest",
                p => p.externalAggregateExample6Id);
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
                        p => p.externalAggregateExample3Id,
                        p => p.externalAggregateExample4Id
                    ),
                    new EventRequester<_ProjectionName_>($"https://service2/api/EventRequest",
                        p => p.externalAggregateExample5Id
                    ),
                    new EventRequester<_ProjectionName_>($"https://service3/api/EventRequest",
                        p => p.externalAggregateExample6Id
                    )
                );
                externalDataEvents.AddRange(events);
        }

        return externalDataEvents;
        */
    }
}