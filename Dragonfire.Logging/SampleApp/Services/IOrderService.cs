using Dragonfire.Logging.Abstractions;
using Dragonfire.Logging.Attributes;

namespace SampleApp.Services
{
    public interface IOrderService: ILoggable
    {
        [Log]
        Task<Order> GetOrderAsync(
            [LogProperty("Tenantds")] string tenantId,
            [LogProperty] string orderId);

        [Log(MaxDepth = 0, Level = LogLevel.Debug)]
        void ProcessOrder(string orderId);

        [LogIgnore]
        string GetVersion();
    }
}
