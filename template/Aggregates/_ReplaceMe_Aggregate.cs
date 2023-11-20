using nostify;


namespace _ReplaceMe__Service;

public class _ReplaceMe_ : Aggregate
{
    public _ReplaceMe_()
    {
    }

    new public static string aggregateType => "_ReplaceMe_";

    public override void Apply(PersistedEvent pe)
    {
        if (pe.command == _ReplaceMe_Command.Create || pe.command == _ReplaceMe_Command.Update)
        {
            this.UpdateProperties<_ReplaceMe_>(pe.payload);
        }
        else if (pe.command == _ReplaceMe_Command.Delete)
        {
            this.isDeleted = true;
        }
    }
}



