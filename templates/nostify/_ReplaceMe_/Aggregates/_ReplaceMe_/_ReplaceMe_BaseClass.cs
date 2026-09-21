using nostify;


namespace _ReplaceMe__Service;

public abstract class _ReplaceMe_BaseClass : NostifyObject
{
    protected override void Apply(EventType eventType, IEvent eventToApply)
    {
        switch (eventType)
        {
            case Create__ReplaceMe_ create:
                Apply(create, eventToApply);
                break;
            case Update__ReplaceMe_ update:
                Apply(update, eventToApply);
                break;
            case Delete__ReplaceMe_ delete:
                Apply(delete, eventToApply);
                break;
            case BulkCreate__ReplaceMe_ bulkCreate:
                Apply(bulkCreate, eventToApply);
                break;
            case BulkUpdate__ReplaceMe_ bulkUpdate:
                Apply(bulkUpdate, eventToApply);
                break;
            case BulkDelete__ReplaceMe_ bulkDelete:
                Apply(bulkDelete, eventToApply);
                break;
            default:
                throw new InvalidOperationException($"Unsupported event type '{eventType.GetType().Name}' for '{GetType().Name}'.");
        }
    }

    protected abstract void Apply(Create__ReplaceMe_ eventType, IEvent eventToApply);
    protected abstract void Apply(Update__ReplaceMe_ eventType, IEvent eventToApply);
    protected abstract void Apply(Delete__ReplaceMe_ eventType, IEvent eventToApply);
    protected abstract void Apply(BulkCreate__ReplaceMe_ eventType, IEvent eventToApply);
    protected abstract void Apply(BulkUpdate__ReplaceMe_ eventType, IEvent eventToApply);
    protected abstract void Apply(BulkDelete__ReplaceMe_ eventType, IEvent eventToApply);
}