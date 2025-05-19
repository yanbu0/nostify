
namespace nostify;

public enum SagaStepStatus
{
    WaitingForTrigger,
    Triggered,
    CompletedSuccessfully,
    RollingBack,
    RolledBack
}