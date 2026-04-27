using System.Diagnostics;

namespace Dragonfire.Outbox.Observability;

public static class OutboxActivitySource
{
    public static readonly ActivitySource Source = new("Dragonfire.Outbox", "1.0.0");
}
