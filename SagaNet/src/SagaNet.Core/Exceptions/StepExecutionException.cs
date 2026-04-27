namespace SagaNet.Core.Exceptions;

public sealed class StepExecutionException : WorkflowException
{
    public string StepName { get; }
    public int AttemptCount { get; }

    public StepExecutionException(
        string stepName,
        int attemptCount,
        Guid instanceId,
        string message,
        Exception? inner = null)
        : base(message, instanceId, inner ?? new Exception(message))
    {
        StepName = stepName;
        AttemptCount = attemptCount;
    }
}
