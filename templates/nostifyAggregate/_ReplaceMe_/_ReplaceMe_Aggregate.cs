using Microsoft.Extensions.Logging;
using nostify;
 
namespace _ServiceName__Service;
 
public class _ReplaceMe_ : _ReplaceMe_BaseClass, IAggregate
{
    private readonly ILogger<_ReplaceMe_> _logger;
 
    public _ReplaceMe_(ILogger<_ReplaceMe_> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
 
    public bool isDeleted { get; set; } = false;
 
    public static string aggregateType => "_ReplaceMe_";
    public static string currentStateContainerName => $"{aggregateType}CurrentState";
 
    /// <summary>
    /// Handles Create events for the aggregate.
    /// Populates the aggregate properties from the event payload.
    /// </summary>
    [ApplyEvents(typeof(Create__ReplaceMe_))]
    protected void OnCreate(IEvent eventToApply)
    {
        try
        {
            this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying Create event to aggregate {AggregateType}. EventType: {EventType}, Event: {@Event}",
                aggregateType,
                eventToApply.eventType,
                eventToApply);
            throw;
        }
    }
 
    /// <summary>
    /// Handles Update events for the aggregate.
    /// Populates the aggregate properties from the event payload.
    /// </summary>
    [ApplyEvents(typeof(Update__ReplaceMe_))]
    protected void OnUpdate(IEvent eventToApply)
    {
        try
        {
            this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying Update event to aggregate {AggregateType}. EventType: {EventType}, Event: {@Event}",
                aggregateType,
                eventToApply.eventType,
                eventToApply);
            throw;
        }
    }
 
    /// <summary>
    /// Handles bulk create events for the aggregate.
    /// Populates the aggregate properties from the event payload.
    /// </summary>
    [ApplyEvents(typeof(BulkCreate__ReplaceMe_))]
    protected void OnBulkCreate(IEvent eventToApply)
    {
        try
        {
            this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying BulkCreate event to aggregate {AggregateType}. EventType: {EventType}, Event: {@Event}",
                aggregateType,
                eventToApply.eventType,
                eventToApply);
            throw;
        }
    }
 
    /// <summary>
    /// Handles bulk update events for the aggregate.
    /// Populates the aggregate properties from the event payload.
    /// </summary>
    [ApplyEvents(typeof(BulkUpdate__ReplaceMe_))]
    protected void OnBulkUpdate(IEvent eventToApply)
    {
        try
        {
            this.UpdateProperties<_ReplaceMe_>(eventToApply.payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying BulkUpdate event to aggregate {AggregateType}. EventType: {EventType}, Event: {@Event}",
                aggregateType,
                eventToApply.eventType,
                eventToApply);
            throw;
        }
    }
 
    /// <summary>
    /// Handles delete events for the aggregate.
    /// Marks the aggregate as deleted.
    /// </summary>
    [ApplyEvents(typeof(Delete__ReplaceMe_))]
    protected void OnDelete(IEvent eventToApply)
    {
        try
        {
            this.isDeleted = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying Delete event to aggregate {AggregateType}. EventType: {EventType}, Event: {@Event}",
                aggregateType,
                eventToApply.eventType,
                eventToApply);
            throw;
        }
    }
 
    /// <summary>
    /// Handles bulk delete events for the aggregate.
    /// Marks the aggregate as deleted.
    /// </summary>
    [ApplyEvents(typeof(BulkDelete__ReplaceMe_))]
    protected void OnBulkDelete(IEvent eventToApply)
    {
        try
        {
            this.isDeleted = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error applying BulkDelete event to aggregate {AggregateType}. EventType: {EventType}, Event: {@Event}",
                aggregateType,
                eventToApply.eventType,
                eventToApply);
            throw;
        }
    }
}
 
