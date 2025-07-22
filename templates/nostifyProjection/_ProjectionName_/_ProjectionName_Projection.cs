

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


    //**********************************************************************************************

    public override void Apply(Event eventToApply)
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
        // If data exists within this service, even if a different container, use the container to get the data
        Container sameServiceEventStore = await nostify.GetEventStoreContainerAsync();
        
        //Use GetEventsAsync to get events from the same service, the selectors are a parameter list of the properties that are used to filter the events
        List<ExternalDataEvent> externalDataEvents = await ExternalDataEvent.GetEventsAsync(sameServiceEventStore, 
            projectionsToInit, 
            p => p.sameServiceAggregateExampleId,
            p => p.sameServiceAggregateExampleId2);

        externalDataEvents.AddRange(externalDataEvents);

        // Get external data necessary to initialize projections here
        // To access data in other services, use httpClient and the EventRequest endpoint
        if (httpClient != null)
        {
            //An EventRequest endpoint is created by the template in every service
            var externalEventsFromExternalService = await ExternalDataEvent.GetEventsAsync(httpClient,
                "https://externalDataExampleUrl/api/EventRequest",
                projectionsToInit,
                p => p.externalAggregateExample1Id
            );
            externalDataEvents.AddRange(externalEventsFromExternalService);

            //If this Projection has more than one external Aggregate to get data from, create more queries, 
            //its OK to create multiple ExternalDataEvent objects for the same Projection, all of the Events get flattened into one list
            //in the InitAsync method
            var externalEventsFromSecondExternalService = await ExternalDataEvent.GetEventsAsync(httpClient,
                "https://externalDataExampleUrl2/api/EventRequest",
                projectionsToInit,
                p => p.externalAggregateExample2Id
            );
            externalDataEvents.AddRange(externalEventsFromSecondExternalService);
        }

        return externalDataEvents;
    }
}