using System.Diagnostics;

namespace OutboxNet.Observability;

public static class OutboxActivitySource
{
    public static readonly ActivitySource Source = new("OutboxNet", "1.0.0");
}
