
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
        throw new NotImplementedException();
    }

    public static async Task InitContainerAsync(INostify nostify, HttpClient? httpClient = null)
    {
        throw new NotImplementedException();
    }

}