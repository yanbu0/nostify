

using nostify;

namespace _ServiceName__Service;

public sealed class Create__ReplaceMe_ : EventType
{
    public Create__ReplaceMe_() : base("Create__ReplaceMe_", isNew: true)
    {
    }
}

public sealed class Update__ReplaceMe_ : EventType
{
    public Update__ReplaceMe_() : base("Update__ReplaceMe_")
    {
    }
}

public sealed class Delete__ReplaceMe_ : EventType
{
    public Delete__ReplaceMe_() : base("Delete__ReplaceMe_", isNew: false, allowNullPayload: true)
    {
    }
}

public sealed class BulkCreate__ReplaceMe_ : EventType
{
    public BulkCreate__ReplaceMe_() : base("BulkCreate__ReplaceMe_", isNew: true)
    {
    }
}

public sealed class BulkUpdate__ReplaceMe_ : EventType
{
    public BulkUpdate__ReplaceMe_() : base("BulkUpdate__ReplaceMe_")
    {
    }
}

public sealed class BulkDelete__ReplaceMe_ : EventType
{
    public BulkDelete__ReplaceMe_() : base("BulkDelete__ReplaceMe_", isNew: false, allowNullPayload: true)
    {
    }
}

public static class _ReplaceMe_Command
{
    public static Create__ReplaceMe_ Create => new Create__ReplaceMe_();
    public static Update__ReplaceMe_ Update => new Update__ReplaceMe_();
    public static Delete__ReplaceMe_ Delete => new Delete__ReplaceMe_();
    public static BulkCreate__ReplaceMe_ BulkCreate => new BulkCreate__ReplaceMe_();
    public static BulkUpdate__ReplaceMe_ BulkUpdate => new BulkUpdate__ReplaceMe_();
    public static BulkDelete__ReplaceMe_ BulkDelete => new BulkDelete__ReplaceMe_();
}