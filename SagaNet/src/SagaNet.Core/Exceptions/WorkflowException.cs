namespace SagaNet.Core.Exceptions;

public class WorkflowException : Exception
{
    public Guid? WorkflowInstanceId { get; }

    public WorkflowException(string message) : base(message) { }

    public WorkflowException(string message, Guid instanceId) : base(message)
        => WorkflowInstanceId = instanceId;

    public WorkflowException(string message, Exception inner) : base(message, inner) { }

    public WorkflowException(string message, Guid instanceId, Exception inner)
        : base(message, inner)
        => WorkflowInstanceId = instanceId;
}
