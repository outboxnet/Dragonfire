using System.Threading.Tasks;
using Dragonfire.Logging.Abstractions;
using Dragonfire.Logging.Attributes;
using Microsoft.Extensions.Logging;

namespace MyApp.Services
{
    // ── Scenario A: ILoggable on the INTERFACE ────────────────────────────────
    // All [Log], [LogProperty], [LogIgnore] live on the interface.
    // The implementation class carries no logging attributes at all.
    // The class doesn't even need to reference ILoggable directly.

    public interface IOrderService : ILoggable
    {
        [Log]
        Task<string> GetOrderAsync(
            [LogProperty("TenantId")] string tenantId,
            [LogProperty] string orderId);           // key = "orderId" (param name)

        [Log(MaxDepth = 0, Level = LogLevel.Debug)]
        void ProcessOrder(string orderId);

        [LogIgnore]
        string GetVersion();
    }

    public class OrderService : IOrderService    // no ILoggable here — inherited through IOrderService
    {
        public Task<string> GetOrderAsync(string tenantId, string orderId)
            => Task.FromResult($"Order-{orderId}");

        public void ProcessOrder(string orderId) { }

        public string GetVersion() => "1.0";
    }

    // ── Scenario B: ILoggable on the IMPLEMENTATION (original pattern) ────────
    // Attributes can still live on the interface or on the class — both work.

    public interface IInventoryService
    {
        Task<int> GetStockAsync([LogProperty("Sku")] string sku);
    }

    public class InventoryService : IInventoryService, ILoggable
    {
        public Task<int> GetStockAsync(string sku) => Task.FromResult(42);
    }
}
