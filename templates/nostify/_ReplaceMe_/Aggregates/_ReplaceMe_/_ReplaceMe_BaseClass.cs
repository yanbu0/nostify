using nostify;


namespace _ReplaceMe__Service;

public abstract class _ReplaceMe_BaseClass : NostifyObject
{
    protected override void Apply(EventType eventType, IEvent eventToApply)
    {
        switch (eventType)
        {
            case _ReplaceMe_Create create:
                Apply(create, eventToApply);
                break;
            case _ReplaceMe_Update update:
                Apply(update, eventToApply);
                break;
            case _ReplaceMe_Delete delete:
                Apply(delete, eventToApply);
                break;
            case _ReplaceMe_BulkCreate bulkCreate:
                Apply(bulkCreate, eventToApply);
                break;
            case _ReplaceMe_BulkUpdate bulkUpdate:
                Apply(bulkUpdate, eventToApply);
                break;
            case _ReplaceMe_BulkDelete bulkDelete:
                Apply(bulkDelete, eventToApply);
                break;
            default:
                throw new InvalidOperationException($"Unsupported event type '{eventType.GetType().Name}' for '{GetType().Name}'.");
        }
    }

    protected abstract void Apply(_ReplaceMe_Create eventType, IEvent eventToApply);
    protected abstract void Apply(_ReplaceMe_Update eventType, IEvent eventToApply);
    protected abstract void Apply(_ReplaceMe_Delete eventType, IEvent eventToApply);
    protected abstract void Apply(_ReplaceMe_BulkCreate eventType, IEvent eventToApply);
    protected abstract void Apply(_ReplaceMe_BulkUpdate eventType, IEvent eventToApply);
    protected abstract void Apply(_ReplaceMe_BulkDelete eventType, IEvent eventToApply);
}