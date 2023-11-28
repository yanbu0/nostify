
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
        if (eventToApply.command == _ReplaceMe_Command.Create || eventToApply.command == _ReplaceMe_Command.Update)
        {
            this.UpdateProperties<_ProjectionName_>(eventToApply.payload);
        }
        else if (eventToApply.command == _ReplaceMe_Command.Delete)
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