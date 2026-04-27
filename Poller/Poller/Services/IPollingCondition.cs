namespace Poller.Services
{
    /// <summary>
    /// Defines the terminal conditions for a polling operation.
    /// Implement this interface to express when polling should stop with success or failure.
    /// </summary>
    /// <typeparam name="TResponseData">The type returned by the polling strategy.</typeparam>
    public interface IPollingCondition<TResponseData>
    {
        /// <summary>Returns <c>true</c> when the polling goal has been reached.</summary>
        bool IsComplete(TResponseData response);

        /// <summary>Returns <c>true</c> when the operation has failed and should not be retried.</summary>
        bool IsFailed(TResponseData response);
    }
}
