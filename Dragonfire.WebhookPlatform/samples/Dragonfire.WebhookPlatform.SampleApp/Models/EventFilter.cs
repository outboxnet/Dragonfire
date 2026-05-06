namespace Dragonfire.WebhookPlatform.SampleApp.Models;

/// <summary>
/// Bound from query string by the Outgoing/Incoming controllers. Combines two independent
/// filters: an exact <see cref="EventType"/> match and an optional SQL Server JSON path
/// lookup on the message payload — e.g. <c>JsonPath="$.customerId"</c>, <c>JsonValue="cust-1234"</c>
/// runs <c>JSON_VALUE(Payload, '$.customerId') = 'cust-1234'</c>. An empty filter passes
/// every row through unchanged. Both filters AND together when both are populated.
/// </summary>
public sealed class EventFilter
{
    public string? EventType { get; set; }
    public string? JsonPath { get; set; }
    public string? JsonValue { get; set; }

    public bool HasEventType => !string.IsNullOrWhiteSpace(EventType);
    public bool HasJsonFilter =>
        !string.IsNullOrWhiteSpace(JsonPath) && !string.IsNullOrWhiteSpace(JsonValue);
    public bool IsEmpty => !HasEventType && !HasJsonFilter;
}
