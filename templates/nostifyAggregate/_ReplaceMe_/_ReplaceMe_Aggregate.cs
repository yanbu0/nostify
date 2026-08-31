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

    private protected override void Apply(EventType eventType, IEvent eventToApply)
    {
    }

    private protected void Apply(_ReplaceMe_Command eventType, IEvent eventToApply)
    {
        if (eventType == _ReplaceMe_Command.BulkCreate
            || eventType == _ReplaceMe_Command.Create
            || eventType == _ReplaceMe_Command.Update
            || eventType == _ReplaceMe_Command.BulkUpdate)
        {
            this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
        }
        else if (eventType == _ReplaceMe_Command.Delete
            || eventType == _ReplaceMe_Command.BulkDelete)
        {
            this.isDeleted = true;
        }
    }
}


