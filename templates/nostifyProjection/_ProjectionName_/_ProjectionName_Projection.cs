

using Microsoft.Azure.Cosmos;
using nostify;

namespace _ReplaceMe__Service;

public class _ProjectionName_ : NostifyObject, IProjection
{
    public _ProjectionName_()
    {

    }

    public static string containerName => "_ProjectionName_";

    public bool isDeleted { get; set; }

    public override void Apply(Event eventToApply)
    {
        //Should update the command tree below to not use string matching
        if (eventToApply.command.name.Equals("Create__ReplaceMe_") || eventToApply.command.name.Equals("Update__ReplaceMe_"))
        {
            this.UpdateProperties<_ProjectionName_>(eventToApply.payload);
        }
        else if (eventToApply.command.name.Equals("Delete__ReplaceMe_"))
        {
            this.isDeleted = true;
        }
    }

    public async Task<Event> SeedExternalDataAsync(INostify nostify, HttpClient? httpClient = null)
    {
        //Replace this with getting all the data from exernal aggregates and creating an event
        throw new NotImplementedException();
    }

    public static async Task InitContainerAsync(INostify nostify, HttpClient? httpClient = null)
    {
        //Delete the container to start fresh
        Container container = await nostify.GetProjectionContainerAsync<_ProjectionName_>();
        string containerName = container.Id;
        await container.DeleteContainerAsync();

        //Replace this with getting all the data and re-creating the container using containerName
        throw new NotImplementedException();
    }

}