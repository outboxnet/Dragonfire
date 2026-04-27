using Dragonfire.Caching.Attributes;

namespace CacheTesting.Service
{
    public class DataModel
    {
        public required string Data1 { get; set; }

        public decimal Data2 { get; set; }
    }

    public interface IDataService
    {
        [Cache(SlidingExpirationSeconds = 100000, KeyTemplate = "data:{param}")]
        Task<IReadOnlyCollection<DataModel>> GetAsync(string? param);
    }

    public class DataService : IDataService
    {
        public async Task<IReadOnlyCollection<DataModel>> GetAsync(string? param)
        {
            var data = Enumerable.Range(0, 100_000)
                .Select(x => new DataModel
                {
                    Data2 = x,
                    Data1 = $"{param}_test_{x}"
                }).ToList();

            return await Task.FromResult(data);
        }
    }
}
