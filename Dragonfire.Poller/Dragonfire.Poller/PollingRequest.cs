namespace Dragonfire.Poller
{
    public class PollingRequest<TRequestData, TResponseData>
    {
        public Guid Id { get; set; }
        public TRequestData RequestData { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public PollingConfiguration Configuration { get; set; } = new();
        public PollingStatus Status { get; set; }
        public List<PollingAttempt> Attempts { get; set; } = new();
        public TResponseData? Result { get; set; }
        public string? FailureReason { get; set; }
    }
}
