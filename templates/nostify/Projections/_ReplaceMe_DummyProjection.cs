
using _ReplaceMe__Service;
using nostify;

namespace _ReplaceMe__Service_Service;

public class _ReplaceMe_DummyProjection : Projection
{
    public _ReplaceMe_DummyProjection()
    {

    }

    new public static string containerName => "_ReplaceMe_DummyProjection";

    public bool isDeleted { get; set; }

    public override void Apply(Event eventToApply)
    {
        if (eventToApply.command == _ReplaceMe_Command.Create || eventToApply.command == _ReplaceMe_Command.Update)
        {
            this.UpdateProperties<_ReplaceMe_DummyProjection>(eventToApply.payload);
        }
        else if (eventToApply.command == _ReplaceMe_Command.Delete)
        {
            this.isDeleted = true;
        }
    }

    public override async Task<Event> Seed(Nostify nostify, HttpClient? httpClient = null)
    {
        throw new NotImplementedException();
    }

    public override async Task InitContainer(Nostify nostify, HttpClient? httpClient = null)
    {
        throw new NotImplementedException();
    }

}