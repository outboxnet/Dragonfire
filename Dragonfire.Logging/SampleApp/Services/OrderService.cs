using Dragonfire.Logging.Abstractions;
using Dragonfire.Logging.Attributes;

namespace SampleApp.Services
{
    public class OrderService : IOrderService
    {
        public Task<Order> GetOrderAsync(string tenantId, string orderId)
        {
            return Task.FromResult<Order>(null);
        }

        public void ProcessOrder(string orderId) { 
        
        }

        public string GetVersion() => "1.0";
    }
}
