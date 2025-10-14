using nostify;

namespace _ServiceName__Service;

public class _ReplaceMe_ : _ReplaceMe_BaseClass, IAggregate
{
    public _ReplaceMe_()
    {
    }

    public bool isDeleted { get; set; } = false;

    public static string aggregateType => "_ReplaceMe_";
    public static string currentStateContainerName => $"{aggregateType}CurrentState";

    public override void Apply(IEvent eventToApply)
    {
        if (eventToApply.command == _ReplaceMe_Command.BulkCreate || eventToApply.command == _ReplaceMe_Command.Create || eventToApply.command == _ReplaceMe_Command.Update)
        {
            this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
        }
        else if (eventToApply.command == _ReplaceMe_Command.Delete)
        {
            this.isDeleted = true;
        }
    }
}



