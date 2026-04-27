namespace Dragonfire.Outbox.Models;

public enum DeliveryStatus
{
    Pending = 0,
    Success = 1,
    Failed = 2,
    DeadLettered = 3
}
