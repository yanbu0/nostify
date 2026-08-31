

using nostify;

namespace _ReplaceMe__Service;

public abstract class _ReplaceMe_Command : EventType
{
    ///<summary>
    ///Base Create Command
    ///</summary>
    public static Create__ReplaceMe_ Create => Create__ReplaceMe_.Instance;
    ///<summary>
    ///Base Update Command
    ///</summary>
    public static Update__ReplaceMe_ Update => Update__ReplaceMe_.Instance;
    ///<summary>
    ///Base Delete Command
    ///</summary>
    public static Delete__ReplaceMe_ Delete => Delete__ReplaceMe_.Instance;
    ///<summary>
    ///Bulk Create Command
    ///</summary>
    public static BulkCreate__ReplaceMe_ BulkCreate => BulkCreate__ReplaceMe_.Instance;
    ///<summary>
    ///Bulk Update Command
    ///</summary>
    public static BulkUpdate__ReplaceMe_ BulkUpdate => BulkUpdate__ReplaceMe_.Instance;
    ///<summary>
    ///Bulk Delete Command
    ///</summary>
    public static BulkDelete__ReplaceMe_ BulkDelete => BulkDelete__ReplaceMe_.Instance;


    /// <summary>
    /// Protected base constructor for command types.
    /// The <paramref name="name"/> parameter is the event name in the format <Action>_<AggregateName>,
    /// for example "Create__ReplaceMe_" or "BulkUpdate_Order".
    /// </summary>
    /// <param name="name">Event type name in the format <Action>_<AggregateName>.</param>
    /// <param name="isNew">True when the command represents creation of a new aggregate instance.</param>
    protected _ReplaceMe_Command(string name, bool isNew = false)
        : base(name, isNew)
    {
    }
}

public sealed class Create__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly Create__ReplaceMe_ Instance = new Create__ReplaceMe_();

    /// <summary>
    /// Create command for the _ReplaceMe_ aggregate.
    /// EventType name: Create__ReplaceMe_ (<Action>_<AggregateName>).
    /// </summary>
    private Create__ReplaceMe_() : base("Create__ReplaceMe_", true)
    {
    }
}

public sealed class Update__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly Update__ReplaceMe_ Instance = new Update__ReplaceMe_();

    /// <summary>
    /// Update command for the _ReplaceMe_ aggregate.
    /// EventType name: Update__ReplaceMe_ (<Action>_<AggregateName>).
    /// </summary>
    private Update__ReplaceMe_() : base("Update__ReplaceMe_")
    {
    }
}

public sealed class Delete__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly Delete__ReplaceMe_ Instance = new Delete__ReplaceMe_();

    /// <summary>
    /// Delete command for the _ReplaceMe_ aggregate.
    /// EventType name: Delete__ReplaceMe_ (<Action>_<AggregateName>).
    /// </summary>
    private Delete__ReplaceMe_() : base("Delete__ReplaceMe_")
    {
    }
}

public sealed class BulkCreate__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly BulkCreate__ReplaceMe_ Instance = new BulkCreate__ReplaceMe_();

    /// <summary>
    /// Bulk create command for the _ReplaceMe_ aggregate.
    /// EventType name: BulkCreate__ReplaceMe_ (<Action>_<AggregateName>).
    /// </summary>
    private BulkCreate__ReplaceMe_() : base("BulkCreate__ReplaceMe_", true)
    {
    }
}

public sealed class BulkUpdate__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly BulkUpdate__ReplaceMe_ Instance = new BulkUpdate__ReplaceMe_();

    /// <summary>
    /// Bulk update command for the _ReplaceMe_ aggregate.
    /// EventType name: BulkUpdate__ReplaceMe_ (<Action>_<AggregateName>).
    /// </summary>
    private BulkUpdate__ReplaceMe_() : base("BulkUpdate__ReplaceMe_")
    {
    }
}

public sealed class BulkDelete__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly BulkDelete__ReplaceMe_ Instance = new BulkDelete__ReplaceMe_();

    /// <summary>
    /// Bulk delete command for the _ReplaceMe_ aggregate.
    /// EventType name: BulkDelete__ReplaceMe_ (<Action>_<AggregateName>).
    /// </summary>
    private BulkDelete__ReplaceMe_() : base("BulkDelete__ReplaceMe_")
    {
    }
}