

using nostify;

namespace _ReplaceMe__Service;

public class _ReplaceMe_Command : NostifyCommand
{
    ///<summary>
    ///Base Create Command
    ///</summary>
    public static readonly _ReplaceMe_Command Create = new _ReplaceMe_Command("Create__ReplaceMe_");
    ///<summary>
    ///Base Update Command
    ///</summary>
    public static readonly _ReplaceMe_Command Update = new _ReplaceMe_Command("Update__ReplaceMe_");
    ///<summary>
    ///Base Delete Command
    ///</summary>
    public static readonly _ReplaceMe_Command Delete = new _ReplaceMe_Command("Delete__ReplaceMe_");


    public _ReplaceMe_Command(string name)
    : base(name)
    {

    }
}