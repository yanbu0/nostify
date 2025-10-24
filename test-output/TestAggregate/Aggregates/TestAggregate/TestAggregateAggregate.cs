using nostify;


namespace TestAggregate_Service;

public class TestAggregate : TestAggregateBaseClass, IAggregate
{
    public TestAggregate()
    {
    }

    public bool isDeleted { get; set; } = false;

    public static string aggregateType => "TestAggregate";
    public static string currentStateContainerName => $"{aggregateType}CurrentState";

    public override void Apply(IEvent eventToApply)
    {
        if (eventToApply.command == TestAggregateCommand.Create || eventToApply.command == TestAggregateCommand.BulkCreate || eventToApply.command == TestAggregateCommand.Update)
        {
            //Note: this uses reflection, may be desirable to optimize
            this.UpdateProperties<TestAggregate>(eventToApply.payload);
        }
        else if (eventToApply.command == TestAggregateCommand.Delete)
        {
            this.isDeleted = true;
        }
    }
}



