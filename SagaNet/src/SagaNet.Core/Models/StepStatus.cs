namespace SagaNet.Core.Models;

/// <summary>Lifecycle status of an <see cref="ExecutionPointer"/>.</summary>
public enum StepStatus
{
    Pending = 0,
    Running = 1,
    Complete = 2,
    Failed = 3,
    Compensating = 4,
    Compensated = 5,
    WaitingForEvent = 6,
    Skipped = 7
}
