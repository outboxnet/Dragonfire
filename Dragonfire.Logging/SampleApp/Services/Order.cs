using Dragonfire.Logging.Attributes;

namespace SampleApp.Services
{
    [Log]
    public class Order
    {
        public string OrderId { get; set; }

        [LogProperty]
        public decimal Price { get; set; }

        [LogProperty]
        public string TenantId { get; set; }
    }
}
