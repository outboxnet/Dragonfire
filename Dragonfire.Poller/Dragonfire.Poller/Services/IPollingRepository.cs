namespace Dragonfire.Poller.Services
{
    // Abstractions/IPollingRepository.cs
    public interface IPollingRepository<TRequestData, TResponseData>
    {
        Task<PollingRequest<TRequestData, TResponseData>?> GetAsync(Guid id);
        Task SaveAsync(PollingRequest<TRequestData, TResponseData> request);
        Task UpdateAsync(PollingRequest<TRequestData, TResponseData> request);
        Task DeleteAsync(Guid id);
    }
}
