namespace Dragonfire.Outbox.Models;

public enum MessageStatus
{
    Pending = 0,
    Processing = 1,
    Published = 2,
    Delivered = 3,
    Failed = 4,
    DeadLettered = 5
}
