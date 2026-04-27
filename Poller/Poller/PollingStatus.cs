namespace Poller
{
    // Domain/Enums/PollingStatus.cs
    public enum PollingStatus
    {
        Pending = 0,
        Polling = 1,
        Completed = 2,
        Failed = 3,
        TimedOut = 4,
        Cancelled = 5
    }
}
