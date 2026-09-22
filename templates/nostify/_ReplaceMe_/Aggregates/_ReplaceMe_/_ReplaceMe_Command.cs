

using nostify;

namespace _ReplaceMe__Service;

public sealed class Create__ReplaceMe_ : EventType<Create__ReplaceMe_>, IEventType
{
    public static string name => "Create__ReplaceMe_";
    public Create__ReplaceMe_() : base(name, isNew: true)
    {
    }
}

public sealed class Update__ReplaceMe_ : EventType<Update__ReplaceMe_>, IEventType
{
    public static string name => "Update__ReplaceMe_";
    public Update__ReplaceMe_() : base(name)
    {
    }
}

public sealed class Delete__ReplaceMe_ : EventType<Delete__ReplaceMe_>, IEventType
{
    public static string name => "Delete__ReplaceMe_";
    public Delete__ReplaceMe_() : base(name, isNew: false, allowNullPayload: true)
    {
    }
}

public sealed class BulkCreate__ReplaceMe_ : EventType<BulkCreate__ReplaceMe_>, IEventType
{
    public static string name => "BulkCreate__ReplaceMe_";
    public BulkCreate__ReplaceMe_() : base(name, isNew: true)
    {
    }
}

public sealed class BulkUpdate__ReplaceMe_ : EventType<BulkUpdate__ReplaceMe_>, IEventType
{
    public static string name => "BulkUpdate__ReplaceMe_";
    public BulkUpdate__ReplaceMe_() : base(name)
    {
    }
}

public sealed class BulkDelete__ReplaceMe_ : EventType<BulkDelete__ReplaceMe_>, IEventType
{
    public static string name => "BulkDelete__ReplaceMe_";
    public BulkDelete__ReplaceMe_() : base(name, isNew: false, allowNullPayload: true)
    {
    }
}

public static class _ReplaceMe_Command
{
    public static Create__ReplaceMe_ Create => Create__ReplaceMe_.Instance;
    public static Update__ReplaceMe_ Update => Update__ReplaceMe_.Instance;
    public static Delete__ReplaceMe_ Delete => Delete__ReplaceMe_.Instance;
    public static BulkCreate__ReplaceMe_ BulkCreate => BulkCreate__ReplaceMe_.Instance;
    public static BulkUpdate__ReplaceMe_ BulkUpdate => BulkUpdate__ReplaceMe_.Instance;
    public static BulkDelete__ReplaceMe_ BulkDelete => BulkDelete__ReplaceMe_.Instance;
}