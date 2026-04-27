namespace InboxNet.Models;

public enum InboxHandlerStatus
{
    Pending = 0,
    Success = 1,
    Failed = 2,
    DeadLettered = 3
}
