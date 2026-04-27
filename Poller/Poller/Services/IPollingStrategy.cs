using Poller.Models;

namespace Poller.Services
{
    // Abstractions/IPollingStrategy.cs
    public interface IPollingStrategy<TRequestData, TResponseData>
    {
        Task<PollingResult<TResponseData>> PollAsync(TRequestData requestData, CancellationToken cancellationToken);
    }
}
