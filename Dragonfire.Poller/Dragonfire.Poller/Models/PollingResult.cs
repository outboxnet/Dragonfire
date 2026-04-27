namespace Dragonfire.Poller.Models
{
    /// <summary>
    /// Represents the outcome of a single polling attempt made by <see cref="Services.IPollingStrategy{TRequestData,TResponseData}"/>.
    /// </summary>
    public class PollingResult<TResponseData>
    {
        public bool IsSuccess { get; set; }
        public TResponseData? Data { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// When <c>false</c> on a failed result the framework stops retrying immediately.
        /// Defaults to <c>true</c> so transient errors are retried automatically.
        /// </summary>
        public bool ShouldContinue { get; set; } = true;

        /// <summary>Data received; polling should continue (e.g. response says "still processing").</summary>
        public static PollingResult<TResponseData> Success(TResponseData data, bool shouldContinue = false)
            => new() { IsSuccess = true, Data = data, ShouldContinue = shouldContinue };

        /// <summary>Transient or permanent failure.</summary>
        public static PollingResult<TResponseData> Failure(string errorMessage, bool shouldContinue = true)
            => new() { IsSuccess = false, ErrorMessage = errorMessage, ShouldContinue = shouldContinue };

        /// <summary>Terminal success — polling is done.</summary>
        public static PollingResult<TResponseData> Complete(TResponseData data)
            => new() { IsSuccess = true, Data = data, ShouldContinue = false };
    }
}
