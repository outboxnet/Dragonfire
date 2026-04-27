namespace Poller
{
    public class PollingAttempt
    {
        public int AttemptNumber { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }
}
