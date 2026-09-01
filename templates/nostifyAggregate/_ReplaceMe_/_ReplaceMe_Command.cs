

using nostify;

namespace _ServiceName__Service;

public abstract class _ReplaceMe_Command : NostifyCommand
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


    protected _ReplaceMe_Command(string name, bool isNew = false)
    : base(name, isNew)
    {

    }
}

public sealed class Create__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly Create__ReplaceMe_ Instance = new Create__ReplaceMe_();

    private Create__ReplaceMe_() : base("Create__ReplaceMe_", true)
    {
    }
}

public sealed class Update__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly Update__ReplaceMe_ Instance = new Update__ReplaceMe_();

    private Update__ReplaceMe_() : base("Update__ReplaceMe_")
    {
    }
}

public sealed class Delete__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly Delete__ReplaceMe_ Instance = new Delete__ReplaceMe_();

    private Delete__ReplaceMe_() : base("Delete__ReplaceMe_")
    {
    }
}

public sealed class BulkCreate__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly BulkCreate__ReplaceMe_ Instance = new BulkCreate__ReplaceMe_();

    private BulkCreate__ReplaceMe_() : base("BulkCreate__ReplaceMe_", true)
    {
    }
}

public sealed class BulkUpdate__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly BulkUpdate__ReplaceMe_ Instance = new BulkUpdate__ReplaceMe_();

    private BulkUpdate__ReplaceMe_() : base("BulkUpdate__ReplaceMe_")
    {
    }
}

public sealed class BulkDelete__ReplaceMe_ : _ReplaceMe_Command
{
    public static readonly BulkDelete__ReplaceMe_ Instance = new BulkDelete__ReplaceMe_();

    private BulkDelete__ReplaceMe_() : base("BulkDelete__ReplaceMe_")
    {
    }
}