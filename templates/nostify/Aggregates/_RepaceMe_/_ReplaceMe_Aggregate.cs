using nostify;


namespace _ReplaceMe__Service;

public class _ReplaceMe_ : Aggregate
{
    public _ReplaceMe_()
    {
    }

    new public static string aggregateType => "_ReplaceMe_";

    public override void Apply(Event eventToApply)
    {
        if (eventToApply.command == _ReplaceMe_Command.Create || eventToApply.command == _ReplaceMe_Command.Update)
        {
            this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
        }
        else if (eventToApply.command == _ReplaceMe_Command.Delete)
        {
            this.isDeleted = true;
        }
    }
}



