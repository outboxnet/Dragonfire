namespace SagaNet.Core.Models;

/// <summary>Lifecycle status of a <see cref="WorkflowInstance"/>.</summary>
public enum WorkflowStatus
{
    /// <summary>Created and ready to run on the next polling cycle.</summary>
    Runnable = 0,

    /// <summary>Currently being executed by a workflow host.</summary>
    Running = 1,

    /// <summary>All steps completed successfully.</summary>
    Complete = 2,

    /// <summary>A step failed and compensation was triggered (saga rollback).</summary>
    Compensated = 3,

    /// <summary>A terminal failure — compensation was attempted but also failed.</summary>
    Failed = 4,

    /// <summary>Waiting for an external event before continuing.</summary>
    Suspended = 5,

    /// <summary>Terminated externally before completion.</summary>
    Terminated = 6
}
