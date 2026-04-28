using System.Threading.Tasks;
using Dragonfire.Caching.Abstractions;
using Dragonfire.Caching.Attributes;

namespace MyApp.Services
{
    public class Order
    {
        public required string Id { get; set; }
        public required string TenantId { get; set; }
        public decimal Total { get; set; }
    }

    // ── Scenario A: ICacheable on the INTERFACE ────────────────────────────────
    // [Cache] / [CacheInvalidate] / [CacheKey] all live on the interface.
    // The implementation class carries no caching attributes at all.

    public interface IOrderService : ICacheable
    {
        [Cache(SlidingExpirationSeconds = 300, KeyTemplate = "order:{tenantId}:{orderId}",
               Tags = new[] { "tenant:{tenantId}" })]
        Task<Order?> GetOrderAsync(
            [CacheKey("tenantId")] string tenantId,
            [CacheKey] string orderId);

        [Cache(AbsoluteExpirationSeconds = 60)]
        Task<int> GetOrderCountAsync(string tenantId);

        [CacheInvalidate("order:{tenantId}:*")]
        [CacheInvalidate("ordercount:*")]
        Task UpdateOrderAsync(string tenantId, Order order);

        [CacheInvalidate("tenant", "tenantId")]
        Task PurgeTenantAsync(string tenantId);
    }

    public class OrderService : IOrderService
    {
        public Task<Order?> GetOrderAsync(string tenantId, string orderId)
            => Task.FromResult<Order?>(new Order { Id = orderId, TenantId = tenantId, Total = 0m });

        public Task<int> GetOrderCountAsync(string tenantId) => Task.FromResult(0);

        public Task UpdateOrderAsync(string tenantId, Order order) => Task.CompletedTask;

        public Task PurgeTenantAsync(string tenantId) => Task.CompletedTask;
    }

    // ── Scenario B: ICacheable on the IMPLEMENTATION ─────────────────────────
    // Attributes can live on either side; both are read by the generator.

    public interface IInventoryService
    {
        [Cache(SlidingExpirationSeconds = 120, KeyTemplate = "stock:{sku}")]
        Task<int> GetStockAsync(string sku);
    }

    public class InventoryService : IInventoryService, ICacheable
    {
        public Task<int> GetStockAsync(string sku) => Task.FromResult(42);
    }
}
