using Dragonfire.Poller.Models;

namespace Dragonfire.Poller.Services
{
    // Abstractions/IPollingStrategy.cs
    public interface IPollingStrategy<TRequestData, TResponseData>
    {
        Task<PollingResult<TResponseData>> PollAsync(TRequestData requestData, CancellationToken cancellationToken);
    }
}
