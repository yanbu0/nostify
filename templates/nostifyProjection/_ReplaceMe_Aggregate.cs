using nostify;


namespace _ReplaceMe__Service;

//Dummy class for template replacement, will not be included when deploying

public class _ReplaceMe_ : NostifyObject, IAggregate
{
    public _ReplaceMe_()
    {
    }

    public bool isDeleted { get; set; } = false;

    public static string aggregateType => "_ReplaceMe_";
    public static string currentStateContainerName => $"{aggregateType}CurrentState";

    public override void Apply(Event eventToApply)
    {
    }
}
