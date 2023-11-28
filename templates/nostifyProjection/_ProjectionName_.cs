
using nostify;

namespace _ReplaceMe__Service;

public class _ProjectionName_ : Projection
{
    public _ProjectionName_()
    {

    }

    new public static string containerName => "_ProjectionName_";

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

    public override Task<Event> Seed(Nostify nostify, HttpClient? httpClient = null)
    {
        throw new NotImplementedException();
    }

    public override Task InitContainer(Nostify nostify, HttpClient? httpClient = null)
    {
        throw new NotImplementedException();
    }

}