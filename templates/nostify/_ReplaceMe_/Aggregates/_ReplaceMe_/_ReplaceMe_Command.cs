

using nostify;

namespace _ReplaceMe__Service;

public abstract class _ReplaceMe_Command : EventType
{
    ///<summary>
    ///Base Create Command
    ///</summary>
    public static _ReplaceMe_Create Create => _ReplaceMe_Create.Instance;
    ///<summary>
    ///Base Update Command
    ///</summary>
    public static _ReplaceMe_Update Update => _ReplaceMe_Update.Instance;
    ///<summary>
    ///Base Delete Command
    ///</summary>
    public static _ReplaceMe_Delete Delete => _ReplaceMe_Delete.Instance;
    ///<summary>
    ///Bulk Create Command
    ///</summary>
    public static _ReplaceMe_BulkCreate BulkCreate => _ReplaceMe_BulkCreate.Instance;
    ///<summary>
    ///Bulk Update Command
    ///</summary>
    public static _ReplaceMe_BulkUpdate BulkUpdate => _ReplaceMe_BulkUpdate.Instance;
    ///<summary>
    ///Bulk Delete Command
    ///</summary>
    public static _ReplaceMe_BulkDelete BulkDelete => _ReplaceMe_BulkDelete.Instance;


    protected _ReplaceMe_Command(string name, bool isNew = false)
    : base(name, isNew)
    {

    }
}

public sealed class _ReplaceMe_Create : _ReplaceMe_Command
{
    public static readonly _ReplaceMe_Create Instance = new _ReplaceMe_Create();
    private _ReplaceMe_Create() : base("Create__ReplaceMe_", true)
    {
    }
}

public sealed class _ReplaceMe_Update : _ReplaceMe_Command
{
    public static readonly _ReplaceMe_Update Instance = new _ReplaceMe_Update();
    private _ReplaceMe_Update() : base("Update__ReplaceMe_")
    {
    }
}

public sealed class _ReplaceMe_Delete : _ReplaceMe_Command
{
    public static readonly _ReplaceMe_Delete Instance = new _ReplaceMe_Delete();
    private _ReplaceMe_Delete() : base("Delete__ReplaceMe_")
    {
    }
}

public sealed class _ReplaceMe_BulkCreate : _ReplaceMe_Command
{
    public static readonly _ReplaceMe_BulkCreate Instance = new _ReplaceMe_BulkCreate();
    private _ReplaceMe_BulkCreate() : base("BulkCreate__ReplaceMe_", true)
    {
    }
}

public sealed class _ReplaceMe_BulkUpdate : _ReplaceMe_Command
{
    public static readonly _ReplaceMe_BulkUpdate Instance = new _ReplaceMe_BulkUpdate();
    private _ReplaceMe_BulkUpdate() : base("BulkUpdate__ReplaceMe_")
    {
    }
}

public sealed class _ReplaceMe_BulkDelete : _ReplaceMe_Command
{
    public static readonly _ReplaceMe_BulkDelete Instance = new _ReplaceMe_BulkDelete();
    private _ReplaceMe_BulkDelete() : base("BulkDelete__ReplaceMe_")
    {
    }
}