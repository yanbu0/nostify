

using nostify;

namespace TestAggregate_Service;

public class TestAggregateCommand : NostifyCommand
{
    ///<summary>
    ///Base Create Command
    ///</summary>
    public static readonly TestAggregateCommand Create = new TestAggregateCommand("Create_TestAggregate", true);
    ///<summary>
    ///Base Update Command
    ///</summary>
    public static readonly TestAggregateCommand Update = new TestAggregateCommand("Update_TestAggregate");
    ///<summary>
    ///Base Delete Command
    ///</summary>
    public static readonly TestAggregateCommand Delete = new TestAggregateCommand("Delete_TestAggregate");
    ///<summary>
    ///Bulk Create Command
    ///</summary>
    public static readonly TestAggregateCommand BulkCreate = new TestAggregateCommand("BulkCreate_TestAggregate", true);


    public TestAggregateCommand(string name, bool isNew = false)
    : base(name, isNew)
    {

    }
}