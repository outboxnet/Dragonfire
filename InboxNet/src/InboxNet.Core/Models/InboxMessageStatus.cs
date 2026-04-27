namespace InboxNet.Models;

public enum InboxMessageStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3,
    DeadLettered = 4
}
