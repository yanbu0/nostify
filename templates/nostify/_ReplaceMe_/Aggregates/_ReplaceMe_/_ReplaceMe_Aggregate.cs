using nostify;


namespace _ReplaceMe__Service;

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
        if (eventType == _ReplaceMe_Command.Create || eventType == _ReplaceMe_Command.BulkCreate || eventType == _ReplaceMe_Command.Update)
        {
            //Note: this uses reflection, may be desirable to optimize
            this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
        }
        else if (eventType == _ReplaceMe_Command.Delete)
        {
            this.isDeleted = true;
        }
    }
}



