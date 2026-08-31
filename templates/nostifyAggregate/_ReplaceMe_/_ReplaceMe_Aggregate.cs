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

    // Apply handlers use the new typed EventType Apply overloads.
    // These methods are invoked by the Nostify infrastructure via dynamic dispatch based on the
    // concrete EventType of the incoming event.

    /// <summary>
    /// Handles Create events for the aggregate.
    /// Populates the aggregate properties from the event payload.
    /// </summary>
    protected void Apply(Create__ReplaceMe_ eventType, IEvent eventToApply)
    {
        this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
    }

    /// <summary>
    /// Handles Update events for the aggregate.
    /// Populates the aggregate properties from the event payload.
    /// </summary>
    protected void Apply(Update__ReplaceMe_ eventType, IEvent eventToApply)
    {
        this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
    }

    /// <summary>
    /// Handles bulk create events for the aggregate.
    /// Populates the aggregate properties from the event payload.
    /// </summary>
    protected void Apply(BulkCreate__ReplaceMe_ eventType, IEvent eventToApply)
    {
        this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
    }

    /// <summary>
    /// Handles bulk update events for the aggregate.
    /// Populates the aggregate properties from the event payload.
    /// </summary>
    protected void Apply(BulkUpdate__ReplaceMe_ eventType, IEvent eventToApply)
    {
        this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
    }

    /// <summary>
    /// Handles delete events for the aggregate.
    /// Marks the aggregate as deleted.
    /// </summary>
    protected void Apply(Delete__ReplaceMe_ eventType, IEvent eventToApply)
    {
        this.isDeleted = true;
    }

    /// <summary>
    /// Handles bulk delete events for the aggregate.
    /// Marks the aggregate as deleted.
    /// </summary>
    protected void Apply(BulkDelete__ReplaceMe_ eventType, IEvent eventToApply)
    {
        this.isDeleted = true;
    }
}

