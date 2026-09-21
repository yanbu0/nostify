

using nostify;

namespace _ServiceName__Service;

public sealed class Create__ReplaceMe_ : EventType<Create__ReplaceMe_>
{
    public Create__ReplaceMe_() : base("Create__ReplaceMe_", isNew: true)
    {
    }
}

public sealed class Update__ReplaceMe_ : EventType<Update__ReplaceMe_>
{
    public Update__ReplaceMe_() : base("Update__ReplaceMe_")
    {
    }
}

public sealed class Delete__ReplaceMe_ : EventType<Delete__ReplaceMe_>
{
    public Delete__ReplaceMe_() : base("Delete__ReplaceMe_", isNew: false, allowNullPayload: true)
    {
    }
}

public sealed class BulkCreate__ReplaceMe_ : EventType<BulkCreate__ReplaceMe_>
{
    public BulkCreate__ReplaceMe_() : base("BulkCreate__ReplaceMe_", isNew: true)
    {
    }
}

public sealed class BulkUpdate__ReplaceMe_ : EventType<BulkUpdate__ReplaceMe_>
{
    public BulkUpdate__ReplaceMe_() : base("BulkUpdate__ReplaceMe_")
    {
    }
}

public sealed class BulkDelete__ReplaceMe_ : EventType<BulkDelete__ReplaceMe_>
{
    public BulkDelete__ReplaceMe_() : base("BulkDelete__ReplaceMe_", isNew: false, allowNullPayload: true)
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